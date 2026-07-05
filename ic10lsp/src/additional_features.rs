use std::collections::{HashMap, HashSet};
use tower_lsp::lsp_types::{
    CodeActionOrCommand, Diagnostic, DiagnosticSeverity, Position as LspPosition, Range as LspRange,
};
use tree_sitter::{Query, QueryCursor, Tree};

use crate::instructions::LOGIC_TYPES;
use crate::Range;

#[derive(Debug, Clone)]
pub struct OperationRecord {
    pub line_number: u32,
    pub operation: String, // "add temp temp 50"
}

#[derive(Debug, Clone)]
pub struct RegisterUsage {
    pub assignments: Vec<Range>,    // Where register is assigned values
    pub reads: Vec<Range>,          // Where register is read/used
    pub alias_name: Option<String>, // If register has an alias
    pub operation_history: Vec<OperationRecord>, // Simple history of operations
    pub value_kind: ValueKind,      // Tracked kind of value currently held
}
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ValueKind {
    Unknown,
    Number,
    DeviceId,
    LogicType,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RegisterState {
    Unused,           // Never assigned or read
    AssignedNotRead,  // Assigned but value never used
    ReadBeforeAssign, // Read before any assignment (error)
    Used,             // Properly assigned and read
}

// ---------------------------------------------------------------------------
// Control-flow analysis (definite/may assignment) for read-before-assign.
//
// IC10 programs are tiny (<= ~128 lines, ~18 registers), so a full per-
// instruction control-flow graph with iterative dataflow is effectively free
// (microseconds). We build a CFG, then run a forward "may be assigned"
// analysis (union over predecessors, no kill). A read is reported as
// read-before-assign ONLY when the register is provably *never* assigned on
// ANY path reaching the read. This is the sound, false-positive-free choice:
// it stays silent whenever some execution path could have assigned the value
// (loops, init-skips, jumps), at the cost of occasionally missing a bug that
// only manifests on one branch. For a linter that emits hard errors, silence
// on ambiguity beats crying wolf on correct code.
// ---------------------------------------------------------------------------

/// Where a jump/branch transfers control. Either a resolved 0-based source row
/// or "we can't tell" (computed register target, unresolved define, etc.).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum CfgTarget {
    Line(u32),
    Indeterminate,
}

/// How an instruction passes control to the next one.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum CfgControl {
    Seq,                 // normal instruction: falls through to next
    JumpAbs(CfgTarget),  // j: unconditional, absolute, no fallthrough
    JumpRel(CfgTarget),  // jr: unconditional, relative, no fallthrough
    BranchAbs(CfgTarget),// b*: conditional, absolute, target + fallthrough
    BranchRel(CfgTarget),// br*: conditional, relative, target + fallthrough
    Call(CfgTarget),     // jal: sets ra, target + fallthrough (subroutine return)
    Halt,                // hcf: no successors
}

/// One instruction in source order, distilled to what the dataflow needs.
struct CfgInstr {
    row: u32,                         // 0-based source row (for target resolution)
    reads: Vec<(String, Range)>,      // canonical register reads, in operand order
    writes: Vec<String>,              // canonical registers written
    control: CfgControl,
}

impl RegisterUsage {
    pub fn new() -> Self {
        Self {
            assignments: Vec::new(),
            reads: Vec::new(),
            alias_name: None,
            operation_history: Vec::new(),
            value_kind: ValueKind::Unknown,
        }
    }

    pub fn get_state(&self) -> RegisterState {
        if self.assignments.is_empty() && self.reads.is_empty() {
            RegisterState::Unused
        } else if !self.assignments.is_empty() && self.reads.is_empty() {
            RegisterState::AssignedNotRead
        } else if self.assignments.is_empty() && !self.reads.is_empty() {
            RegisterState::ReadBeforeAssign
        } else {
            // Check if any reads happen before first assignment
            // Sort assignments and reads by line number to ensure proper ordering
            let mut assignments_by_line: Vec<_> =
                self.assignments.iter().map(|r| r.0.start.line).collect();
            let mut reads_by_line: Vec<_> = self.reads.iter().map(|r| r.0.start.line).collect();

            assignments_by_line.sort();
            reads_by_line.sort();

            if let (Some(&first_assignment_line), Some(&first_read_line)) =
                (assignments_by_line.first(), reads_by_line.first())
            {
                if first_read_line < first_assignment_line {
                    RegisterState::ReadBeforeAssign
                } else {
                    RegisterState::Used
                }
            } else {
                RegisterState::Used
            }
        }
    }
}

pub struct RegisterAnalyzer {
    register_usage: HashMap<String, RegisterUsage>,
    alias_to_register: HashMap<String, String>, // alias -> register mapping for quick lookup
    ignored_registers: std::collections::HashSet<String>, // registers to suppress diagnostics for
    control_flow_enabled: bool,                 // use CFG-based read-before-assign analysis
    cfg_violations: Vec<(String, Range)>,       // (canonical register, read range) confirmed by CFG
}

// Helper function to recursively find identifier nodes within operands
fn find_identifier_in_node(node: tree_sitter::Node) -> Option<tree_sitter::Node> {
    if node.kind() == "identifier" {
        return Some(node);
    }
    
    let mut cursor = node.walk();
    for child in node.children(&mut cursor) {
        if let Some(found) = find_identifier_in_node(child) {
            return Some(found);
        }
    }
    None
}

// --- Control-flow helpers (free functions; no &self to avoid borrow conflicts) ---

/// Resolve a read operand to its canonical register name + the range to
/// underline. Mirrors `detect_register_reads` (check the operand's child, then
/// the operand itself, then any nested identifier for indirect references).
fn cfg_resolve_read_operand(
    operand: tree_sitter::Node,
    content: &str,
    aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
) -> Option<(String, Range)> {
    let nodes_to_check = if let Some(child) = operand.child(0) {
        vec![child, operand]
    } else {
        vec![operand]
    };
    for node in nodes_to_check {
        match node.kind() {
            "register" => {
                if let Ok(reg) = node.utf8_text(content.as_bytes()) {
                    return Some((reg.to_string(), Range::from(node.range())));
                }
            }
            "identifier" => {
                if let Ok(id) = node.utf8_text(content.as_bytes()) {
                    if let Some(alias_data) = aliases.get(id) {
                        if let crate::AliasValue::Register(reg) = &alias_data.value {
                            return Some((reg.clone(), Range::from(node.range())));
                        }
                    }
                }
            }
            _ => {
                if let Some(id_node) = find_identifier_in_node(node) {
                    if let Ok(id) = id_node.utf8_text(content.as_bytes()) {
                        if let Some(alias_data) = aliases.get(id) {
                            if let crate::AliasValue::Register(reg) = &alias_data.value {
                                return Some((reg.clone(), Range::from(id_node.range())));
                            }
                        }
                    }
                }
            }
        }
    }
    None
}

/// Resolve the first operand of an assignment instruction to the canonical
/// register it writes. Mirrors `detect_register_assignments`.
fn cfg_resolve_write_target(
    first_operand: tree_sitter::Node,
    content: &str,
    aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
) -> Option<String> {
    let child = first_operand.child(0)?;
    match child.kind() {
        "register" => child.utf8_text(content.as_bytes()).ok().map(|s| s.to_string()),
        "identifier" => {
            let id = child.utf8_text(content.as_bytes()).ok()?;
            if let Some(alias_data) = aliases.get(id) {
                if let crate::AliasValue::Register(reg) = &alias_data.value {
                    return Some(reg.clone());
                }
            }
            None
        }
        _ => None,
    }
}

/// Resolve a jump/branch target operand to an absolute source row (or
/// Indeterminate when it's computed/unresolvable).
fn cfg_resolve_target(
    operand: Option<&tree_sitter::Node>,
    relative: bool,
    row: u32,
    label_lines: &HashMap<String, u32>,
    content: &str,
) -> CfgTarget {
    let operand = match operand {
        Some(o) => *o,
        None => return CfgTarget::Indeterminate,
    };
    let node = operand.child(0).unwrap_or(operand);
    match node.kind() {
        "number" => {
            let txt = node.utf8_text(content.as_bytes()).unwrap_or("").trim();
            if let Ok(v) = txt.parse::<i64>() {
                let target = if relative { row as i64 + v } else { v };
                if target >= 0 {
                    return CfgTarget::Line(target as u32);
                }
            }
            CfgTarget::Indeterminate
        }
        "identifier" => {
            let txt = node.utf8_text(content.as_bytes()).unwrap_or("");
            match label_lines.get(txt) {
                Some(&lrow) => CfgTarget::Line(lrow),
                None => CfgTarget::Indeterminate, // alias-to-register or define constant
            }
        }
        // register / hash / anything else => computed target
        _ => CfgTarget::Indeterminate,
    }
}

/// Classify how an instruction transfers control, resolving its target.
fn cfg_classify_control(
    op_lower: &str,
    operands: &[tree_sitter::Node],
    row: u32,
    label_lines: &HashMap<String, u32>,
    content: &str,
) -> CfgControl {
    match op_lower {
        "hcf" => CfgControl::Halt,
        "j" => CfgControl::JumpAbs(cfg_resolve_target(operands.get(0), false, row, label_lines, content)),
        "jal" => CfgControl::Call(cfg_resolve_target(operands.get(0), false, row, label_lines, content)),
        "jr" => CfgControl::JumpRel(cfg_resolve_target(operands.get(0), true, row, label_lines, content)),
        _ => {
            // Every IC10 opcode beginning with 'b' is a conditional branch whose
            // last operand is the target. "br*" variants are PC-relative.
            if operands.is_empty() {
                CfgControl::Seq
            } else if op_lower.starts_with("br") {
                CfgControl::BranchRel(cfg_resolve_target(operands.last(), true, row, label_lines, content))
            } else if op_lower.starts_with('b') {
                CfgControl::BranchAbs(cfg_resolve_target(operands.last(), false, row, label_lines, content))
            } else {
                CfgControl::Seq
            }
        }
    }
}

impl RegisterAnalyzer {
    pub fn new() -> Self {
        Self {
            register_usage: HashMap::new(),
            alias_to_register: HashMap::new(),
            ignored_registers: std::collections::HashSet::new(),
            control_flow_enabled: true,
            cfg_violations: Vec::new(),
        }
    }

    /// Toggle control-flow-aware read-before-assign analysis. When disabled the
    /// analyzer falls back to the legacy textual (line-order) heuristic.
    pub fn set_control_flow_enabled(&mut self, enabled: bool) {
        self.control_flow_enabled = enabled;
    }

    fn ensure_register_entry(&mut self, name: &str) -> &mut RegisterUsage {
        self.register_usage
            .entry(name.to_string())
            .or_insert_with(RegisterUsage::new)
    }

    /// Check if a string is a valid register name (r0-r15, ra, sp, rr0-rr15)
    fn is_register_name(name: &str) -> bool {
        if name == "ra" || name == "sp" {
            return true;
        }
        // Check for r0-r15 pattern
        if name.len() >= 2 && name.starts_with('r') {
            if let Ok(num) = name[1..].parse::<u8>() {
                if num <= 15 {
                    return true;
                }
            }
        }
        // Check for rr0-rr15 pattern
        if name.len() >= 3 && name.starts_with("rr") {
            if let Ok(num) = name[2..].parse::<u8>() {
                if num <= 15 {
                    return true;
                }
            }
        }
        false
    }

    fn bootstrap_registers(&mut self) {
        for reg in ["sp", "ra"] {
            self.ensure_register_entry(reg);
        }

        if let Some(sp_usage) = self.register_usage.get_mut("sp") {
            if sp_usage.assignments.is_empty() {
                sp_usage.assignments.push(Range(LspRange::new(
                    LspPosition::new(0, 0),
                    LspPosition::new(0, 0),
                )));
            }
            if sp_usage.reads.is_empty() {
                sp_usage.reads.push(Range(LspRange::new(
                    LspPosition::new(0, 0),
                    LspPosition::new(0, 0),
                )));
            }
        }

        if let Some(ra_usage) = self.register_usage.get_mut("ra") {
            if ra_usage.assignments.is_empty() {
                ra_usage.assignments.push(Range(LspRange::new(
                    LspPosition::new(0, 0),
                    LspPosition::new(0, 0),
                )));
            }
        }
    }

    fn parse_ignore_directives(&mut self, content: &str) {
        // Parse comments like: # ignore r2, r5, r10 (with or without colon)
        // Also check for #IgnoreRegisterWarnings to suppress ALL register warnings
        let mut ignore_all_registers = false;
        
        for line in content.lines() {
            let trimmed = line.trim();
            if let Some(comment_start) = trimmed.find('#') {
                let comment = &trimmed[comment_start + 1..].trim().to_lowercase();
                
                // Check for #IgnoreRegisterWarnings directive (case-insensitive)
                if comment.starts_with("ignoreregisterwarnings") {
                    ignore_all_registers = true;
                    break; // No need to parse individual registers if ignoring all
                }
                
                // Look for ignore or ignore: directive
                if let Some(ignore_start) = comment.find("ignore") {
                    let after_ignore = &comment[ignore_start + 6..].trim();
                    // Skip optional colon
                    let registers_str = if after_ignore.starts_with(':') {
                        &after_ignore[1..].trim()
                    } else {
                        after_ignore
                    };
                    
                    // Split by comma and extract register names
                    for reg in registers_str.split(',') {
                        let reg_name = reg.trim();
                        if !reg_name.is_empty() {
                            self.ignored_registers.insert(reg_name.to_string());
                        }
                    }
                }
            }
        }
        
        // If #IgnoreRegisterWarnings found, add ALL registers to ignored list
        if ignore_all_registers {
            for reg in [
                "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9", "r10", "r11", "r12", "r13",
                "r14", "r15", "ra", "sp",
                "rr0", "rr1", "rr2", "rr3", "rr4", "rr5", "rr6", "rr7", "rr8", "rr9", "rr10", "rr11", "rr12", "rr13",
                "rr14", "rr15",
            ] {
                self.ignored_registers.insert(reg.to_string());
            }
        }
    }

    pub fn analyze_register_usage(
        &mut self,
        tree: &Tree,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) {
        self.register_usage.clear();
        self.alias_to_register.clear();
        self.ignored_registers.clear();
        self.cfg_violations.clear();

        // Parse ignore directives from comments
        self.parse_ignore_directives(content);

        // Initialize all known registers
        for reg in [
            "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9", "r10", "r11", "r12", "r13",
            "r14", "r15", "ra", "sp",
            "rr0", "rr1", "rr2", "rr3", "rr4", "rr5", "rr6", "rr7", "rr8", "rr9", "rr10", "rr11", "rr12", "rr13",
            "rr14", "rr15",
        ] {
            self.register_usage
                .insert(reg.to_string(), RegisterUsage::new());
        }

        // Add aliased registers and build alias mapping
        for (alias_name, alias_data) in aliases {
            if let crate::AliasValue::Register(reg_name) = &alias_data.value {
                let usage = self.ensure_register_entry(reg_name);
                if usage.alias_name.is_none() {
                    usage.alias_name = Some(alias_name.clone());
                }
                self.alias_to_register
                    .insert(alias_name.clone(), reg_name.clone());
            }
        }

        self.detect_register_assignments(tree, content, aliases);
        self.detect_register_reads(tree, content, aliases);
        self.detect_jal_ra_assignments(tree, content);
        self.track_operation_history(tree, content, aliases);
        self.detect_register_value_kinds(tree, content, aliases);
        self.fallback_line_scan(content, aliases); // resilience if tree-sitter patterns miss
        self.bootstrap_registers();
        self.mark_rr_as_used();
        // Control-flow-aware read-before-assign (cheap; always computed, used by
        // generate_diagnostics only when control_flow_enabled).
        self.compute_control_flow(tree, content, aliases);
    }

    fn detect_register_assignments(
        &mut self,
        tree: &Tree,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) {
        let mut cursor = QueryCursor::new();
        // Query for all instructions and manually check the first operand
        let instruction_query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction (operation) @op) @instruction",
        )
        .unwrap();

        let op_idx = instruction_query.capture_index_for_name("op").unwrap();
        let instruction_idx = instruction_query
            .capture_index_for_name("instruction")
            .unwrap();

        for query_match in cursor.matches(&instruction_query, tree.root_node(), content.as_bytes()) {
            let mut operation = None;
            let mut instruction_node = None;

            for cap in query_match.captures {
                if cap.index == op_idx {
                    operation = Some(cap.node.utf8_text(content.as_bytes()).unwrap());
                } else if cap.index == instruction_idx {
                    instruction_node = Some(cap.node);
                }
            }

            if let (Some(op), Some(inst_node)) = (operation, instruction_node) {
                if self.is_assignment_operation(op) {
                    // Get the first operand (target for assignment operations)
                    let mut tree_cursor = inst_node.walk();
                    let operands: Vec<_> = inst_node
                        .children_by_field_name("operand", &mut tree_cursor)
                        .collect();

                    if let Some(first_operand) = operands.first() {
                        if let Some(operand_child) = first_operand.child(0) {
                            match operand_child.kind() {
                                "register" => {
                                    let reg_name =
                                        operand_child.utf8_text(content.as_bytes()).unwrap();
                                    if reg_name == "r14" || reg_name == "r15" {
                                        eprintln!("DEBUG ASSIGN: Direct register {} at line {} op={}", 
                                            reg_name, inst_node.start_position().row + 1, operation.unwrap_or("?"));
                                    }
                                    let usage = self.ensure_register_entry(reg_name);
                                    usage.assignments.push(Range::from(operand_child.range()));
                                }
                                "identifier" => {
                                    let identifier =
                                        operand_child.utf8_text(content.as_bytes()).unwrap();
                                    if let Some(alias_data) = aliases.get(identifier) {
                                        if let crate::AliasValue::Register(reg_name) =
                                            &alias_data.value
                                        {
                                            if reg_name == "r14" || reg_name == "r15" {
                                                eprintln!("DEBUG ASSIGN: Via alias '{}' -> {} at line {} op={}", 
                                                    identifier, reg_name, inst_node.start_position().row + 1, operation.unwrap_or("?"));
                                            }
                                            let usage = self.ensure_register_entry(reg_name);
                                            usage
                                                .assignments
                                                .push(Range::from(operand_child.range()));
                                        }
                                    }
                                }
                                _ => {}
                            }
                        }
                    }
                }
            }
        }
    }

    fn mark_rr_as_used(&mut self) {
        let fabricated = LspRange::new(LspPosition::new(0, 0), LspPosition::new(0, 0));
        let fabricated_range = Range(fabricated);
        let keys: Vec<String> = self.register_usage.keys().cloned().collect();
        for k in keys {
            if k.starts_with("rr") {
                let usage = self.ensure_register_entry(&k);
                // rr registers (register references) are implicitly initialized
                // They always reference r0-r15 based on the rr number
                if usage.assignments.is_empty() {
                    usage.assignments.push(fabricated_range.clone());
                }
                if usage.reads.is_empty() {
                    usage.reads.push(fabricated_range.clone());
                }
            }
        }
    }

    fn detect_register_reads(
        &mut self,
        tree: &Tree,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) {
        let mut cursor = QueryCursor::new();

        // Query for all instructions and manually check operands
        let instruction_query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction (operation) @op) @instruction",
        )
        .unwrap();

        let op_idx = instruction_query.capture_index_for_name("op").unwrap();
        let instruction_idx = instruction_query
            .capture_index_for_name("instruction")
            .unwrap();

        for query_match in cursor.matches(&instruction_query, tree.root_node(), content.as_bytes()) {
            let mut operation = None;
            let mut instruction_node = None;

            for cap in query_match.captures {
                if cap.index == op_idx {
                    operation = Some(cap.node.utf8_text(content.as_bytes()).unwrap());
                } else if cap.index == instruction_idx {
                    instruction_node = Some(cap.node);
                }
            }

            if let (Some(op), Some(inst_node)) = (operation, instruction_node) {
                // Skip alias instruction entirely - it doesn't perform runtime reads
                if op.to_ascii_lowercase() == "alias" {
                    continue;
                }
                
                let mut tree_cursor = inst_node.walk();
                let operands: Vec<_> = inst_node
                    .children_by_field_name("operand", &mut tree_cursor)
                    .collect();

                // For assignment operations, skip the first operand (target)
                // For other operations, all operands are potential reads
                let start_idx = if self.is_assignment_operation(op) {
                    1
                } else {
                    0
                };

                for operand in operands.into_iter().skip(start_idx) {
                    // Try to get child node first (for complex operands), otherwise use operand itself
                    let nodes_to_check = if let Some(child) = operand.child(0) {
                        vec![child, operand] // Check child first, then operand as fallback
                    } else {
                        vec![operand] // Only check operand if no child
                    };
                    
                    for node_to_check in nodes_to_check {
                        match node_to_check.kind() {
                            "register" => {
                                let reg_name = node_to_check.utf8_text(content.as_bytes()).unwrap();
                                let usage = self.ensure_register_entry(reg_name);
                                usage.reads.push(Range::from(node_to_check.range()));
                                break; // Found a match, stop checking
                            }
                            "identifier" => {
                                let identifier = node_to_check.utf8_text(content.as_bytes()).unwrap();
                                if let Some(alias_data) = aliases.get(identifier) {
                                    if let crate::AliasValue::Register(reg_name) = &alias_data.value {
                                        let usage = self.ensure_register_entry(reg_name);
                                        usage.reads.push(Range::from(node_to_check.range()));
                                        break; // Found a match, stop checking
                                    }
                                }
                            }
                            _ => {
                                // Check if this operand contains an identifier that's a register alias
                                // (handles indirect device references like "l r0 refid Temperature")
                                if let Some(identifier_node) = find_identifier_in_node(node_to_check) {
                                    let identifier = identifier_node.utf8_text(content.as_bytes()).unwrap();
                                    if let Some(alias_data) = aliases.get(identifier) {
                                        if let crate::AliasValue::Register(reg_name) = &alias_data.value {
                                            let usage = self.ensure_register_entry(reg_name);
                                            usage.reads.push(Range::from(identifier_node.range()));
                                            break; // Found a match, stop checking
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    fn detect_jal_ra_assignments(&mut self, tree: &Tree, content: &str) {
        let mut cursor = QueryCursor::new();

        // Query for instructions and detect 'jal' case-insensitively so mixed-case source
        // (e.g. JAL, Jal) is still recognised as assigning to ra.
        let instruction_query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction (operation) @op) @instruction",
        )
        .unwrap();

        let op_idx = instruction_query.capture_index_for_name("op").unwrap();
        let instruction_idx = instruction_query
            .capture_index_for_name("instruction")
            .unwrap();

        for query_match in cursor.matches(&instruction_query, tree.root_node(), content.as_bytes()) {
            let mut operation = None;
            let mut instruction_node = None;

            for cap in query_match.captures {
                if cap.index == op_idx {
                    operation = Some(cap.node.utf8_text(content.as_bytes()).unwrap());
                } else if cap.index == instruction_idx {
                    instruction_node = Some(cap.node);
                }
            }

            if let (Some(op), Some(_inst_node)) = (operation, instruction_node) {
                if op.to_ascii_lowercase().as_str() == "jal" {
                    let usage = self.ensure_register_entry("ra");
                    // record the instruction range as an assignment to ra
                    // Using the first capture's node range is fine here
                    usage.assignments.push(Range::from(_inst_node.range()));
                }
            }
        }
    }

    fn is_assignment_operation(&self, operation: &str) -> bool {
        // Case-insensitive match for operations that assign to their first register operand
        let op = operation.to_ascii_lowercase();
        matches!(
            op.as_str(),
            "move" | "add" | "sub" | "mul" | "div" | "mod" | "max" | "min" |
            "abs" | "ceil" | "floor" | "round" | "sqrt" | "trunc" | "exp" | "log" |
            "sin" | "cos" | "tan" | "asin" | "acos" | "atan" | "atan2" |
            "and" | "or" | "xor" | "nor" | "not" | "sla" | "sll" | "sra" | "srl" |
            "l" | "lb" | "lr" | "ls" | "lbn" | "lbs" | "lbns" |
            "peek" | "pop" | "sap" | "sapz" |
            "sdns" | "sdse" | "select" | "seq" | "seqz" | "sge" | "sgez" |
            "sgt" | "sgtz" | "sle" | "slez" | "slt" | "sltz" | "sna" | "snaz" |
            "sne" | "snez" | "snan" | "snanz" | "rget" |
            // Additional load/generate operations that assign to first register
            "get" | "getd" | "ld" | "rmap" | "rand" | "pow" | "ext" | "ins" | "lerp"
        )
    }

    /// Build a per-instruction control-flow graph and run a forward "may be
    /// assigned" dataflow. Fills `self.cfg_violations` with reads of registers
    /// that are provably never assigned on ANY path reaching them.
    fn compute_control_flow(
        &mut self,
        tree: &Tree,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) {
        let bytes = content.as_bytes();
        let root = tree.root_node();

        // 1. Map every label name to its 0-based source row.
        let mut label_lines: HashMap<String, u32> = HashMap::new();
        if let Ok(label_query) =
            Query::new(tree_sitter_ic10::language(), "(label (identifier) @name)")
        {
            let mut cursor = QueryCursor::new();
            if let Some(name_idx) = label_query.capture_index_for_name("name") {
                for m in cursor.matches(&label_query, root, bytes) {
                    for cap in m.captures {
                        if cap.index == name_idx {
                            if let Ok(name) = cap.node.utf8_text(bytes) {
                                label_lines
                                    .entry(name.to_string())
                                    .or_insert(cap.node.start_position().row as u32);
                            }
                        }
                    }
                }
            }
        }

        // 2. Collect instructions in source order.
        let instr_query = match Query::new(
            tree_sitter_ic10::language(),
            "(instruction (operation) @op) @instruction",
        ) {
            Ok(q) => q,
            Err(_) => return,
        };
        let op_idx = instr_query.capture_index_for_name("op").unwrap();
        let inst_idx = instr_query.capture_index_for_name("instruction").unwrap();

        let mut raw: Vec<(String, tree_sitter::Node)> = Vec::new();
        {
            let mut cursor = QueryCursor::new();
            for m in cursor.matches(&instr_query, root, bytes) {
                let mut op_text = None;
                let mut inst_node = None;
                for cap in m.captures {
                    if cap.index == op_idx {
                        op_text = cap.node.utf8_text(bytes).ok();
                    } else if cap.index == inst_idx {
                        inst_node = Some(cap.node);
                    }
                }
                if let (Some(op), Some(node)) = (op_text, inst_node) {
                    raw.push((op.to_string(), node));
                }
            }
        }
        raw.sort_by_key(|(_, n)| (n.start_position().row, n.start_position().column));
        if raw.is_empty() {
            return;
        }

        // 3. Distill each instruction into reads / writes / control transfer.
        let mut instrs: Vec<CfgInstr> = Vec::with_capacity(raw.len());
        for (op, inst) in &raw {
            let row = inst.start_position().row as u32;
            let op_lower = op.to_ascii_lowercase();
            let mut tc = inst.walk();
            let operands: Vec<tree_sitter::Node> =
                inst.children_by_field_name("operand", &mut tc).collect();

            let mut writes: Vec<String> = Vec::new();
            let mut reads: Vec<(String, Range)> = Vec::new();

            let is_assign = self.is_assignment_operation(op);
            if is_assign {
                if let Some(first) = operands.first() {
                    if let Some(reg) = cfg_resolve_write_target(*first, content, aliases) {
                        writes.push(reg);
                    }
                }
            }
            if op_lower == "jal" {
                writes.push("ra".to_string());
            }

            let start_idx = if is_assign { 1 } else { 0 };
            for operand in operands.iter().skip(start_idx) {
                if let Some(rd) = cfg_resolve_read_operand(*operand, content, aliases) {
                    reads.push(rd);
                }
            }

            let control = cfg_classify_control(&op_lower, &operands, row, &label_lines, content);
            instrs.push(CfgInstr { row, reads, writes, control });
        }

        let n = instrs.len();

        // 4. Build successor edges. Targets resolve to the first instruction at
        //    or after the target row; indeterminate targets reach everywhere.
        let find_index = |target_row: u32| -> Option<usize> {
            instrs.iter().position(|ci| ci.row >= target_row)
        };
        let mut succ: Vec<Vec<usize>> = vec![Vec::new(); n];
        for i in 0..n {
            let next = if i + 1 < n { Some(i + 1) } else { None };
            let add_target = |list: &mut Vec<usize>, t: CfgTarget| match t {
                CfgTarget::Line(row) => {
                    if let Some(idx) = find_index(row) {
                        list.push(idx);
                    }
                }
                CfgTarget::Indeterminate => {
                    for j in 0..n {
                        list.push(j);
                    }
                }
            };
            match instrs[i].control {
                CfgControl::Seq => {
                    if let Some(nx) = next {
                        succ[i].push(nx);
                    }
                }
                CfgControl::Halt => {}
                CfgControl::JumpAbs(t) | CfgControl::JumpRel(t) => {
                    let mut list = std::mem::take(&mut succ[i]);
                    add_target(&mut list, t);
                    succ[i] = list;
                }
                CfgControl::BranchAbs(t) | CfgControl::BranchRel(t) | CfgControl::Call(t) => {
                    let mut list = std::mem::take(&mut succ[i]);
                    if let Some(nx) = next {
                        list.push(nx);
                    }
                    add_target(&mut list, t);
                    succ[i] = list;
                }
            }
        }

        // 5. Predecessors + reachability from entry (instruction 0).
        let mut preds: Vec<Vec<usize>> = vec![Vec::new(); n];
        for i in 0..n {
            for &s in &succ[i] {
                preds[s].push(i);
            }
        }
        let mut reachable = vec![false; n];
        let mut stack = vec![0usize];
        reachable[0] = true;
        while let Some(u) = stack.pop() {
            for &v in &succ[u] {
                if !reachable[v] {
                    reachable[v] = true;
                    stack.push(v);
                }
            }
        }

        // 6. Entry set: registers that are implicitly initialized at startup.
        let mut entry: HashSet<String> = HashSet::new();
        entry.insert("sp".to_string());
        entry.insert("ra".to_string());
        for k in 0..=15 {
            entry.insert(format!("rr{}", k));
        }

        // 7. Forward "may be assigned" dataflow to fixpoint (monotone: sets only grow).
        let writes_sets: Vec<HashSet<String>> = instrs
            .iter()
            .map(|ci| ci.writes.iter().cloned().collect())
            .collect();
        let mut may: Vec<HashSet<String>> = vec![HashSet::new(); n];
        loop {
            let mut changed = false;
            for i in 0..n {
                let mut incoming: HashSet<String> = HashSet::new();
                if i == 0 {
                    incoming.extend(entry.iter().cloned());
                }
                for &p in &preds[i] {
                    // out(p) = may[p] ∪ writes[p]
                    for r in &may[p] {
                        incoming.insert(r.clone());
                    }
                    for r in &writes_sets[p] {
                        incoming.insert(r.clone());
                    }
                }
                for r in incoming {
                    if may[i].insert(r) {
                        changed = true;
                    }
                }
            }
            if !changed {
                break;
            }
        }

        // 8. A read is a confirmed violation when the register is not in the
        //    instruction's IN set (never assigned on any path reaching it).
        for i in 0..n {
            if !reachable[i] {
                continue; // dead code: never executes, don't flag
            }
            for (reg, range) in &instrs[i].reads {
                if reg == "sp" || reg == "ra" || reg.starts_with("rr") {
                    continue;
                }
                if self.ignored_registers.contains(reg) {
                    continue;
                }
                if !may[i].contains(reg) {
                    self.cfg_violations.push((reg.clone(), *range));
                }
            }
        }
    }

    pub fn generate_diagnostics(&self) -> Vec<Diagnostic> {
        let mut diagnostics = Vec::new();

        // DEBUG: Print ALL HashMap keys to find phantom entries
        eprintln!("DEBUG HASHMAP KEYS: {:?}", self.register_usage.keys().collect::<Vec<_>>());

        for (register_name, usage) in &self.register_usage {
            // Debug: log the state for the problematic registers
            if register_name == "r14" || register_name == "r15" {
                eprintln!("DEBUG FINAL: {} - assignments: {}, reads: {}, state: {:?}, alias: {:?}", 
                    register_name, 
                    usage.assignments.len(), 
                    usage.reads.len(),
                    usage.get_state(),
                    usage.alias_name
                );
            }
            
            // Skip registers that are in the ignore list
            if self.ignored_registers.contains(register_name) {
                continue;
            }
            
            match usage.get_state() {
                RegisterState::Unused => {
                    // Only warn about unused aliases, not bare registers
                    if let Some(_alias_name) = &usage.alias_name {
                        // We don't have the alias definition range here, would need to pass it
                        // This will be handled when integrating with main diagnostics
                    }
                }
                RegisterState::AssignedNotRead => {
                    // Suppress assigned-but-never-read for rr* registers (register reference)
                    if register_name.starts_with("rr") {
                        continue;
                    }
                    // Suppress for return address register 'ra' as many scripts never read it explicitly
                    if register_name == "ra" {
                        continue;
                    }
                    for assignment_range in &usage.assignments {
                        let register_display = usage
                            .alias_name
                            .as_ref()
                            .map(|alias| format!("'{}' ({})", alias, register_name))
                            .unwrap_or_else(|| register_name.clone());

                        eprintln!("DEBUG DIAGNOSTIC: AssignedNotRead for {} at range {:?} (reads={})", 
                            register_display, assignment_range, usage.reads.len());

                        diagnostics.push(Diagnostic {
                            range: assignment_range.clone().into(),
                            severity: Some(DiagnosticSeverity::WARNING),
                            code: Some(tower_lsp::lsp_types::NumberOrString::String("register_assigned_not_read".to_string())),
                            message: format!("Register {} is assigned but never read. Consider removing to optimize register usage.", register_display),
                            data: Some(serde_json::json!(register_name)),
                            ..Default::default()
                        });
                    }
                }
                RegisterState::ReadBeforeAssign => {
                    // When control-flow analysis is on, read-before-assign is
                    // decided by the CFG pass below (which accounts for jumps,
                    // branches and loops) rather than this textual heuristic.
                    if self.control_flow_enabled {
                        continue;
                    }
                    // Do not flag stack pointer, return address, or rr registers as read-before-assign; they're implicitly initialized
                    if register_name == "sp" || register_name == "ra" || register_name.starts_with("rr") {
                        continue;
                    }
                    for read_range in &usage.reads {
                        let register_display = usage
                            .alias_name
                            .as_ref()
                            .map(|alias| format!("'{}' ({})", alias, register_name))
                            .unwrap_or_else(|| register_name.clone());

                        eprintln!("DEBUG DIAGNOSTIC: ReadBeforeAssign for {} at range {:?}", register_display, read_range);

                        diagnostics.push(Diagnostic {
                            range: read_range.clone().into(),
                            severity: Some(DiagnosticSeverity::ERROR),
                            code: Some(tower_lsp::lsp_types::NumberOrString::String("register_read_before_assign".to_string())),
                            message: format!(
                                "Register {} is read before being assigned a value.",
                                register_display
                            ),
                            data: Some(serde_json::json!(register_name)),
                            ..Default::default()
                        });
                    }
                }
                RegisterState::Used => {
                    // No diagnostic needed for properly used registers
                }
            }
        }

        // Control-flow-aware read-before-assign: emit only reads of registers
        // proven to be assigned on NO path that reaches them. This both removes
        // false positives from the textual heuristic (loops, init-skips, forward
        // jumps) and pinpoints the exact reads at fault.
        if self.control_flow_enabled {
            for (register_name, range) in &self.cfg_violations {
                if register_name == "sp"
                    || register_name == "ra"
                    || register_name.starts_with("rr")
                {
                    continue;
                }
                if self.ignored_registers.contains(register_name) {
                    continue;
                }
                let register_display = self
                    .register_usage
                    .get(register_name)
                    .and_then(|u| u.alias_name.clone())
                    .map(|alias| format!("'{}' ({})", alias, register_name))
                    .unwrap_or_else(|| register_name.clone());

                diagnostics.push(Diagnostic {
                    range: (*range).into(),
                    severity: Some(DiagnosticSeverity::ERROR),
                    code: Some(tower_lsp::lsp_types::NumberOrString::String(
                        "register_read_before_assign".to_string(),
                    )),
                    message: format!(
                        "Register {} is read before being assigned a value.",
                        register_display
                    ),
                    data: Some(serde_json::json!(register_name)),
                    ..Default::default()
                });
            }
        }

        diagnostics
    }

    fn track_operation_history(
        &mut self,
        tree: &Tree,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) {
        let mut cursor = QueryCursor::new();

        // Query for all instructions in order
        let instruction_query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction (operation) @op) @instruction",
        )
        .unwrap();

        let op_idx = instruction_query.capture_index_for_name("op").unwrap();
        let instruction_idx = instruction_query
            .capture_index_for_name("instruction")
            .unwrap();

        for query_match in cursor.matches(&instruction_query, tree.root_node(), content.as_bytes()) {
            let mut operation = None;
            let mut instruction_node = None;

            for cap in query_match.captures {
                if cap.index == op_idx {
                    operation = Some(cap.node.utf8_text(content.as_bytes()).unwrap());
                } else if cap.index == instruction_idx {
                    instruction_node = Some(cap.node);
                }
            }

            if let (Some(op), Some(inst_node)) = (operation, instruction_node) {
                // Always record usage against all registers seen in operands
                self.add_operation_usage_for_all_register_operands(inst_node, content, aliases);
                // Additionally record assignment-specific history for target register
                self.add_operation_to_history(op, inst_node, content, aliases);
            }
        }
    }

    fn set_kind(&mut self, reg: &str, kind: ValueKind) {
        if reg.is_empty() {
            return;
        }
        let usage = self.ensure_register_entry(reg);
        usage.value_kind = kind;
    }

    #[allow(dead_code)]
    fn get_operand_text<'a>(
        &self,
        node: &tree_sitter::Node<'a>,
        content: &'a str,
    ) -> Option<&'a str> {
        node.child(0)
            .map(|c| c.utf8_text(content.as_bytes()).ok())
            .flatten()
    }

    fn detect_register_value_kinds(
        &mut self,
        tree: &Tree,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) {
        let mut cursor = QueryCursor::new();
        let instruction_query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction (operation) @op) @instruction",
        )
        .unwrap();

        let op_idx = instruction_query.capture_index_for_name("op").unwrap();
        let instruction_idx = instruction_query
            .capture_index_for_name("instruction")
            .unwrap();

        for query_match in cursor.matches(&instruction_query, tree.root_node(), content.as_bytes()) {
            let mut operation: Option<&str> = None;
            let mut instruction_node: Option<tree_sitter::Node> = None;

            for cap in query_match.captures {
                if cap.index == op_idx {
                    operation = Some(cap.node.utf8_text(content.as_bytes()).unwrap());
                } else if cap.index == instruction_idx {
                    instruction_node = Some(cap.node);
                }
            }

            let Some(op) = operation else {
                continue;
            };
            let Some(inst) = instruction_node else {
                continue;
            };

            // Collect operands
            let mut tree_cursor = inst.walk();
            let operands: Vec<_> = inst
                .children_by_field_name("operand", &mut tree_cursor)
                .collect();
            if operands.is_empty() {
                continue;
            }

            // Helpers
            let target_reg = self.get_register_from_operand(&operands[0], content, aliases);
            let op_lc = op.to_lowercase();

            match op_lc.as_str() {
                // Loads
                "l" | "ld" | "lb" | "lbn" => {
                    // Scan subsequent operands to find the logic type token irrespective of exact position
                    let mut saw_logic = false;
                    let mut saw_reference = false;
                    for operand in operands.iter().skip(1) {
                        if let Some(kind_node) = operand.child(0) {
                            // Only check LogicType tokens, not identifiers (which could be aliases)
                            if kind_node.kind() == "logictype" {
                                let lt = kind_node.utf8_text(content.as_bytes()).unwrap_or("");
                                // Record that a logic token was found; classify specifically if ReferenceId
                                if lt.eq_ignore_ascii_case("ReferenceId") {
                                    saw_reference = true;
                                }
                                // Mark as logic when matches any known logic type keyword set
                                if LOGIC_TYPES.contains(lt) {
                                    saw_logic = true;
                                }
                            }
                        }
                    }
                    if !target_reg.is_empty() {
                        if saw_reference {
                            self.set_kind(&target_reg, ValueKind::DeviceId);
                        } else if saw_logic {
                            self.set_kind(&target_reg, ValueKind::Number);
                        }
                    }
                }
                // Pass-through: simple unary ops copy DeviceId if operand is DeviceId
                "move" | "alias" => { /* handled above or ignored here */ }
                "abs" | "ceil" | "floor" | "round" | "sqrt" | "trunc" => {
                    if operands.len() >= 2 {
                        if let Some(src) = operands[1].child(0) {
                            if src.kind() == "register" {
                                let src_reg = src.utf8_text(content.as_bytes()).unwrap_or("");
                                if let Some(kind) =
                                    self.register_usage.get(src_reg).map(|u| u.value_kind)
                                {
                                    if kind == ValueKind::DeviceId || kind == ValueKind::LogicType {
                                        // preserve device id and logictype through pure unary ops
                                        if !target_reg.is_empty() {
                                            self.set_kind(&target_reg, kind);
                                        }
                                        continue;
                                    }
                                }
                            }
                        }
                    }
                    if !target_reg.is_empty() {
                        self.set_kind(&target_reg, ValueKind::Number);
                    }
                }
                // Explicit numeric generating ops
                // move already handled earlier; no case needed here to avoid unreachable pattern warning
                // get/getd read from device slots or network channels and can return any type of data
                // pop/peek read from the stack and can return any type of data
                // We treat them as Unknown so they can be used as numbers, device IDs, or other values
                "get" | "getd" | "pop" | "peek" => {
                    // Leave as Unknown - don't set a specific kind
                    // This allows the register to be used flexibly as the runtime value could be anything
                }
                "add" | "sub" | "mul" | "div" | "mod" | "max" | "min" | "and" | "or" | "xor" | "nor" => {
                    // Arithmetic/logical operations: always produce Number even if inputs include LogicType constants
                    if !target_reg.is_empty() {
                        self.set_kind(&target_reg, ValueKind::Number);
                    }
                }
                _ => {
                    // Arithmetic and others -> Number
                    if !target_reg.is_empty() && self.is_assignment_operation(op) {
                        self.set_kind(&target_reg, ValueKind::Number);
                    }
                }
            }
        }
    }

    // Fallback textual scan to reinforce ValueKind propagation & assignment detection when
    // parsing nuances (or grammar drift) prevent earlier pattern-based detection. This keeps
    // tests and runtime behavior robust.
    fn fallback_line_scan(
        &mut self,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) {
        for (idx, line) in content.lines().enumerate() {
            let tokens: Vec<&str> = line.split_whitespace().collect();
            if tokens.is_empty() {
                continue;
            }
            match tokens[0].to_ascii_lowercase().as_str() {
                "l" => {
                    if tokens.len() >= 4 {
                        let target = tokens[1];
                        // CRITICAL: Only process direct register names, skip aliases
                        if !Self::is_register_name(target) {
                            continue;
                        }
                        let logic = tokens[3];
                        let usage = self.ensure_register_entry(target);
                        if logic.eq_ignore_ascii_case("ReferenceId") {
                            usage.value_kind = ValueKind::DeviceId;
                        } else if matches!(usage.value_kind, ValueKind::Unknown) {
                            usage.value_kind = ValueKind::Number;
                        }
                    }
                }
                "move" => {
                    if tokens.len() >= 3 {
                        let dst = tokens[1];
                        let src = tokens[2];
                        
                        // CRITICAL: Only process direct register names, skip aliases
                        if !Self::is_register_name(dst) {
                            continue;
                        }
                        
                        // Check if source is a LogicType or SlotLogicType identifier
                        if src.contains("LogicType.") || src.contains("SlotLogicType.") {
                            self.ensure_register_entry(dst).value_kind = ValueKind::LogicType;
                        } else if Self::is_register_name(src) {
                            // Direct register-to-register move
                            let src_kind = self
                                .register_usage
                                .get(src)
                                .map(|u| u.value_kind)
                                .unwrap_or(ValueKind::Unknown);
                            self.ensure_register_entry(dst).value_kind = src_kind;
                        } else if let Some(crate::AliasValue::Register(src_reg)) =
                            aliases.get(src).map(|d| &d.value)
                        {
                            // move from an alias of a register: propagate that register's kind
                            let src_kind = self
                                .register_usage
                                .get(src_reg)
                                .map(|u| u.value_kind)
                                .unwrap_or(ValueKind::Unknown);
                            self.ensure_register_entry(dst).value_kind = src_kind;
                        }
                    }
                }
                "get" | "getd" | "pop" | "peek" => {
                    if tokens.len() >= 2 {
                        let operand = tokens[1];
                        
                        eprintln!("DEBUG FALLBACK: Line {} op={} operand='{}' starts_with_r={}", 
                            idx + 1, tokens[0], operand, operand.starts_with('r'));
                        
                        // CRITICAL: Only process register names (r0-r15, etc), never alias names
                        // Aliases are compile-time only and shouldn't have runtime entries
                        if !Self::is_register_name(operand) {
                            eprintln!("DEBUG FALLBACK: Skipping non-register operand '{}'", operand);
                            // This is an alias or other identifier - skip it entirely
                            // The tree-sitter detection already handles aliases by resolving them to registers
                            continue;
                        }
                        
                        // Direct register name - check if tree-sitter already detected it
                        if let Some(ru) = self.register_usage.get(operand) {
                            if !ru.assignments.is_empty() {
                                eprintln!("DEBUG FALLBACK: Tree-sitter already detected {} (skipping)", operand);
                                continue; // Tree-sitter already detected this
                            }
                        }
                        
                        // Tree-sitter missed this direct register - add fallback assignment
                        eprintln!("DEBUG FALLBACK: Adding fallback assignment for {}", operand);
                        let ru = self.ensure_register_entry(operand);
                        if ru.assignments.is_empty() {
                            let fabricated = LspRange::new(
                                LspPosition::new(idx as u32, 0),
                                LspPosition::new(idx as u32, 0),
                            );
                            ru.assignments.push(Range(fabricated));
                        }
                    }
                }
                "add" | "sub" | "mul" | "div" | "mod" | "max" | "min" => {
                    // Arithmetic operations: check if any operand is a LogicType constant
                    if tokens.len() >= 3 {
                        let dst = tokens[1];
                        // CRITICAL: Only process direct register names, skip aliases
                        if !Self::is_register_name(dst) {
                            continue;
                        }
                        // If source contains LogicType, the operation produces a Number
                        let has_logictype = tokens[2..].iter().any(|t| t.contains("LogicType.") || t.contains("SlotLogicType."));
                        if has_logictype {
                            self.ensure_register_entry(dst).value_kind = ValueKind::Number;
                        }
                    }
                }
                _ => {}
            }
        }
    }

    fn add_operation_to_history(
        &mut self,
        operation: &str,
        instruction_node: tree_sitter::Node,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) {
        if !self.is_assignment_operation(operation) {
            return;
        }

        let mut tree_cursor = instruction_node.walk();
        let operands: Vec<_> = instruction_node
            .children_by_field_name("operand", &mut tree_cursor)
            .collect();

        if operands.is_empty() {
            return;
        }

        // Get target register (first operand)
        let target_register = self.get_register_from_operand(&operands[0], content, aliases);
        if target_register.is_empty() {
            return;
        }

        let line_number = instruction_node.start_position().row as u32 + 1;
        let instruction_text = instruction_node.utf8_text(content.as_bytes()).unwrap_or("");

        // Update register usage with simple operation history
        let usage = self.ensure_register_entry(&target_register);
        // Avoid duplicate entries for the same line
        let should_add_record = usage
            .operation_history
            .last()
            .map_or(true, |last_record| last_record.line_number != line_number);

        if should_add_record {
            usage.operation_history.push(OperationRecord {
                line_number,
                operation: instruction_text.to_string(),
            });
        }
    }

    /// Record this instruction against every register that appears in its operands (reads or target).
    /// This broadens history tracking so new/less common opcodes are still reflected automatically.
    fn add_operation_usage_for_all_register_operands(
        &mut self,
        instruction_node: tree_sitter::Node,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) {
        let mut tree_cursor = instruction_node.walk();
        let operands: Vec<_> = instruction_node
            .children_by_field_name("operand", &mut tree_cursor)
            .collect();
        if operands.is_empty() {
            return;
        }
        let line_number = instruction_node.start_position().row as u32 + 1;
        let instruction_text = instruction_node.utf8_text(content.as_bytes()).unwrap_or("");

        for opnd in operands {
            let reg = self.get_register_from_operand(&opnd, content, aliases);
            if reg.is_empty() {
                continue;
            }
            let usage = self.ensure_register_entry(&reg);
            let should_add = usage
                .operation_history
                .last()
                .map_or(true, |last| last.line_number != line_number);
            if should_add {
                usage.operation_history.push(OperationRecord {
                    line_number,
                    operation: instruction_text.to_string(),
                });
            }
        }
    }

    fn get_register_from_operand(
        &self,
        operand: &tree_sitter::Node,
        content: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) -> String {
        if let Some(operand_child) = operand.child(0) {
            match operand_child.kind() {
                "register" => operand_child
                    .utf8_text(content.as_bytes())
                    .unwrap_or("")
                    .to_string(),
                "identifier" => {
                    let identifier = operand_child.utf8_text(content.as_bytes()).unwrap_or("");
                    if let Some(alias_data) = aliases.get(identifier) {
                        if let crate::AliasValue::Register(reg_name) = &alias_data.value {
                            reg_name.clone()
                        } else {
                            String::new()
                        }
                    } else {
                        String::new()
                    }
                }
                _ => String::new(),
            }
        } else {
            String::new()
        }
    }

    #[allow(dead_code)]
    pub fn get_register_usage(&self) -> &HashMap<String, RegisterUsage> {
        &self.register_usage
    }

    pub fn get_register_info(&self, register_or_alias: &str) -> Option<&RegisterUsage> {
        // Try direct register lookup first
        if let Some(usage) = self.register_usage.get(register_or_alias) {
            return Some(usage);
        }

        // Try alias lookup
        if let Some(register_name) = self.alias_to_register.get(register_or_alias) {
            return self.register_usage.get(register_name);
        }

        None
    }

    pub fn get_register_kind(&self, register_or_alias: &str) -> ValueKind {
        if let Some(info) = self.get_register_info(register_or_alias) {
            return info.value_kind;
        }
        ValueKind::Unknown
    }
}

/// Code Actions for enhanced interactivity with instructions
pub fn get_instruction_code_actions(
    _node: &tree_sitter::Node,
    _content: &str,
) -> Option<Vec<CodeActionOrCommand>> {
    // Code Actions for instruction exploration have been removed per user request
    // The lightbulb hint remains in hover tooltips to guide users to other Code Actions
    None
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashMap;
    use tree_sitter::Parser;

    fn analyze(
        source: &str,
        aliases: &HashMap<String, crate::DefinitionData<crate::AliasValue>>,
    ) -> RegisterAnalyzer {
        let mut parser = Parser::new();
        parser.set_language(tree_sitter_ic10::language()).unwrap();
        let tree = parser.parse(source, None).unwrap();
        let mut ra = RegisterAnalyzer::new();
        ra.analyze_register_usage(&tree, source, aliases);
        ra
    }

    #[test]
    fn stack_pointer_defaults_to_used() {
        let aliases = HashMap::new();
        let ra = analyze("", &aliases);
        let sp_state = ra.get_register_info("sp").unwrap().get_state();
        assert_eq!(sp_state, RegisterState::Used);
    }

    #[test]
    fn ra_reads_allowed_before_textual_assign() {
        let aliases = HashMap::new();
        let src = "Travel:\n    j ra\n";
        let ra = analyze(src, &aliases);
        let ra_state = ra.get_register_info("ra").unwrap().get_state();
        assert_eq!(ra_state, RegisterState::Used);
    }

    #[test]
    fn rr_registers_are_tracked() {
        let aliases = HashMap::new();
        let src = "move rr5 1\npush rr5\n";
        let ra = analyze(src, &aliases);
        let info = ra.get_register_info("rr5").unwrap();
        assert_eq!(info.get_state(), RegisterState::Used);
    }

    #[test]
    fn reference_id_load_sets_deviceid() {
        let src = "l r1 d0 ReferenceId\n";
        let aliases = HashMap::new();
        let ra = analyze(src, &aliases);
        assert_eq!(ra.get_register_kind("r1"), ValueKind::DeviceId);
    }

    #[test]
    fn reference_id_load_lb_sets_deviceid() {
        // lb form: lb rX typeHash logicType batchMode
        // Using 0 as dummy hash, ReferenceId logic type should mark target register as DeviceId
        let src = "lb r2 0 ReferenceId Average\n";
        let aliases = HashMap::new();
        let ra = analyze(src, &aliases);
        assert_eq!(ra.get_register_kind("r2"), ValueKind::DeviceId);
    }

    #[test]
    fn reference_id_load_lbn_sets_deviceid() {
        // lbn form: lbn rX typeHash nameHash logicType batchMode
        let src = "lbn r3 0 0 ReferenceId Average\n";
        let aliases = HashMap::new();
        let ra = analyze(src, &aliases);
        assert_eq!(ra.get_register_kind("r3"), ValueKind::DeviceId);
    }

    #[test]
    fn move_propagates_deviceid() {
        let src = "l r1 d0 ReferenceId\nmove r2 r1\n";
        let aliases = HashMap::new();
        let ra = analyze(src, &aliases);
        assert_eq!(ra.get_register_kind("r2"), ValueKind::DeviceId);
    }

    #[test]
    fn move_from_alias_propagates_deviceid() {
        let src = "l r1 d0 ReferenceId\nmove r3 foo\n";
        let mut aliases: HashMap<String, crate::DefinitionData<crate::AliasValue>> = HashMap::new();
        aliases.insert(
            "foo".to_string(),
            crate::DefinitionData::new(
                Range(tower_lsp::lsp_types::Range::default()),
                crate::AliasValue::Register("r1".to_string()),
            ),
        );
        let ra = analyze(src, &aliases);
        assert_eq!(ra.get_register_kind("r3"), ValueKind::DeviceId);
    }

    #[test]
    fn arithmetic_coerces_to_number() {
        let src = "l r1 d0 ReferenceId\nadd r4 r1 1\n";
        let aliases = HashMap::new();
        let ra = analyze(src, &aliases);
        assert_eq!(ra.get_register_kind("r4"), ValueKind::Number);
    }

    #[test]
    fn get_is_assignment() {
        let src = "get r5 d0 0\n";
        let aliases = HashMap::new();
        let ra = analyze(src, &aliases);
        let info = ra.get_register_info("r5").unwrap();
        assert!(info.assignments.len() >= 1);
    }

    #[test]
    fn rr15_assigned_not_read_is_suppressed() {
        let aliases = HashMap::new();
        let src = "move rr15 1\n";
        let mut parser = Parser::new();
        parser.set_language(tree_sitter_ic10::language()).unwrap();
        let tree = parser.parse(src, None).unwrap();
        let mut ra = RegisterAnalyzer::new();
        ra.analyze_register_usage(&tree, src, &aliases);
        let diags = ra.generate_diagnostics();
        assert!(diags.iter().all(|d| !d.message.contains("rr15")));
    }

    #[test]
    fn get_db_assigns_before_branch_read() {
        let aliases = HashMap::new();
        let src = "yield\nget r12 db 12\nbeqz r12 InitWaitLoop\n";
        let mut parser = Parser::new();
        parser.set_language(tree_sitter_ic10::language()).unwrap();
        let tree = parser.parse(src, None).unwrap();
        let mut ra = RegisterAnalyzer::new();
        ra.analyze_register_usage(&tree, src, &aliases);
        let info = ra.get_register_info("r12").unwrap();
        
        // Debug: verify assignments are being tracked
        assert!(!info.assignments.is_empty(), "get should record an assignment to r12");
        assert!(!info.reads.is_empty(), "beqz should record a read of r12");
        assert!(!info.operation_history.is_empty(), "should have operation history");
        
        assert_eq!(info.get_state(), RegisterState::Used);
    }
    
    #[test]
    fn get_db_with_label_assigns_before_branch_read() {
        let aliases = HashMap::new();
        let src = "InitWaitLoop:\n    yield\n    get r12 db 12\n    beqz r12 InitWaitLoop\n";
        let mut parser = Parser::new();
        parser.set_language(tree_sitter_ic10::language()).unwrap();
        let tree = parser.parse(src, None).unwrap();
        let mut ra = RegisterAnalyzer::new();
        ra.analyze_register_usage(&tree, src, &aliases);
        let info = ra.get_register_info("r12").unwrap();
        
        assert!(!info.assignments.is_empty(), "get should record an assignment to r12 even with label");
        assert!(!info.reads.is_empty(), "beqz should record a read of r12");
        assert_eq!(info.get_state(), RegisterState::Used);
    }
    
    #[test]
    fn debug_tree_sitter_parsing() {
        use tree_sitter::{Query, QueryCursor};
        
        let test_get = "yield\nget r12 db 12\nbeqz r12 InitWaitLoop\n";
        let trinity = "InitWaitLoop:\n    yield\n    get r12 db 12\n    beqz r12 InitWaitLoop\n";
        
        for (name, src) in [("test_get", test_get), ("trinity", trinity)] {
            eprintln!("\n=== Parsing {} ===", name);
            let mut parser = Parser::new();
            parser.set_language(tree_sitter_ic10::language()).unwrap();
            let tree = parser.parse(src, None).unwrap();
            
            eprintln!("S-expression:\n{}", tree.root_node().to_sexp());
            
            let instruction_query = Query::new(
                tree_sitter_ic10::language(),
                "(instruction (operation) @op) @instruction",
            ).unwrap();
            
            let mut cursor = QueryCursor::new();
            let op_idx = instruction_query.capture_index_for_name("op").unwrap();
            let instruction_idx = instruction_query.capture_index_for_name("instruction").unwrap();
            
            eprintln!("Query captures:");
            for (capture, _) in cursor.captures(&instruction_query, tree.root_node(), src.as_bytes()) {
                for cap in capture.captures {
                    if cap.index == op_idx {
                        let line = cap.node.start_position().row + 1;
                        let text = cap.node.utf8_text(src.as_bytes()).unwrap();
                        eprintln!("  Line {}: Operation '{}'", line, text);
                    } else if cap.index == instruction_idx {
                        let line = cap.node.start_position().row + 1;
                        let text = cap.node.utf8_text(src.as_bytes()).unwrap();
                        eprintln!("  Line {}: Instruction '{}'", line, text);
                    }
                }
            }
        }
    }
    
    #[test]
    fn pop_assigns_register() {
        let aliases = HashMap::new();
        let src = "pop r11\nbeqz r11 Loop\n";
        let ra = analyze(src, &aliases);
        let info = ra.get_register_info("r11").unwrap();
        
        // pop should create an assignment
        assert!(!info.assignments.is_empty(), "pop should assign to r11");
        assert!(!info.reads.is_empty(), "beqz should read r11");
        assert_eq!(info.get_state(), RegisterState::Used, "r11 should be Used, not ReadBeforeAssign");
    }
    
    #[test]
    fn peek_assigns_register() {
        let aliases = HashMap::new();
        let src = "peek r10\nadd r9 r10 5\n";
        let ra = analyze(src, &aliases);
        let info = ra.get_register_info("r10").unwrap();
        
        assert!(!info.assignments.is_empty(), "peek should assign to r10");
        assert!(!info.reads.is_empty(), "add should read r10");
        assert_eq!(info.get_state(), RegisterState::Used);
    }

    // ---- Control-flow analysis (read-before-assign) ----------------------

    /// Count read-before-assign ERROR diagnostics mentioning `register`.
    fn rba_count(ra: &RegisterAnalyzer, register: &str) -> usize {
        ra.generate_diagnostics()
            .iter()
            .filter(|d| {
                matches!(
                    &d.code,
                    Some(tower_lsp::lsp_types::NumberOrString::String(c))
                        if c == "register_read_before_assign"
                )
            })
            .filter(|d| {
                d.data
                    .as_ref()
                    .and_then(|v| v.as_str())
                    .map(|s| s == register)
                    .unwrap_or(false)
            })
            .count()
    }

    #[test]
    fn cfg_flags_never_assigned_read() {
        // r2 is read but never written on any path -> genuine read-before-assign.
        let aliases = HashMap::new();
        let ra = analyze("add r1 r2 5\n", &aliases);
        assert_eq!(rba_count(&ra, "r2"), 1, "r2 is never assigned, must be flagged");
        assert_eq!(rba_count(&ra, "r1"), 0, "r1 is the write target, not a read");
    }

    #[test]
    fn cfg_suppresses_loop_back_edge() {
        // Classic loop: r0 assigned at top, incremented inside loop. The textual
        // model could misorder this; the CFG must see r0 as assigned.
        let aliases = HashMap::new();
        let src = "move r0 0\nloop:\nadd r0 r0 1\nblt r0 10 loop\n";
        let ra = analyze(src, &aliases);
        assert_eq!(rba_count(&ra, "r0"), 0, "r0 is assigned before the loop");
    }

    #[test]
    fn cfg_suppresses_init_skip_forward_jump() {
        // Read appears textually BEFORE the assignment, but control flow runs the
        // init block first via the jump. This is the textual model's false positive.
        let aliases = HashMap::new();
        let src = "\
j init
process:
add r1 r0 1
yield
init:
move r0 5
j process
";
        let ra = analyze(src, &aliases);
        assert_eq!(
            rba_count(&ra, "r0"),
            0,
            "r0 is assigned in init (reached before process), must NOT be flagged"
        );
    }

    #[test]
    fn cfg_flags_read_in_unconditionally_reached_block() {
        // r5 is only ever assigned inside a block that is skipped over by an
        // unconditional jump and never falls through -> never assigned on the
        // path that reaches the read.
        let aliases = HashMap::new();
        let src = "\
j main
dead:
move r5 9
hcf
main:
add r6 r5 1
";
        let ra = analyze(src, &aliases);
        assert_eq!(
            rba_count(&ra, "r5"),
            1,
            "r5's only assignment is unreachable before the read"
        );
    }

    #[test]
    fn cfg_respects_disabled_flag_falls_back_to_textual() {
        // With control flow disabled, the legacy textual heuristic runs instead.
        let aliases = HashMap::new();
        let src = "add r1 r2 5\n";
        let mut parser = Parser::new();
        parser.set_language(tree_sitter_ic10::language()).unwrap();
        let tree = parser.parse(src, None).unwrap();
        let mut ra = RegisterAnalyzer::new();
        ra.set_control_flow_enabled(false);
        ra.analyze_register_usage(&tree, src, &aliases);
        // r2 never assigned -> both models agree it's a violation.
        assert_eq!(rba_count(&ra, "r2"), 1);
    }

    #[test]
    fn cfg_ignores_implicitly_initialized_registers() {
        // sp, ra and rr* are implicitly initialized and must never be flagged.
        let aliases = HashMap::new();
        let ra = analyze("add r0 sp 1\nadd r1 ra 1\nadd r2 rr3 1\n", &aliases);
        assert_eq!(rba_count(&ra, "sp"), 0);
        assert_eq!(rba_count(&ra, "ra"), 0);
        assert_eq!(rba_count(&ra, "rr3"), 0);
    }

    /// End-to-end check of the shipped `.ic10` test scripts through the real
    /// diagnostics entry point. Keeps the manual test files honest.
    #[test]
    fn cfg_test_scripts_match_expectations() {
        let dir = concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../dev/testing/control-flow-tests/"
        );
        // (file, expected number of read-before-assign errors)
        let cases = [
            ("01_true_positives.ic10", 3),
            ("02_loop_back_edge.ic10", 0),
            ("03_init_skip_forward_jump.ic10", 0),
            ("04_unreachable_assignment.ic10", 2),
            ("05_realistic_clean_program.ic10", 0),
        ];
        for (file, expected) in cases {
            let path = format!("{}{}", dir, file);
            let content = std::fs::read_to_string(&path)
                .unwrap_or_else(|e| panic!("read {}: {}", path, e));
            let count = crate::lsp_diagnostics::compute_diagnostics_for_text(&content)
                .iter()
                .filter(|d| {
                    matches!(
                        &d.code,
                        Some(tower_lsp::lsp_types::NumberOrString::String(c))
                            if c == "register_read_before_assign"
                    )
                })
                .count();
            assert_eq!(
                count, expected,
                "{} expected {} read-before-assign error(s), got {}",
                file, expected, count
            );
        }
    }

    #[test]
    fn cfg_branch_both_paths_assign() {
        // r4 assigned on the fallthrough; read after the merge. Some path assigns
        // it, so the conservative analysis stays silent.
        let aliases = HashMap::new();
        let src = "\
bgtz r0 skip
move r4 1
skip:
add r7 r4 2
";
        let ra = analyze(src, &aliases);
        // r4 is assigned only on one path; may-analysis -> not flagged (no false alarm).
        assert_eq!(rba_count(&ra, "r4"), 0);
    }
}
