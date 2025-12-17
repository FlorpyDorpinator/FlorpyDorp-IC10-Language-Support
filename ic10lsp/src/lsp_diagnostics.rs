//! LSP Diagnostics Module
//!
//! This module handles all diagnostic generation for the IC10 language server.
//! It provides:
//! - Type checking for instruction parameters
//! - Syntax error detection
//! - Line/column/byte limit validation
//! - Register usage analysis
//! - Linting for branch instructions

use std::collections::HashSet;
use std::time::Instant;

use phf::phf_set;
use sha2::{Sha256, Digest};
use tower_lsp::lsp_types::{
    Diagnostic, DiagnosticSeverity, NumberOrString,
    Position as LspPosition, Range as LspRange, Url,
};
use tree_sitter::{Parser, Query, QueryCursor};

use ic10lsp::instructions::{self, DataType};

use crate::additional_features;
use crate::diagnostic_helpers::diagnostic_identity;
use crate::document::{AliasValue, DefinitionData, TypeData};
use crate::hash_utils::{extract_hash_argument, get_device_hash, is_hash_function_call, is_numeric_string};
use crate::type_classification::{classify_ci_keyword, classify_exact_keyword};
use crate::types::{Position, Range};
use crate::Backend;

// Re-use constants from main module
use crate::{LINT_ABSOLUTE_JUMP, LINT_RELATIVE_BRANCH_TO_LABEL, NAME_ONLY};

/// Check types for all instructions in the document
pub async fn check_types(backend: &Backend, uri: &Url, diagnostics: &mut Vec<Diagnostic>) {
    let files = backend.files.read().await;
    let Some(file_data) = files.get(uri) else {
        return;
    };
    let document = &file_data.document_data;
    let mut type_data = file_data.type_data.clone();

    let Some(tree) = document.tree.as_ref() else {
        return;
    };

    // Read config before the loop to avoid await across non-Send types
    let suppress_hash_diagnostics = backend.config.read().await.suppress_hash_diagnostics;

    let mut cursor = QueryCursor::new();
    let query = Query::new(tree_sitter_ic10::language(), "(instruction)@a").unwrap();

    let captures = cursor.captures(&query, tree.root_node(), document.content.as_bytes());

    // Build register analyzer (for device-id awareness & prior value kinds)
    let mut register_analyzer = additional_features::RegisterAnalyzer::new();
    register_analyzer.analyze_register_usage(tree, &document.content, &type_data.aliases);

    for (capture, _) in captures {
        let capture = capture.captures[0].node;

        if let Some(operation_node) = capture.child_by_field_name("operation") {
            let operation = operation_node
                .utf8_text(document.content.as_bytes())
                .unwrap();
            let Some(signature) = instructions::INSTRUCTIONS.get(operation) else {
                diagnostics.push(Diagnostic::new(
                    Range::from(operation_node.range()).into(),
                    Some(DiagnosticSeverity::ERROR),
                    None,
                    None,
                    format!("Invalid instruction"),
                    None,
                    None,
                ));
                continue;
            };

            let mut argument_count = 0;
            let mut tree_cursor = capture.walk();
            let operands = capture.children_by_field_name("operand", &mut tree_cursor);
            let mut parameters = signature.0.iter();

            let mut first_superfluous_arg = None;
            let mut pending_define_name: Option<(String, Range)> = None;

            for operand in operands {
                argument_count = argument_count + 1;
                let Some(parameter) = parameters.next() else {
                    if first_superfluous_arg.is_none() {
                        first_superfluous_arg = Some(operand);
                    }
                    continue;
                };

                let operand_kind = operand.named_child(0).unwrap().kind();
                let expects_name = parameter.match_type(DataType::Name);
                // Keep track of an underlying register name if this operand ultimately refers to a register
                // (either directly or via alias). We'll use this to permit DeviceId registers where Device is expected.
                let mut underlying_register: Option<String> = None;
                let typ = match operand_kind {
                    "register" => {
                        // Direct register
                        if let Some(reg_text) = operand
                            .named_child(0)
                            .map(|n| n.utf8_text(document.content.as_bytes()).unwrap_or(""))
                        {
                            underlying_register = Some(reg_text.to_string());
                        }
                        instructions::Union(&[DataType::Register])
                    }
                    "device_spec" => instructions::Union(&[DataType::Device]),
                    "number" => instructions::Union(&[DataType::Number]),
                    "logictype" => {
                        let ident = operand
                            .named_child(0)
                            .unwrap()
                            .utf8_text(document.content.as_bytes())
                            .unwrap();
                        let flags = classify_exact_keyword(ident);
                        if flags.any() {
                            flags.to_union()
                        } else {
                            instructions::Union(&[])
                        }
                    }
                    "identifier" => {
                        let ident_node = operand.named_child(0).unwrap();
                        let ident = ident_node
                            .utf8_text(document.content.as_bytes())
                            .unwrap();

                        // First operand of a DEFINE is always the define name; remember it and never treat as unknown
                        if operation.eq_ignore_ascii_case("define") && argument_count == 1 {
                            pending_define_name = Some((
                                ident.to_string(),
                                Range::from(ident_node.range()).into(),
                            ));
                            instructions::Union(&NAME_ONLY)
                        } else

                        // Accept fully-qualified enum names like Family.Member as numeric identifiers (case-insensitive)
                        if expects_name {
                            instructions::Union(&NAME_ONLY)
                        } else if ident.contains('.') {
                            if let Some((canonical, _val, _desc, _dep)) =
                                instructions::enum_info_case_insensitive(ident)
                            {
                                if canonical != ident {
                                    diagnostics.push(Diagnostic::new(
                                        Range::from(operand.range()).into(),
                                        Some(DiagnosticSeverity::WARNING),
                                        None,
                                        None,
                                        format!(
                                            "Enum '{}' differs in case from canonical '{}'.",
                                            ident, canonical
                                        ),
                                        None,
                                        None,
                                    ));
                                }
                                instructions::Union(&[DataType::Number])
                            } else if type_data.defines.contains_key(ident)
                                || type_data.labels.contains_key(ident)
                            {
                                // Fully-qualified define/label; treat as numeric identifier
                                instructions::Union(&[DataType::Number])
                            } else if let Some((canonical, _)) = type_data
                                .defines
                                .keys()
                                .find(|k| k.eq_ignore_ascii_case(ident))
                                .map(|k| (k.clone(), ()))
                            {
                                if canonical != ident {
                                    diagnostics.push(Diagnostic::new(
                                        Range::from(operand.range()).into(),
                                        Some(DiagnosticSeverity::WARNING),
                                        None,
                                        None,
                                        format!(
                                            "Define '{}' differs in case from canonical '{}'.",
                                            ident, canonical
                                        ),
                                        None,
                                        None,
                                    ));
                                }
                                instructions::Union(&[DataType::Number])
                            } else if let Some(type_data_val) = type_data.aliases.get(ident) {
                                match type_data_val.value {
                                    AliasValue::Device(_) => {
                                        instructions::Union(&[DataType::Device])
                                    }
                                    AliasValue::Register(ref reg_name) => {
                                        underlying_register = Some(reg_name.clone());
                                        instructions::Union(&[DataType::Register])
                                    }
                                }
                            } else {
                                // fall through to case-insensitive logic checks below
                                instructions::Union(&[])
                            }
                        }
                        // Prefer user-defined identifiers (defines/labels/aliases) over reserved keywords
                        else if type_data.defines.contains_key(ident)
                            || type_data.labels.contains_key(ident)
                        {
                            // User-defined identifier (define/label) always resolves; value may be HASH(...) or number
                            instructions::Union(&[DataType::Number])
                        } else if let Some((canonical, _)) = type_data
                            .defines
                            .keys()
                            .find(|k| k.eq_ignore_ascii_case(ident))
                            .map(|k| (k.clone(), ()))
                        {
                            if canonical != ident {
                                diagnostics.push(Diagnostic::new(
                                    Range::from(operand.range()).into(),
                                    Some(DiagnosticSeverity::WARNING),
                                    None,
                                    None,
                                    format!(
                                        "Define '{}' differs in case from canonical '{}'.",
                                        ident, canonical
                                    ),
                                    None,
                                    None,
                                ));
                            }
                            instructions::Union(&[DataType::Number])
                        } else if let Some(type_data_val) = type_data.aliases.get(ident) {
                            match type_data_val.value {
                                AliasValue::Device(_) => {
                                    instructions::Union(&[DataType::Device])
                                }
                                AliasValue::Register(ref reg_name) => {
                                    // Alias points at a register; remember for DeviceId substitution
                                    underlying_register = Some(reg_name.clone());
                                    instructions::Union(&[DataType::Register])
                                }
                            }
                        } else {
                            let exact_flags = classify_exact_keyword(ident);
                            if exact_flags.any() {
                                exact_flags.to_union()
                            } else if let Some(_) = instructions::resolve_unnamed_enum_member(ident) {
                                // Identifier is a member of the _unnamed enum (like NotEquals, Equals, etc.)
                                // Treat it as a numeric constant
                                instructions::Union(&[DataType::Number])
                            } else {
                                let ci_flags = classify_ci_keyword(ident);
                                if ci_flags.any() {
                                    diagnostics.push(Diagnostic::new(
                                    Range::from(operand.range()).into(),
                                    Some(DiagnosticSeverity::WARNING),
                                    None,
                                    None,
                                    format!("Identifier '{}' matches a known logic/parameter type by name but differs by case. Consider using proper case or renaming your identifier.", ident),
                                    None,
                                    None,
                                ));
                                    ci_flags.to_union()
                                } else {
                                    diagnostics.push(Diagnostic::new(
                                        Range::from(operand.range()).into(),
                                        Some(DiagnosticSeverity::ERROR),
                                        None,
                                        None,
                                        format!("Unknown identifier"),
                                        None,
                                        None,
                                    ));
                                    continue;
                                }
                            }
                        }
                    }
                    "hash_function" => {
                        // Treat HASH("...") as producing a device hash number
                        let call_text =
                            operand.utf8_text(document.content.as_bytes()).unwrap();
                        if let Some(name) = extract_hash_argument(call_text) {
                            if let Some(_) = get_device_hash(name.as_str()) {
                                // Known device name
                            } else {
                                // Unknown device string; still treat as number but nudge (unless suppressed)
                                if !suppress_hash_diagnostics {
                                    diagnostics.push(Diagnostic::new(
                                        Range::from(operand.range()).into(),
                                        Some(DiagnosticSeverity::INFORMATION),
                                        None,
                                        None,
                                        format!("Unrecognized device name '{}' in HASH(...). Will be treated as number.", name),
                                        None,
                                        None,
                                    ));
                                }
                            }
                        }
                        instructions::Union(&[DataType::Number])
                    }
                    "str_function" => {
                        // STR("...") produces a string hash number
                        // No type mismatch diagnostics needed - it's valid usage
                        instructions::Union(&[DataType::Number])
                    }
                    "function_call" => {
                        // Unknown function: conservatively treat as number to avoid spurious errors
                        instructions::Union(&[DataType::Number])
                    }
                    _ => {
                        continue;
                    }
                };
                // Special case: register (direct or via alias) holding DeviceId or Unknown can satisfy a Device parameter
                // Special case: register holding LogicType or Unknown can satisfy a LogicType parameter
                let mut effective_typ = typ;
                if parameter.match_type(DataType::Device) {
                    if let Some(reg_name) = underlying_register.as_ref() {
                        let kind = register_analyzer.get_register_kind(reg_name);
                        if kind == additional_features::ValueKind::DeviceId
                            || kind == additional_features::ValueKind::Unknown
                        {
                            effective_typ = instructions::Union(&[DataType::Device]);
                        }
                    }
                } else if parameter.match_type(DataType::LogicType) || parameter.match_type(DataType::SlotLogicType) {
                    if let Some(reg_name) = underlying_register.as_ref() {
                        let kind = register_analyzer.get_register_kind(reg_name);
                        // LogicTypes are numeric constants, so Number/LogicType/Unknown can all satisfy LogicType parameters
                        if kind == additional_features::ValueKind::LogicType
                            || kind == additional_features::ValueKind::Number
                            || kind == additional_features::ValueKind::Unknown
                        {
                            // Register holds a numeric/LogicType value, so it can be used where LogicType is expected
                            if parameter.match_type(DataType::LogicType) {
                                effective_typ = instructions::Union(&[DataType::LogicType]);
                            } else {
                                effective_typ = instructions::Union(&[DataType::SlotLogicType]);
                            }
                        }
                    }
                }
                // Allow define name second operand to be register when signature expects Number|Register already (adjusted in INSTRUCTIONS)
                if !parameter.match_union(&effective_typ) {
                    diagnostics.push(Diagnostic::new(
                        Range::from(operand.range()).into(),
                        Some(DiagnosticSeverity::ERROR),
                        None,
                        None,
                        format!(
                            "Type mismatch. Found {}, expected {}",
                            effective_typ, parameter
                        ),
                        None,
                        None,
                    ));
                }

                // After processing the second operand of DEFINE, store it in the working define map
                if operation.eq_ignore_ascii_case("define") && argument_count == 2 {
                    if let Some((define_name, define_range)) = pending_define_name.clone() {
                        let value_text = operand
                            .utf8_text(document.content.as_bytes())
                            .unwrap()
                            .trim()
                            .to_string();
                        type_data.defines.insert(
                            define_name,
                            DefinitionData::new(define_range, value_text.into()),
                        );
                    }
                }
            }
            if argument_count > signature.0.len() {
                let plural_str = if argument_count - signature.0.len() > 1 {
                    "s"
                } else {
                    ""
                };

                diagnostics.push(Diagnostic::new(
                    tower_lsp::lsp_types::Range::new(
                        Position::from(first_superfluous_arg.unwrap().start_position()).into(),
                        Position::from(capture.end_position()).into(),
                    ),
                    Some(DiagnosticSeverity::ERROR),
                    None,
                    None,
                    format!(
                        "Superfluous argument{}. '{}' only requires {} arguments.",
                        plural_str,
                        operation,
                        signature.0.len()
                    ),
                    None,
                    None,
                ));
                continue;
            }
            if argument_count != signature.0.len() {
                diagnostics.push(Diagnostic::new(
                    Range::from(capture.range()).into(),
                    Some(DiagnosticSeverity::ERROR),
                    None,
                    None,
                    "Invalid number of arguments".to_string(),
                    None,
                    None,
                ));
            }
        }
    }
}

/// Run full diagnostics on a document and publish results
pub async fn run_diagnostics(backend: &Backend, uri: &Url) {
    let _timer = crate::performance::TimingGuard::new(&backend.perf_tracker, "lsp.server.diagnostics");
    backend.perf_tracker.increment("lsp.server.diagnostics.calls", 1);
    
    // If diagnostics disabled, clear and bail
    if !*backend.diagnostics_enabled.read().await {
        backend.client
            .publish_diagnostics(uri.clone(), vec![], None)
            .await;
        return;
    }
    
    // Check cache first (content hash to detect changes)
    let content_hash = {
        let files = backend.files.read().await;
        if let Some(file_data) = files.get(uri) {
            let mut hasher = Sha256::new();
            hasher.update(file_data.document_data.content.as_bytes());
            format!("{:x}", hasher.finalize())
        } else {
            return; // File not found
        }
    };
    
    // Try to get cached diagnostics (DashMap is lock-free)
    if let Some(cached_diagnostics) = backend.diagnostic_cache.get(&content_hash) {
        backend.perf_tracker.increment("lsp.server.diagnostics.cache_hits", 1);
        backend.client
            .publish_diagnostics(uri.clone(), cached_diagnostics.clone(), None)
            .await;
        
        // Update the last diagnostic run time
        {
            let mut files = backend.files.write().await;
            if let Some(file_data) = files.get_mut(uri) {
                file_data.last_diagnostic_run = Some(Instant::now());
            }
        }
        return; // Cache hit!
    }
    
    backend.perf_tracker.increment("lsp.server.diagnostics.cache_misses", 1);
    
    // Update the last diagnostic run time
    {
        let mut files = backend.files.write().await;
        if let Some(file_data) = files.get_mut(uri) {
            file_data.last_diagnostic_run = Some(Instant::now());
        }
    }
    
    let mut diagnostics = Vec::new();

    // Collect definitions
    backend.update_definitions(uri, &mut diagnostics).await;

    let config = backend.config.read().await;
    let files = backend.files.read().await;
    let Some(file_data) = files.get(uri) else {
        return;
    };

    let document = &file_data.document_data;
    let Some(tree) = document.tree.as_ref() else {
        return;
    };

    // Syntax errors
    {
        let mut cursor = QueryCursor::new();
        let query = Query::new(tree_sitter_ic10::language(), "(ERROR)@error").unwrap();
        let captures = cursor.captures(&query, tree.root_node(), document.content.as_bytes());
        for (capture, _) in captures {
            diagnostics.push(Diagnostic::new(
                Range::from(capture.captures[0].node.range()).into(),
                Some(DiagnosticSeverity::ERROR),
                None,
                None,
                "Syntax error".to_string(),
                None,
                None,
            ));
        }
    }

    // Find invalid instructions
    {
        let mut cursor = QueryCursor::new();
        let query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction (invalid_instruction)@error)",
        )
        .unwrap();
        let captures = cursor.captures(&query, tree.root_node(), document.content.as_bytes());
        for (capture, _) in captures {
            let node = capture.captures[0].node;
            let instruction_text = node.utf8_text(document.content.as_bytes()).unwrap();
            if !instructions::INSTRUCTIONS.contains_key(instruction_text) {
                diagnostics.push(Diagnostic::new(
                    Range::from(node.range()).into(),
                    Some(DiagnosticSeverity::ERROR),
                    None,
                    None,
                    "Invalid instruction".to_string(),
                    None,
                    None,
                ));
            }
        }
    }

    // Type check - need to drop files lock first
    drop(files);
    drop(config);
    check_types(backend, uri, &mut diagnostics).await;

    // Re-acquire locks for remaining checks
    let config = backend.config.read().await;
    let files = backend.files.read().await;
    let Some(file_data) = files.get(uri) else {
        return;
    };
    let document = &file_data.document_data;
    let Some(tree) = document.tree.as_ref() else {
        return;
    };

    // Overlength checks
    {
        let mut cursor = QueryCursor::new();

        let query = Query::new(tree_sitter_ic10::language(), "(instruction)@x").unwrap();
        for (capture, _) in
            cursor.captures(&query, tree.root_node(), document.content.as_bytes())
        {
            let node = capture.captures[0].node;
            if node.end_position().column > config.max_columns {
                diagnostics.push(Diagnostic {
                    range: LspRange::new(
                        LspPosition::new(
                            node.end_position().row as u32,
                            config.max_columns as u32,
                        ),
                        Position::from(node.end_position()).into(),
                    ),
                    severity: Some(DiagnosticSeverity::ERROR),
                    message: format!("Instruction past column {}", config.max_columns),
                    ..Default::default()
                });
            }
        }

        if config.warn_overcolumn_comment {
            let query = Query::new(tree_sitter_ic10::language(), "(comment)@x").unwrap();
            for (capture, _) in
                cursor.captures(&query, tree.root_node(), document.content.as_bytes())
            {
                let node = capture.captures[0].node;
                if node.end_position().column > config.max_columns {
                    diagnostics.push(Diagnostic {
                        range: LspRange::new(
                            LspPosition::new(
                                node.end_position().row as u32,
                                config.max_columns as u32,
                            ),
                            Position::from(node.end_position()).into(),
                        ),
                        severity: Some(DiagnosticSeverity::WARNING),
                        message: format!("Comment past column {}", config.max_columns),
                        ..Default::default()
                    });
                }
            }
        }

        // Check for #IgnoreLimits directive
        if !crate::diagnostic_helpers::should_ignore_limits(&document.content) {
            cursor.set_point_range(
                tree_sitter::Point::new(config.max_lines, 0)
                    ..tree_sitter::Point::new(usize::MAX, usize::MAX),
            );
            let query = Query::new(tree_sitter_ic10::language(), "(instruction)@x").unwrap();

            for (capture, _) in
                cursor.captures(&query, tree.root_node(), document.content.as_bytes())
            {
                let node = capture.captures[0].node;
                diagnostics.push(Diagnostic {
                    range: Range::from(node.range()).into(),
                    severity: Some(DiagnosticSeverity::ERROR),
                    message: format!("Instruction past line {}", config.max_lines),
                    ..Default::default()
                });
            }

            if config.warn_overline_comment {
                let query = Query::new(tree_sitter_ic10::language(), "(comment)@x").unwrap();
                for (capture, _) in
                    cursor.captures(&query, tree.root_node(), document.content.as_bytes())
                {
                    let node = capture.captures[0].node;
                    diagnostics.push(Diagnostic {
                        range: Range::from(node.range()).into(),
                        severity: Some(DiagnosticSeverity::WARNING),
                        message: format!("Comment past line {}", config.max_lines),
                        ..Default::default()
                    });
                }
            }
        }
    }

    // Byte size check
    {
        // Check for #IgnoreLimits directive
        if !crate::diagnostic_helpers::should_ignore_limits(&document.content) {
            let mut byte_count = 0;
            let mut start_pos: Option<LspPosition> = None;
            
            // Stationeers byte counting (matches UpdateFileSize() method):
            // After paste, each line is trimmed with TrimEnd()
            // Then UpdateFileSize() counts: line.Length + 2 bytes (CRLF) for all except last line
            // This matches the file loading behavior from InputSourceCode.cs lines 562-568
            
            // Split content by newlines, filtering out empty trailing lines
            // This matches how Stationeers processes pasted content
            let mut lines: Vec<&str> = document.content.lines().collect();
            
            // Remove trailing empty lines (Stationeers doesn't count these)
            while lines.last().map_or(false, |l| l.trim().is_empty()) {
                lines.pop();
            }
            
            for (line_idx, line) in lines.iter().enumerate() {
                let trimmed = line.trim_end();
                byte_count += trimmed.len();
                
                // Add CRLF (2 bytes) for all lines except the last
                // Matches C# code: if (j < this.LinesOfCode.Count - 1)
                if line_idx < lines.len() - 1 {
                    byte_count += 2;
                }
            }

            // Find position where limit is exceeded (scan content for position)
            if byte_count > config.max_bytes {
                let mut current_line = 0;
                let mut current_col = 0;
                let mut running_count = 0;

                for char in document.content.chars() {
                    let char_len = char.len_utf8();

                    if running_count <= config.max_bytes && running_count + char_len > config.max_bytes {
                        if start_pos.is_none() {
                            start_pos = Some(LspPosition::new(current_line, current_col));
                        }
                    }
                    running_count += char_len;

                    if char == '\n' {
                        current_line += 1;
                        current_col = 0;
                    } else if char != '\r' {
                        current_col += 1;
                    }
                }

                let end_line = document.content.lines().count().saturating_sub(1) as u32;
                let end_col = document.content.lines().last().map_or(0, |l| l.len()) as u32;

                diagnostics.push(Diagnostic {
                    range: LspRange::new(
                        start_pos.unwrap_or_else(|| LspPosition::new(end_line, 0)),
                        LspPosition::new(end_line, end_col),
                    ),
                    severity: Some(DiagnosticSeverity::ERROR),
                    message: format!(
                        "Script size ({} bytes) exceeds the maximum limit of {} bytes.",
                        byte_count, config.max_bytes
                    ),
                    ..Default::default()
                });
            }
        }
    }

    // Absolute jump to number lint
    {
        const BRANCH_INSTRUCTIONS: phf::Set<&'static str> = phf_set!(
            "bdns", "bdnsal", "bdse", "bdseal", "bap", "bapz", "bapzal", "beq", "beqal",
            "beqz", "beqzal", "bge", "bgeal", "bgez", "bgezal", "bgt", "bgtal", "bgtz",
            "bgtzal", "ble", "bleal", "blez", "blezal", "blt", "bltal", "bltz", "bltzal",
            "bna", "bnaz", "bnazal", "bne", "bneal", "bnez", "bnezal", "j", "jal"
        );
        let mut cursor = QueryCursor::new();
        let query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction operand: (operand (number))) @x",
        )
        .unwrap();
        let mut tree_cursor = tree.walk();
        let captures = cursor.captures(&query, tree.root_node(), document.content.as_bytes());
        for (capture, _) in captures {
            let capture = capture.captures[0].node;
            let Some(operation_node) = capture.child_by_field_name("operation") else {
                continue;
            };
            let operation = operation_node
                .utf8_text(document.content.as_bytes())
                .unwrap();
            if !BRANCH_INSTRUCTIONS.contains(operation) {
                continue;
            }

            tree_cursor.reset(capture);
            let Some(last_operand) = capture
                .children_by_field_name("operand", &mut tree_cursor)
                .into_iter()
                .last()
            else {
                continue;
            };
            if let Some(last_operand) = last_operand.child(0) {
                if last_operand.kind() == "number" {
                    diagnostics.push(Diagnostic::new(
                        Range::from(capture.range()).into(),
                        Some(DiagnosticSeverity::WARNING),
                        Some(NumberOrString::String(LINT_ABSOLUTE_JUMP.to_string())),
                        None,
                        "Absolute jump to line number".to_string(),
                        None,
                        None,
                    ));
                }
            }
        }
    }

    // Relative branch to label lint (should use absolute branch)
    {
        const RELATIVE_BRANCH_INSTRUCTIONS: phf::Set<&'static str> = phf_set!(
            "brdns", "brdnsal", "brdse", "brdseal", "brap", "brapz", "brapzal", "breq", "breqal",
            "breqz", "breqzal", "brge", "brgeal", "brgez", "brgezal", "brgt", "brgtal", "brgtz",
            "brgtzal", "brle", "brleal", "brlez", "brlezal", "brlt", "brltal", "brltz", "brltzal",
            "brna", "brnaz", "brnazal", "brne", "brneal", "brnez", "brnezal"
        );
        let mut cursor = QueryCursor::new();
        let query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction operand: (operand (identifier))) @x",
        )
        .unwrap();
        let mut tree_cursor = tree.walk();
        let captures = cursor.captures(&query, tree.root_node(), document.content.as_bytes());
        for (capture, _) in captures {
            let capture = capture.captures[0].node;
            let Some(operation_node) = capture.child_by_field_name("operation") else {
                continue;
            };
            let operation = operation_node
                .utf8_text(document.content.as_bytes())
                .unwrap();
            if !RELATIVE_BRANCH_INSTRUCTIONS.contains(operation) {
                continue;
            }

            tree_cursor.reset(capture);
            let Some(last_operand) = capture
                .children_by_field_name("operand", &mut tree_cursor)
                .into_iter()
                .last()
            else {
                continue;
            };
            if let Some(last_operand_child) = last_operand.child(0) {
                if last_operand_child.kind() == "identifier" {
                    let identifier_text = last_operand_child
                        .utf8_text(document.content.as_bytes())
                        .unwrap();
                    // Check if this identifier is a label (exists in labels map)
                    if file_data.type_data.labels.contains_key(identifier_text) {
                        diagnostics.push(Diagnostic::new(
                            Range::from(capture.range()).into(),
                            Some(DiagnosticSeverity::WARNING),
                            Some(NumberOrString::String(LINT_RELATIVE_BRANCH_TO_LABEL.to_string())),
                            None,
                            "Relative branch to label - do you REALLY want to use a relative branch here? Relative branches use the numeric value at the label, not the label's line number. Use absolute branch instead.".to_string(),
                            None,
                            None,
                        ));
                    }
                }
            }
        }
    }

    // Check for numbers inside HASH() functions
    {
        let mut cursor = QueryCursor::new();
        let query = Query::new(
            tree_sitter_ic10::language(),
            "(hash_function argument: (hash_string)) @hash",
        )
        .unwrap();
        let captures = cursor.captures(&query, tree.root_node(), document.content.as_bytes());
        
        for (capture, _) in captures {
            let hash_func_node = capture.captures[0].node;
            
            // Get the argument node (hash_string)
            if let Some(arg_node) = hash_func_node.child_by_field_name("argument") {
                let arg_text = arg_node.utf8_text(document.content.as_bytes()).unwrap();
                
                // Extract the string content (strip quotes)
                if let Some(content) = extract_hash_argument(arg_text) {
                    // Check if the content is numeric
                    if is_numeric_string(&content) {
                        diagnostics.push(Diagnostic::new(
                            Range::from(hash_func_node.range()).into(),
                            Some(DiagnosticSeverity::ERROR),
                            None,
                            None,
                            format!(
                                "Content inside HASH() argument cannot be a number. Use the hash value directly: {}",
                                content
                            ),
                            None,
                            None,
                        ));
                    }
                }
            }
        }
    }

    // Register usage analysis
    {
        // Skip register diagnostics if globally suppressed
        if !config.suppress_register_warnings {
            let mut register_analyzer = additional_features::RegisterAnalyzer::new();
            register_analyzer.analyze_register_usage(
                tree,
                &document.content,
                &file_data.type_data.aliases,
            );
            let register_diagnostics = register_analyzer.generate_diagnostics();
            let mut seen = HashSet::new();
            for existing in diagnostics.iter() {
                seen.insert(diagnostic_identity(existing));
            }
            for diag in register_diagnostics {
                if seen.insert(diagnostic_identity(&diag)) {
                    diagnostics.push(diag);
                }
            }
        }
    }

    // Global deduplication to avoid duplicate squiggles across all producers
    {
        let mut seen: HashSet<(u32, u32, u32, u32, String)> = HashSet::new();
        diagnostics.retain(|d| seen.insert(diagnostic_identity(d)));
    }

    // Store in cache (DashMap is lock-free)
    // Limit cache size to prevent memory bloat
    if backend.diagnostic_cache.len() > 100 {
        // Clear oldest entries (simple strategy: clear half when limit reached)
        let keys_to_remove: Vec<_> = backend.diagnostic_cache.iter()
            .take(50)
            .map(|entry| entry.key().clone())
            .collect();
        for key in keys_to_remove {
            backend.diagnostic_cache.remove(&key);
        }
    }
    backend.diagnostic_cache.insert(content_hash, diagnostics.clone());

    backend.client
        .publish_diagnostics(uri.to_owned(), diagnostics, None)
        .await;
}

/// Compute diagnostics for a single text buffer using the same logic as the LSP diagnostics.
/// This is a standalone function that doesn't require the Backend.
pub fn compute_diagnostics_for_text(content: &str) -> Vec<Diagnostic> {
    let mut diagnostics: Vec<Diagnostic> = Vec::new();

    let mut parser = Parser::new();
    parser
        .set_language(tree_sitter_ic10::language())
        .expect("Could not set language");
    let tree = parser.parse(content, None).unwrap();

    // Syntax errors
    {
        let mut cursor = QueryCursor::new();
        let query = Query::new(tree_sitter_ic10::language(), "(ERROR)@error").unwrap();
        let captures = cursor.captures(&query, tree.root_node(), content.as_bytes());
        for (capture, _) in captures {
            diagnostics.push(Diagnostic::new(
                Range::from(capture.captures[0].node.range()).into(),
                Some(DiagnosticSeverity::ERROR),
                None,
                None,
                "Syntax error".to_string(),
                None,
                None,
            ));
        }
    }

    // Invalid instructions
    {
        let mut cursor = QueryCursor::new();
        let query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction (invalid_instruction)@error)",
        )
        .unwrap();
        let captures = cursor.captures(&query, tree.root_node(), content.as_bytes());
        for (capture, _) in captures {
            let node = capture.captures[0].node;
            let instruction_text = node.utf8_text(content.as_bytes()).unwrap();
            if !instructions::INSTRUCTIONS.contains_key(instruction_text) {
                diagnostics.push(Diagnostic::new(
                    Range::from(node.range()).into(),
                    Some(DiagnosticSeverity::ERROR),
                    None,
                    None,
                    "Invalid instruction".to_string(),
                    None,
                    None,
                ));
            }
        }
    }

    // Collect defines/aliases/labels
    let mut type_data = TypeData::default();
    {
        let mut cursor = QueryCursor::new();
        let query = Query::new(
            tree_sitter_ic10::language(),
            "(instruction (operation \"define\"))@define
                         (instruction (operation \"alias\"))@alias
                         (instruction (operation \"label\"))@alias
                         (label (identifier)@label)",
        )
        .unwrap();

        let define_idx = query.capture_index_for_name("define").unwrap();
        let alias_idx = query.capture_index_for_name("alias").unwrap();
        let label_idx = query.capture_index_for_name("label").unwrap();

        let captures = cursor.captures(&query, tree.root_node(), content.as_bytes());
        for (capture, _) in captures {
            let capture_idx = capture.captures[0].index;
            if capture_idx == define_idx || capture_idx == alias_idx {
                if let Some(name_operand_node) = capture.captures[0].node.child_by_field_name("operand") {
                    // Prefer inner identifier for the name and trim whitespace
                    let (name_text, name_range) = if let Some(id_node) = name_operand_node.child_by_field_name("identifier")
                        .or_else(|| name_operand_node.child(0).filter(|n| n.kind() == "identifier"))
                    {
                        (
                            id_node.utf8_text(content.as_bytes()).unwrap().trim().to_string(),
                            Range::from(id_node.range()),
                        )
                    } else {
                        (
                            name_operand_node
                                .utf8_text(content.as_bytes())
                                .unwrap()
                                .trim()
                                .to_string(),
                            Range::from(name_operand_node.range()),
                        )
                    };

                    if let Some(value_node) = capture.captures[0]
                        .node
                        .children_by_field_name("operand", &mut name_operand_node.walk())
                        .last()
                    {
                        let value = value_node.utf8_text(content.as_bytes()).unwrap();
                        if capture.captures[0].index == define_idx {
                            let child_kind = value_node.child(0).map(|x| x.kind()).unwrap_or("");
                            if child_kind == "number"
                                || child_kind == "function_call"
                                || child_kind == "hash_function"
                                || child_kind == "str_function"
                                || child_kind == "preproc_string"
                                || child_kind == "identifier"
                            {
                                type_data.defines.insert(
                                    name_text,
                                    DefinitionData::new(
                                        name_range.into(),
                                        value.to_string().into(),
                                    ),
                                );
                            }
                        } else if capture.captures[0].index == alias_idx {
                            if value_node
                                .child(0)
                                .map(|x| x.kind())
                                .map_or(false, |x| x == "register" || x == "device_spec")
                            {
                                type_data.aliases.insert(
                                    name_text,
                                    DefinitionData::new(name_range.into(), value.to_owned().into()),
                                );
                            }
                        }
                    }
                }
            } else if capture_idx == label_idx {
                let name_node = capture.captures[0].node;
                let name = name_node.utf8_text(content.as_bytes()).unwrap();
                type_data.labels.insert(
                    name.to_owned(),
                    DefinitionData {
                        range: Range::from(name_node.range()),
                        value: name_node.start_position().row as u8,
                    },
                );
            }
        }
    }

    // Type checking (simplified copy of check_types)
    {
        let mut cursor = QueryCursor::new();
        let query = Query::new(tree_sitter_ic10::language(), "(instruction)@a").unwrap();
        let captures = cursor.captures(&query, tree.root_node(), content.as_bytes());

        // Register analyzer
        let mut register_analyzer = additional_features::RegisterAnalyzer::new();
        register_analyzer.analyze_register_usage(&tree, content, &type_data.aliases);

        for (capture, _) in captures {
            let capture = capture.captures[0].node;
            if let Some(operation_node) = capture.child_by_field_name("operation") {
                let operation = operation_node.utf8_text(content.as_bytes()).unwrap();
                if let Some(signature) = instructions::INSTRUCTIONS.get(operation) {
                    let mut argument_count = 0;
                    let mut tree_cursor = capture.walk();
                    let operands = capture.children_by_field_name("operand", &mut tree_cursor);
                    let mut parameters = signature.0.iter();
                    let mut first_superfluous_arg = None;
                    let mut pending_define_name: Option<(String, Range)> = None;

                    for operand in operands {
                        argument_count += 1;
                        let Some(parameter) = parameters.next() else {
                            if first_superfluous_arg.is_none() {
                                first_superfluous_arg = Some(operand);
                            }
                            continue;
                        };
                        let operand_kind = operand.named_child(0).unwrap().kind();
                        let expects_name = parameter.match_type(DataType::Name);
                        let mut underlying_register: Option<String> = None;
                        let typ = match operand_kind {
                            "register" => {
                                if let Some(reg_text) = operand
                                    .named_child(0)
                                    .map(|n| n.utf8_text(content.as_bytes()).unwrap_or(""))
                                {
                                    underlying_register = Some(reg_text.to_string());
                                }
                                instructions::Union(&[DataType::Register])
                            }
                            "device_spec" => instructions::Union(&[DataType::Device]),
                            "number" => instructions::Union(&[DataType::Number]),
                            "logictype" => {
                                let ident = operand
                                    .named_child(0)
                                    .unwrap()
                                    .utf8_text(content.as_bytes())
                                    .unwrap();
                                let flags = classify_exact_keyword(ident);
                                if flags.any() {
                                    flags.to_union()
                                } else {
                                    instructions::Union(&[])
                                }
                            }
                            "identifier" => {
                                let ident_node = operand.named_child(0).unwrap();
                                let ident =
                                    ident_node.utf8_text(content.as_bytes()).unwrap();

                                if operation.eq_ignore_ascii_case("define")
                                    && argument_count == 1
                                {
                                    pending_define_name = Some((
                                        ident.to_string(),
                                        Range::from(ident_node.range()).into(),
                                    ));
                                    instructions::Union(&NAME_ONLY)
                                } else if expects_name {
                                    instructions::Union(&NAME_ONLY)
                                } else if ident.contains('.') {
                                    if let Some((canonical, _val, _desc, _dep)) =
                                        instructions::enum_info_case_insensitive(ident)
                                    {
                                        if canonical != ident {
                                            diagnostics.push(Diagnostic::new(
                                                Range::from(operand.range()).into(),
                                                Some(DiagnosticSeverity::WARNING),
                                                None,
                                                None,
                                                format!(
                                                    "Enum '{}' differs in case from canonical '{}'.",
                                                    ident, canonical
                                                ),
                                                None,
                                                None,
                                            ));
                                        }
                                        instructions::Union(&[DataType::Number])
                                    } else if type_data.defines.contains_key(ident)
                                        || type_data.labels.contains_key(ident)
                                    {
                                        instructions::Union(&[DataType::Number])
                                    } else if let Some((canonical, _)) = type_data
                                        .defines
                                        .keys()
                                        .find(|k| k.eq_ignore_ascii_case(ident))
                                        .map(|k| (k.clone(), ()))
                                    {
                                        if canonical != ident {
                                            diagnostics.push(Diagnostic::new(
                                                Range::from(operand.range()).into(),
                                                Some(DiagnosticSeverity::WARNING),
                                                None,
                                                None,
                                                format!(
                                                    "Define '{}' differs in case from canonical '{}'.",
                                                    ident, canonical
                                                ),
                                                None,
                                                None,
                                            ));
                                        }
                                        instructions::Union(&[DataType::Number])
                                    } else if let Some(type_data_val) =
                                        type_data.aliases.get(ident)
                                    {
                                        match type_data_val.value {
                                            AliasValue::Device(_) => {
                                                instructions::Union(&[DataType::Device])
                                            }
                                            AliasValue::Register(ref reg_name) => {
                                                underlying_register = Some(reg_name.clone());
                                                instructions::Union(&[DataType::Register])
                                            }
                                        }
                                    } else {
                                        instructions::Union(&[])
                                    }
                                } else if type_data.defines.contains_key(ident)
                                    || type_data.labels.contains_key(ident)
                                {
                                    instructions::Union(&[DataType::Number])
                                } else if let Some((canonical, _)) = type_data
                                    .defines
                                    .keys()
                                    .find(|k| k.eq_ignore_ascii_case(ident))
                                    .map(|k| (k.clone(), ()))
                                {
                                    if canonical != ident {
                                        diagnostics.push(Diagnostic::new(
                                            Range::from(operand.range()).into(),
                                            Some(DiagnosticSeverity::WARNING),
                                            None,
                                            None,
                                            format!(
                                                "Define '{}' differs in case from canonical '{}'.",
                                                ident, canonical
                                            ),
                                            None,
                                            None,
                                        ));
                                    }
                                    instructions::Union(&[DataType::Number])
                                } else if let Some(type_data_val) =
                                    type_data.aliases.get(ident)
                                {
                                    match type_data_val.value {
                                        AliasValue::Device(_) => {
                                            instructions::Union(&[DataType::Device])
                                        }
                                        AliasValue::Register(ref reg_name) => {
                                            underlying_register = Some(reg_name.clone());
                                            instructions::Union(&[DataType::Register])
                                        }
                                    }
                                } else {
                                    let exact_flags = classify_exact_keyword(ident);
                                    if exact_flags.any() {
                                        exact_flags.to_union()
                                    } else {
                                        let ci_flags = classify_ci_keyword(ident);
                                        if ci_flags.any() {
                                            diagnostics.push(Diagnostic::new(
                                                Range::from(operand.range()).into(),
                                                Some(DiagnosticSeverity::WARNING),
                                                None,
                                                None,
                                                format!("Identifier '{}' matches a known logic/parameter type by name but differs by case. Consider using proper case or renaming your identifier.", ident),
                                                None,
                                                None,
                                            ));
                                            ci_flags.to_union()
                                        } else {
                                            diagnostics.push(Diagnostic::new(
                                                Range::from(operand.range()).into(),
                                                Some(DiagnosticSeverity::ERROR),
                                                None,
                                                None,
                                                format!("Unknown identifier"),
                                                None,
                                                None,
                                            ));
                                            continue;
                                        }
                                    }
                                }
                            }
                            "function_call" | "hash_function" | "str_function" => {
                                let call_text = operand.utf8_text(content.as_bytes()).unwrap();
                                if is_hash_function_call(call_text) {
                                    instructions::Union(&[DataType::Number])
                                } else {
                                    instructions::Union(&[DataType::Number])
                                }
                            }
                            _ => continue,
                        };

                        let mut effective_typ = typ;
                        if parameter.match_type(DataType::Device) {
                            if let Some(reg_name) = underlying_register.as_ref() {
                                if register_analyzer.get_register_kind(reg_name)
                                    == additional_features::ValueKind::DeviceId
                                {
                                    effective_typ = instructions::Union(&[DataType::Device]);
                                }
                            }
                        }

                        if !parameter.match_union(&effective_typ) {
                            diagnostics.push(Diagnostic::new(
                                Range::from(operand.range()).into(),
                                Some(DiagnosticSeverity::ERROR),
                                None,
                                None,
                                format!(
                                    "Type mismatch. Found {}, expected {}",
                                    effective_typ, parameter
                                ),
                                None,
                                None,
                            ));
                        }

                        if operation.eq_ignore_ascii_case("define") && argument_count == 2 {
                            if let Some((define_name, define_range)) = pending_define_name.clone() {
                                let value_text = operand
                                    .utf8_text(content.as_bytes())
                                    .unwrap()
                                    .trim()
                                    .to_string();
                                type_data.defines.insert(
                                    define_name,
                                    DefinitionData::new(define_range, value_text.into()),
                                );
                            }
                        }
                    }

                    if argument_count > signature.0.len() {
                        if let Some(first_superfluous_arg) = first_superfluous_arg {
                            let plural_str = if argument_count - signature.0.len() > 1 {
                                "s"
                            } else {
                                ""
                            };
                            diagnostics.push(Diagnostic::new(
                                tower_lsp::lsp_types::Range::new(
                                    Position::from(first_superfluous_arg.start_position()).into(),
                                    Position::from(capture.end_position()).into(),
                                ),
                                Some(DiagnosticSeverity::ERROR),
                                None,
                                None,
                                format!(
                                    "Superfluous argument{}. '{}' only requires {} arguments.",
                                    plural_str,
                                    operation,
                                    signature.0.len()
                                ),
                                None,
                                None,
                            ));
                            continue;
                        }
                    }
                    if argument_count != signature.0.len() {
                        diagnostics.push(Diagnostic::new(
                            Range::from(capture.range()).into(),
                            Some(DiagnosticSeverity::ERROR),
                            None,
                            None,
                            "Invalid number of arguments".to_string(),
                            None,
                            None,
                        ));
                    }
                }
            }
        }
    }

    // Register analyzer diagnostics
    {
        let mut register_analyzer = additional_features::RegisterAnalyzer::new();
        register_analyzer.analyze_register_usage(&tree, content, &type_data.aliases);
        let mut seen = HashSet::new();
        for existing in diagnostics.iter() {
            seen.insert(diagnostic_identity(existing));
        }
        for diag in register_analyzer.generate_diagnostics() {
            if seen.insert(diagnostic_identity(&diag)) {
                diagnostics.push(diag);
            }
        }
    }

    diagnostics
}
