//! LSP Handlers Module
//!
//! This module contains handlers for various LSP protocol methods:
//! - Semantic tokens for syntax highlighting
//! - Document symbols for outline view
//! - Signature help for function parameters
//! - Code actions for quick fixes and refactors
//! - Go-to-definition for navigation

use std::collections::HashMap;

use phf::phf_set;
use tower_lsp::jsonrpc::Result;
use tower_lsp::lsp_types::{
    CodeAction, CodeActionKind, CodeActionOrCommand, CodeActionParams,
    Documentation, DocumentSymbolParams, DocumentSymbolResponse,
    GotoDefinitionParams, GotoDefinitionResponse, Location, NumberOrString,
    ParameterInformation, ParameterLabel, SemanticToken, SemanticTokens,
    SemanticTokensParams, SemanticTokensResult, SemanticTokenType,
    SignatureHelp, SignatureHelpParams, SignatureInformation,
    SymbolInformation, SymbolKind, TextEdit, WorkspaceEdit,
};
use tree_sitter::{Query, QueryCursor};

use ic10lsp::instructions;

use crate::tree_utils::{get_current_parameter, NodeEx};
use crate::types::{Position, Range};
use crate::{Backend, LINT_ABSOLUTE_JUMP, LINT_RELATIVE_BRANCH_TO_LABEL, SEMANTIC_SYMBOL_LEGEND};

/// Handle semantic tokens request for syntax highlighting
pub async fn handle_semantic_tokens_full(
    backend: &Backend,
    params: SemanticTokensParams,
) -> Result<Option<SemanticTokensResult>> {
    let mut ret = Vec::new();
    let files = backend.files.read().await;
    let uri = params.text_document.uri;
    let Some(file_data) = files.get(&uri) else {
        return Err(tower_lsp::jsonrpc::Error::invalid_request());
    };
    let document = &file_data.document_data;

    let Some(ref tree) = document.tree else {
        return Err(tower_lsp::jsonrpc::Error::internal_error());
    };

    let mut cursor = QueryCursor::new();
    let query = Query::new(
        tree_sitter_ic10::language(),
        "(comment) @comment
         (instruction (operation)@keyword)
         (instruction (invalid_instruction)@invalid_keyword)
         (logictype)@string
         (device)@preproc
         (register)@macro
         (number)@float
         (identifier)@variable",
    )
    .unwrap();

    let mut previous_line = 0u32;
    let mut previous_col = 0u32;

    let comment_idx = query.capture_index_for_name("comment").unwrap();
    let keyword_idx = query.capture_index_for_name("keyword").unwrap();
    let invalid_keyword_idx = query.capture_index_for_name("invalid_keyword").unwrap();
    let string_idx = query.capture_index_for_name("string").unwrap();
    let preproc_idx = query.capture_index_for_name("preproc").unwrap();
    let macro_idx = query.capture_index_for_name("macro").unwrap();
    let float_idx = query.capture_index_for_name("float").unwrap();
    let variable_idx = query.capture_index_for_name("variable").unwrap();

    for (capture, _) in cursor.captures(&query, tree.root_node(), document.content.as_bytes()) {
        let node = capture.captures[0].node;
        let idx = capture.captures[0].index;
        let start = node.range().start_point;

        let tokentype = {
            if idx == comment_idx {
                SemanticTokenType::COMMENT
            } else if idx == keyword_idx {
                SemanticTokenType::KEYWORD
            } else if idx == invalid_keyword_idx {
                let instruction_text = node.utf8_text(document.content.as_bytes()).unwrap();
                if instructions::INSTRUCTIONS.contains_key(instruction_text) {
                    SemanticTokenType::KEYWORD
                } else {
                    continue;
                }
            } else if idx == string_idx {
                SemanticTokenType::STRING
            } else if idx == preproc_idx {
                SemanticTokenType::FUNCTION
            } else if idx == macro_idx {
                SemanticTokenType::MACRO
            } else if idx == float_idx {
                SemanticTokenType::NUMBER
            } else if idx == variable_idx {
                // Classify identifiers: labels -> TYPE (purple), enums -> ENUM, otherwise VARIABLE
                let ident_text = node.utf8_text(document.content.as_bytes()).unwrap_or("");
                // Reconstruct fully-qualified enum token if this identifier is part of a dotted operand
                let mut qualified_operand: Option<String> = None;
                if let Some(parent) = node.parent() {
                    if parent.kind() == "operand" {
                        if let Ok(full) = parent.utf8_text(document.content.as_bytes()) {
                            // Trim trailing comment or whitespace artifacts
                            let full_trim = full.split('#').next().unwrap_or(full).trim();
                            if full_trim.contains('.') {
                                qualified_operand = Some(full_trim.to_string());
                            }
                        }
                    }
                }
                // Determine if this identifier is a branch/jump label reference even if forward-declared.
                let mut branch_label_reference = false;
                if !file_data.type_data.labels.contains_key(ident_text) {
                    // Only attempt contextual detection if not already a known label definition.
                    if let Some(operand_parent) = node.parent() {
                        if operand_parent.kind() == "operand" {
                            if let Some(instr_parent) = operand_parent.parent() {
                                if instr_parent.kind() == "instruction" {
                                    if let Some(op_node) =
                                        instr_parent.child_by_field_name("operation")
                                    {
                                        if let Ok(op_text) =
                                            op_node.utf8_text(document.content.as_bytes())
                                        {
                                            // Classify branch/jump mnemonics for positional label operands.
                                            // Two groups: (a,b,label) form and (a,label) form; plus single-operand j/jal.
                                            static THREE_OPERAND_BRANCHES: phf::Set<
                                                &'static str,
                                            > = phf_set!(
                                                "beq", "bne", "blt", "bgt", "ble", "bge",
                                                "breq", "brne", "brlt", "brgt", "brle", "brge",
                                                "beqal", "bneal", "bltal", "bgtal", "bleal",
                                                "bgeal"
                                            );
                                            static TWO_OPERAND_BRANCHES: phf::Set<
                                                &'static str,
                                            > = phf_set!(
                                                "beqz", "bnez", "bltz", "bgtz", "blez", "bgez",
                                                "breqz", "brnez", "brltz", "brgtz", "brlez",
                                                "brgez", "beqzal", "bnezal", "bltzal",
                                                "bgtzal", "blezal", "bgezal"
                                            );
                                            static SINGLE_OPERAND_JUMPS: phf::Set<
                                                &'static str,
                                            > = phf_set!("j", "jal");

                                            // Count operand index for this identifier within the instruction.
                                            let mut w = instr_parent.walk();
                                            let operands: Vec<_> = instr_parent
                                                .children_by_field_name("operand", &mut w)
                                                .collect();
                                            let operand_index = operands
                                                .iter()
                                                .position(|o| o.id() == operand_parent.id());
                                            if let Some(idx_op) = operand_index {
                                                let op_lower = op_text.to_ascii_lowercase();
                                                if THREE_OPERAND_BRANCHES
                                                    .contains(op_lower.as_str())
                                                {
                                                    // label is last (third) operand
                                                    if idx_op == 2 {
                                                        branch_label_reference = true;
                                                    }
                                                } else if TWO_OPERAND_BRANCHES
                                                    .contains(op_lower.as_str())
                                                {
                                                    // label is second operand
                                                    if idx_op == 1 {
                                                        branch_label_reference = true;
                                                    }
                                                } else if SINGLE_OPERAND_JUMPS
                                                    .contains(op_lower.as_str())
                                                {
                                                    // label is sole operand
                                                    if idx_op == 0 {
                                                        branch_label_reference = true;
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
                if file_data.type_data.labels.contains_key(ident_text) || branch_label_reference
                {
                    SemanticTokenType::TYPE
                } else if ident_text.contains('.')
                    && ic10lsp::instructions::enum_info_case_insensitive(ident_text).is_some()
                {
                    SemanticTokenType::ENUM
                } else if let Some(full) = qualified_operand.as_ref() {
                    // If the full operand is an enum qualified name (e.g., TraderInstruction.WriteTraderData)
                    // color both identifiers as ENUM tokens
                    if ic10lsp::instructions::enum_info_case_insensitive(full).is_some() {
                        SemanticTokenType::ENUM
                    } else {
                        SemanticTokenType::VARIABLE
                    }
                } else if ic10lsp::instructions::resolve_unnamed_enum_member(ident_text).is_some() {
                    // _unnamed enum members (NotEquals, Equals, Greater, Less) are numeric constants
                    SemanticTokenType::NUMBER
                } else {
                    SemanticTokenType::VARIABLE
                }
            } else {
                continue;
            }
        };

        // Convert byte positions to UTF-16 code units (required by VS Code)
        // Tree-sitter gives us byte offsets, but LSP uses UTF-16
        let line_text = document.content.lines().nth(start.row).unwrap_or("");
        
        // Calculate UTF-16 column for the start position
        let byte_start = start.column;
        let utf16_start = if byte_start <= line_text.len() {
            line_text[..byte_start].encode_utf16().count() as u32
        } else {
            line_text.encode_utf16().count() as u32
        };
        
        // Calculate UTF-16 column for the end position
        let byte_end = node.range().end_point.column;
        let utf16_end = if byte_end <= line_text.len() {
            line_text[..byte_end].encode_utf16().count() as u32
        } else {
            line_text.encode_utf16().count() as u32
        };
        
        // Calculate token length in UTF-16 code units
        let utf16_length = utf16_end.saturating_sub(utf16_start);
        
        // Skip tokens with zero length (defensive)
        if utf16_length == 0 {
            continue;
        }
        
        // Calculate delta for LSP semantic tokens format
        let utf16_delta_line = start.row as u32 - previous_line;
        let utf16_delta_start = if utf16_delta_line == 0 {
            utf16_start - previous_col
        } else {
            utf16_start
        };

        ret.push(SemanticToken {
            delta_line: utf16_delta_line,
            delta_start: utf16_delta_start,
            length: utf16_length,
            token_type: SEMANTIC_SYMBOL_LEGEND
                .iter()
                .position(|x| *x == tokentype)
                .unwrap() as u32,
            token_modifiers_bitset: 0,
        });

        previous_line = start.row as u32;
        previous_col = utf16_start;
    }
    Ok(Some(SemanticTokensResult::Tokens(SemanticTokens {
        result_id: None,
        data: ret,
    })))
}

/// Handle document symbol request for outline view
pub async fn handle_document_symbol(
    backend: &Backend,
    params: DocumentSymbolParams,
) -> Result<Option<DocumentSymbolResponse>> {
    let mut ret = Vec::new();
    let files = backend.files.read().await;
    let uri = params.text_document.uri;

    let Some(file_data) = files.get(&uri) else {
        return Err(tower_lsp::jsonrpc::Error::invalid_request());
    };

    let document = &file_data.document_data;

    let Some(ref tree) = document.tree else {
        return Err(tower_lsp::jsonrpc::Error::internal_error());
    };

    let mut cursor = QueryCursor::new();
    let query = Query::new(
        tree_sitter_ic10::language(),
        "(instruction (operation \"define\") . (operand)@name)@define
        (instruction (operation \"alias\") . (operand)@name)@alias
        (instruction (operation \"label\") . (operand)@name)@alias
        (label (identifier)@name)@label",
    )
    .unwrap();
    let define_idx = query.capture_index_for_name("define").unwrap();
    let alias_idx = query.capture_index_for_name("alias").unwrap();
    let label_idx = query.capture_index_for_name("label").unwrap();
    let name_idx = query.capture_index_for_name("name").unwrap();

    let matches = cursor.matches(&query, tree.root_node(), document.content.as_bytes());

    for matched in matches {
        let main_match = {
            let mut ret = None;
            for cap in matched.captures {
                if cap.index == define_idx || cap.index == alias_idx || cap.index == label_idx {
                    ret = Some(cap);
                }
            }
            match ret {
                Some(ret) => ret,
                None => continue,
            }
        };

        let kind = if main_match.index == define_idx {
            SymbolKind::NUMBER
        } else if main_match.index == alias_idx {
            SymbolKind::VARIABLE
        } else if main_match.index == label_idx {
            SymbolKind::FUNCTION
        } else {
            SymbolKind::FILE
        };

        let Some(name_node) = matched.nodes_for_capture_index(name_idx).next() else {
            continue;
        };

        let name = name_node.utf8_text(document.content.as_bytes()).unwrap();
        #[allow(deprecated)]
        ret.push(SymbolInformation {
            name: name.to_string(),
            kind,
            tags: None,
            deprecated: Some(matched.pattern_index == 2),
            location: Location::new(uri.clone(), Range::from(name_node.range()).into()),
            container_name: None,
        });
    }
    Ok(Some(DocumentSymbolResponse::Flat(ret)))
}

/// Handle signature help request for function parameter hints
pub async fn handle_signature_help(
    backend: &Backend,
    params: SignatureHelpParams,
) -> Result<Option<SignatureHelp>> {
    let uri = params.text_document_position_params.text_document.uri;
    let position = Position::from(params.text_document_position_params.position);

    let files = backend.files.read().await;
    let Some(file_data) = files.get(&uri) else {
        return Err(tower_lsp::jsonrpc::Error::invalid_request());
    };

    let document = &file_data.document_data;

    let Some(ref tree) = document.tree else {
        return Err(tower_lsp::jsonrpc::Error::internal_error());
    };

    let Some(node) = backend.node_at_position(position, tree) else {
        return Ok(None);
    };

    let Some(line_node) = node.find_parent("line") else {
        return Ok(None);
    };

    let Some(instruction_node) =
        line_node.query("(instruction)@x", document.content.as_bytes())
    else {
        return Ok(None);
    };

    let Some(operation_node) =
        instruction_node
            .child_by_field_name("operation")
            .or_else(|| {
                instruction_node.query("(invalid_instruction)@x", document.content.as_bytes())
            })
    else {
        return Ok(None);
    };

    let text_raw = operation_node
        .utf8_text(document.content.as_bytes())
        .unwrap();
    let lowered;
    let text: &str = if instructions::INSTRUCTIONS.contains_key(text_raw) {
        text_raw
    } else {
        lowered = text_raw.to_ascii_lowercase();
        lowered.as_str()
    };

    // Convert position (line, column) to document byte offset
    let line_start_byte = line_node.start_byte();
    let cursor_byte = line_start_byte + position.0.character as usize;

    let (current_param, _) = get_current_parameter(
        instruction_node,
        cursor_byte,
        document.content.as_bytes(),
    );

    let Some(_signature) = instructions::INSTRUCTIONS.get(text) else {
        return Ok(None);
    };

    // Use enriched syntax for the display label
    let label = crate::tooltip_documentation::get_instruction_syntax(text);

    let mut parameters: Vec<[u32; 2]> = Vec::new();

    // The label is PLAIN TEXT like: "lbn dest(r?) deviceHash(r?|num) nameHash(r?|num) logicType batchMode"
    // We need to find each parameter token after the instruction name
    // Split by whitespace and skip the first token (instruction name)
    
    let tokens: Vec<&str> = label.split_whitespace().collect();
    
    if tokens.is_empty() {
        return Ok(None);
    }
    
    // Skip first token (instruction name), rest are parameters
    let param_tokens = &tokens[1..];
    
    // Now find byte positions of each parameter in the label string
    let mut search_start = 0;
    
    // Skip past the instruction name first
    if let Some(first_space) = label[search_start..].find(char::is_whitespace) {
        search_start += first_space;
        // Skip whitespace
        while search_start < label.len() && label.as_bytes()[search_start].is_ascii_whitespace() {
            search_start += 1;
        }
    }
    
    for (_idx, param_token) in param_tokens.iter().enumerate() {
        // Find this parameter token in the label starting from search_start
        if let Some(token_pos) = label[search_start..].find(param_token) {
            let abs_start = search_start + token_pos;
            let abs_end = abs_start + param_token.len();
            
            parameters.push([abs_start as u32, abs_end as u32]);
            
            // Move search start past this token
            search_start = abs_end;
            // Skip whitespace for next search
            while search_start < label.len() && label.as_bytes()[search_start].is_ascii_whitespace() {
                search_start += 1;
            }
        }
    }

    Ok(Some(SignatureHelp {
        signatures: vec![SignatureInformation {
            label,
            documentation: instructions::INSTRUCTION_DOCS
                .get(text)
                .map(|x| Documentation::String(x.to_string())),
            parameters: Some(
                parameters
                    .iter()
                    .map(|offset| ParameterInformation {
                        label: ParameterLabel::LabelOffsets(offset.to_owned()),
                        documentation: None,
                    })
                    .collect(),
            ),
            active_parameter: None,
        }],
        active_signature: Some(0),
        active_parameter: Some(current_param as u32),
    }))
}

/// Handle code action request for quick fixes and refactors
pub async fn handle_code_action(
    backend: &Backend,
    params: CodeActionParams,
) -> Result<Option<Vec<CodeActionOrCommand>>> {
    let mut ret = Vec::new();

    let files = backend.files.read().await;
    let Some(file_data) = files.get(&params.text_document.uri) else {
        return Err(tower_lsp::jsonrpc::Error::invalid_request());
    };

    let document = &file_data.document_data;
    let uri = &document.url;

    let Some(ref tree) = document.tree else {
        return Err(tower_lsp::jsonrpc::Error::invalid_request());
    };

    let Some(node) = backend.node_at_range(params.range.into(), tree) else {
        return Ok(None);
    };

    'diagnostics: for diagnostic in params.context.diagnostics {
        let Some(line_node) = node.find_parent("line") else {
            continue 'diagnostics;
        };

        let Some(NumberOrString::String(code)) = diagnostic.code.clone() else {
            continue;
        };
        match code.as_str() {
            LINT_ABSOLUTE_JUMP => {
                const REPLACEMENTS: phf::Map<&'static str, &'static str> = phf::phf_map! {
                    "bdns" => "brdns",
                    "bdse" => "brdse",
                    "bap" => "brap",
                    "bapz" => "brapz",
                    "beq" => "breq",
                    "beqz" => "breqz",
                    "bge" => "brge",
                    "bgez" => "brgez",
                    "bgt" => "brgt",
                    "bgtz" => "brgtz",
                    "ble" => "brle",
                    "blez" => "brlez",
                    "blt" => "brlt",
                    "bltz" => "brltz",
                    "bna" => "brna",
                    "bnaz" => "brnaz",
                    "bne" => "brne",
                    "bnez" => "brnez",
                    "j" => "jr",
                };

                if let Some(node) =
                    line_node.query("(instruction (operation)@x)", document.content.as_bytes())
                {
                    let text = node.utf8_text(document.content.as_bytes()).unwrap();

                    if let Some(replacement) = REPLACEMENTS.get(text) {
                        let edit = TextEdit::new(
                            Range::from(node.range()).into(),
                            replacement.to_string(),
                        );

                        ret.push(CodeActionOrCommand::CodeAction(CodeAction {
                            title: format!("Replace with {replacement}"),
                            kind: Some(CodeActionKind::QUICKFIX),
                            diagnostics: Some(vec![diagnostic]),
                            edit: Some(WorkspaceEdit::new(HashMap::from([(
                                uri.clone(),
                                vec![edit],
                            )]))),
                            command: None,
                            is_preferred: Some(true),
                            disabled: None,
                            data: None,
                        }));
                    }

                    break;
                }
            }
            LINT_RELATIVE_BRANCH_TO_LABEL => {
                const REPLACEMENTS: phf::Map<&'static str, &'static str> = phf::phf_map! {
                    "brdns" => "bdns",
                    "brdnsal" => "bdnsal",
                    "brdse" => "bdse",
                    "brdseal" => "bdseal",
                    "brap" => "bap",
                    "brapz" => "bapz",
                    "brapzal" => "bapzal",
                    "breq" => "beq",
                    "breqal" => "beqal",
                    "breqz" => "beqz",
                    "breqzal" => "beqzal",
                    "brge" => "bge",
                    "brgeal" => "bgeal",
                    "brgez" => "bgez",
                    "brgezal" => "bgezal",
                    "brgt" => "bgt",
                    "brgtal" => "bgtal",
                    "brgtz" => "bgtz",
                    "brgtzal" => "bgtzal",
                    "brle" => "ble",
                    "brleal" => "bleal",
                    "brlez" => "blez",
                    "brlezal" => "blezal",
                    "brlt" => "blt",
                    "brltal" => "bltal",
                    "brltz" => "bltz",
                    "brltzal" => "bltzal",
                    "brna" => "bna",
                    "brnaz" => "bnaz",
                    "brnazal" => "bnazal",
                    "brne" => "bne",
                    "brneal" => "bneal",
                    "brnez" => "bnez",
                    "brnezal" => "bnezal",
                };

                if let Some(node) =
                    line_node.query("(instruction (operation)@x)", document.content.as_bytes())
                {
                    let text = node.utf8_text(document.content.as_bytes()).unwrap();

                    if let Some(replacement) = REPLACEMENTS.get(text) {
                        let edit = TextEdit::new(
                            Range::from(node.range()).into(),
                            replacement.to_string(),
                        );

                        ret.push(CodeActionOrCommand::CodeAction(CodeAction {
                            title: format!("Convert to absolute branch: {replacement}"),
                            kind: Some(CodeActionKind::QUICKFIX),
                            diagnostics: Some(vec![diagnostic]),
                            edit: Some(WorkspaceEdit::new(HashMap::from([(
                                uri.clone(),
                                vec![edit],
                            )]))),
                            command: None,
                            is_preferred: Some(true),
                            disabled: None,
                            data: None,
                        }));
                    }

                    break;
                }
            }
            "register_assigned_not_read" | "register_read_before_assign" => {
                // Extract register name from diagnostic data
                if let Some(data) = &diagnostic.data {
                    if let Some(register_name) = data.as_str() {
                        // Find existing @ignore directive or create a new one at the top
                        let content = &document.content;
                        let mut ignore_line_index = None;
                        let mut existing_registers = Vec::new();

                        // Look for existing ignore directive
                        for (idx, line) in content.lines().enumerate() {
                            if line.contains("# ignore") {
                                ignore_line_index = Some(idx);
                                // Parse existing registers
                                if let Some(ignore_start) = line.find("ignore") {
                                    let after_ignore = &line[ignore_start + 6..].trim();
                                    let registers_str = if after_ignore.starts_with(':') {
                                        &after_ignore[1..].trim()
                                    } else {
                                        after_ignore
                                    };
                                    for reg in registers_str.split(',') {
                                        let reg_name = reg.trim();
                                        if !reg_name.is_empty() {
                                            existing_registers.push(reg_name.to_string());
                                        }
                                    }
                                }
                                break;
                            }
                        }

                        // Add register if not already present
                        if !existing_registers.contains(&register_name.to_string()) {
                            existing_registers.push(register_name.to_string());
                        }

                        let new_ignore_line = format!("# ignore {}", existing_registers.join(", "));

                        let edit = if let Some(line_idx) = ignore_line_index {
                            // Replace existing line
                            TextEdit::new(
                                tower_lsp::lsp_types::Range::new(
                                    tower_lsp::lsp_types::Position::new(line_idx as u32, 0),
                                    tower_lsp::lsp_types::Position::new(line_idx as u32, content.lines().nth(line_idx).unwrap().len() as u32),
                                ),
                                new_ignore_line,
                            )
                        } else {
                            // Insert at top of file
                            TextEdit::new(
                                tower_lsp::lsp_types::Range::new(
                                    tower_lsp::lsp_types::Position::new(0, 0),
                                    tower_lsp::lsp_types::Position::new(0, 0),
                                ),
                                format!("{}\n", new_ignore_line),
                            )
                        };

                        ret.push(CodeActionOrCommand::CodeAction(CodeAction {
                            title: format!("Ignore diagnostics for {}", register_name),
                            kind: Some(CodeActionKind::QUICKFIX),
                            diagnostics: Some(vec![diagnostic.clone()]),
                            edit: Some(WorkspaceEdit::new(HashMap::from([(
                                uri.clone(),
                                vec![edit],
                            )]))),
                            is_preferred: Some(false),
                            ..Default::default()
                        }));
                    }
                }
            }
            _ => {}
        }
    }

    // Add instruction-based code actions for enhanced interactivity
    if let Some(instruction_actions) =
        crate::additional_features::get_instruction_code_actions(&node, &document.content)
    {
        ret.extend(instruction_actions);
    }

    // HASH conversion code actions (HASH("device") <-> number)
    // Check if we're on a hash_function node
    if let Some(hash_func_node) = node.find_parent("hash_function") {
        if let Some(hash_string_node) = hash_func_node.child_by_field_name("argument") {
            let string_text = hash_string_node
                .utf8_text(document.content.as_bytes())
                .unwrap();
            
            // Extract device name without quotes
            if let Some(device_name) = crate::hash_utils::extract_hash_argument(string_text) {
                // Look up the hash value
                if let Some(&hash_value) = crate::device_hashes::DEVICE_NAME_TO_HASH.get(device_name.as_str()) {
                    // Offer to convert HASH("DeviceName") to hash number
                    let edit = TextEdit::new(
                        Range::from(hash_func_node.range()).into(),
                        hash_value.to_string(),
                    );

                    ret.push(CodeActionOrCommand::CodeAction(CodeAction {
                        title: format!("Convert to hash number: {}", hash_value),
                        kind: Some(CodeActionKind::REFACTOR),
                        diagnostics: None,
                        edit: Some(WorkspaceEdit::new(HashMap::from([(
                            uri.clone(),
                            vec![edit],
                        )]))),
                        is_preferred: Some(false),
                        ..Default::default()
                    }));
                }
            }
        }
    }
    
    // Check if we're on a number that is a known device hash
    if node.kind() == "number" {
        let number_text = node.utf8_text(document.content.as_bytes()).unwrap();
        if let Ok(hash_value) = number_text.parse::<i32>() {
            // Check if this is a known device hash by looking it up in the reverse map
            if let Some(display_name) = crate::device_hashes::HASH_TO_DISPLAY_NAME.get(&hash_value) {
                // Find the device name (key) that maps to this hash
                let mut device_name_opt = None;
                for device_name in crate::device_hashes::DEVICE_NAME_TO_HASH.keys() {
                    if crate::device_hashes::DEVICE_NAME_TO_HASH[device_name] == hash_value {
                        device_name_opt = Some(device_name);
                        break;
                    }
                }
                
                if let Some(device_name) = device_name_opt {
                    // Offer to convert hash number to HASH("DeviceName")
                    let edit = TextEdit::new(
                        Range::from(node.range()).into(),
                        format!("HASH(\"{}\")", device_name),
                    );

                    ret.push(CodeActionOrCommand::CodeAction(CodeAction {
                        title: format!("Convert to HASH(\"{}\")", display_name),
                        kind: Some(CodeActionKind::REFACTOR),
                        diagnostics: None,
                        edit: Some(WorkspaceEdit::new(HashMap::from([(
                            uri.clone(),
                            vec![edit],
                        )]))),
                        is_preferred: Some(false),
                        ..Default::default()
                    }));
                }
            }
        }
    }

    Ok(Some(ret))
}

/// Handle goto definition request for navigation
pub async fn handle_goto_definition(
    backend: &Backend,
    params: GotoDefinitionParams,
) -> Result<Option<GotoDefinitionResponse>> {
    let files = backend.files.read().await;
    let Some(file_data) = files.get(&params.text_document_position_params.text_document.uri)
    else {
        return Err(tower_lsp::jsonrpc::Error::internal_error());
    };
    let document = &file_data.document_data;
    let mut type_data = file_data.type_data.clone();

    let position = params.text_document_position_params.position;

    if let Some(tree) = document.tree.as_ref() {
        if let Some(node) = backend.node_at_position(position.into(), tree) {
            if node.kind() == "identifier" {
                let name = node.utf8_text(document.content.as_bytes()).unwrap();
                if let Some(range) = type_data.get_range(name) {
                    return Ok(Some(GotoDefinitionResponse::Scalar(Location::new(
                        document.url.clone(),
                        range.into(),
                    ))));
                }
            }
        }
    }
    Ok(None)
}
