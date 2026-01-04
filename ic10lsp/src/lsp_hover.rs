//! LSP Hover and Inlay Hints Module
//!
//! This module handles hover documentation and inlay hints for the IC10 language server.
//! It provides:
//! - Hover documentation for instructions, registers, defines, aliases, labels
//! - Inlay hints for device hashes, enum values, and instruction parameters

use std::sync::OnceLock;

use tower_lsp::lsp_types::{
    Hover, HoverContents, HoverParams, InlayHint, InlayHintKind, InlayHintLabel,
    InlayHintParams, LanguageString, MarkedString,
};
use tower_lsp::jsonrpc::Result;
use tree_sitter::{Query, QueryCursor};

use ic10lsp::instructions;

use crate::additional_features;
use crate::document::AliasValue;
use crate::tree_utils::{get_current_parameter, NodeEx};
use crate::types::{Position, Range};
use crate::Backend;

// ============================================================================
// Cached Queries (compiled once, reused for all requests)
// ============================================================================

/// Cached query for number nodes (device hash lookups)
fn query_number() -> &'static Query {
    static QUERY: OnceLock<Query> = OnceLock::new();
    QUERY.get_or_init(|| {
        Query::new(tree_sitter_ic10::language(), "(number)@x").unwrap()
    })
}

/// Cached query for hash function calls
fn query_hash_function() -> &'static Query {
    static QUERY: OnceLock<Query> = OnceLock::new();
    QUERY.get_or_init(|| {
        Query::new(tree_sitter_ic10::language(), "(hash_function)@call").unwrap()
    })
}

/// Cached query for enum identifiers
fn query_enum_identifier() -> &'static Query {
    static QUERY: OnceLock<Query> = OnceLock::new();
    QUERY.get_or_init(|| {
        Query::new(tree_sitter_ic10::language(), "(identifier)@x").unwrap()
    })
}

/// Cached query for define instructions with number operands
fn query_define_number() -> &'static Query {
    static QUERY: OnceLock<Query> = OnceLock::new();
    QUERY.get_or_init(|| {
        Query::new(
            tree_sitter_ic10::language(),
            "(instruction (operation \"define\") (operand)@name (operand (number)@value))@inst",
        ).unwrap()
    })
}

/// Cached query for instruction nodes
fn query_instruction() -> &'static Query {
    static QUERY: OnceLock<Query> = OnceLock::new();
    QUERY.get_or_init(|| {
        Query::new(tree_sitter_ic10::language(), "(instruction)@i").unwrap()
    })
}

/// Cached query for str function calls
fn query_str_function() -> &'static Query {
    static QUERY: OnceLock<Query> = OnceLock::new();
    QUERY.get_or_init(|| {
        Query::new(tree_sitter_ic10::language(), "(str_function)@call").unwrap()
    })
}

/// Cached query for identifiers (for enum resolution)
fn query_identifier() -> &'static Query {
    static QUERY: OnceLock<Query> = OnceLock::new();
    QUERY.get_or_init(|| {
        Query::new(tree_sitter_ic10::language(), "(identifier)@id").unwrap()
    })
}

/// Warm up all cached queries at startup to eliminate first-request latency
pub fn warmup_queries() {
    let _ = query_number();
    let _ = query_hash_function();
    let _ = query_enum_identifier();
    let _ = query_define_number();
    let _ = query_instruction();
    let _ = query_str_function();
    let _ = query_identifier();
}

/// Handle hover requests - delegates to internal implementation
pub async fn handle_hover(backend: &Backend, params: HoverParams) -> Result<Option<Hover>> {
    let _timer = crate::performance::TimingGuard::new(&backend.perf_tracker, "lsp.server.hover");
    backend.perf_tracker.increment("lsp.server.hover.calls", 1);
    
    let files = backend.files.read().await;
    let Some(file_data) = files.get(&params.text_document_position_params.text_document.uri)
    else {
        return Err(tower_lsp::jsonrpc::Error::internal_error());
    };
    let document = &file_data.document_data;
    let type_data = file_data.type_data.clone();

    let position = params.text_document_position_params.position;

    let Some(tree) = document.tree.as_ref() else {
        return Ok(None);
    };
    let root = tree.root_node();
    let Some(node) = root.named_descendant_for_point_range(
        tree_sitter::Point::new(position.line as usize, position.character as usize),
        tree_sitter::Point::new(position.line as usize, position.character as usize + 1),
    ) else {
        return Ok(None);
    };

    let name = node.utf8_text(document.content.as_bytes()).unwrap();
    match node.kind() {
        "identifier" => {
            // Check for _unnamed enum members first (NotEquals, Equals, Greater, Less)
            if let Some(value) = instructions::resolve_unnamed_enum_member(name) {
                let mut parts: Vec<MarkedString> = Vec::new();
                parts.push(MarkedString::LanguageString(LanguageString {
                    language: "ic10".to_string(),
                    value: format!("{} = {}", name, value),
                }));
                parts.push(MarkedString::String(format!(
                    "**Constant**\n\nNumeric value: `{}`\n\nUsed for comparison operations.",
                    value
                )));
                return Ok(Some(Hover {
                    contents: HoverContents::Array(parts),
                    range: Some(Range::from(node.range()).into()),
                }));
            }

            // Enum hover: show value and description for fully-qualified enums
            if name.contains('.') {
                if let Some((canonical, value, desc, deprecated)) =
                    instructions::enum_info_case_insensitive(name)
                {
                    let mut parts: Vec<MarkedString> = Vec::new();
                    parts.push(MarkedString::LanguageString(LanguageString {
                        language: "ic10".to_string(),
                        value: format!("{} = {}", canonical, value),
                    }));
                    let mut md = String::new();
                    md.push_str(&format!("**{}**\n\nValue: `{}`", canonical, value));
                    if !desc.is_empty() {
                        md.push_str(&format!("\n\n{}", desc));
                    }
                    if deprecated {
                        md.push_str("\n\n**Deprecated**");
                    }
                    if canonical != name {
                        md.push_str(&format!(
                            "\n\n_Case differs: typed '{}' → canonical '{}'_.",
                            name, canonical
                        ));
                    }
                    parts.push(MarkedString::String(md));
                    return Ok(Some(Hover {
                        contents: HoverContents::Array(parts),
                        range: Some(Range::from(node.range()).into()),
                    }));
                }
            }
            if let Some(definition_data) = type_data.defines.get(name) {
                // Check if this is a HASH() function call
                if let Some(parent) = node.parent() {
                    if parent.kind() == "function_call" {
                        let parent_text =
                            parent.utf8_text(document.content.as_bytes()).unwrap();
                        if let Some(device_name) =
                            crate::hash_utils::extract_hash_argument(parent_text)
                        {
                            if let Some(device_hash) =
                                crate::hash_utils::get_device_hash(&device_name)
                            {
                                let mut parts: Vec<MarkedString> = Vec::new();
                                parts.push(MarkedString::LanguageString(LanguageString {
                                    language: "ic10".to_string(),
                                    value: format!(
                                        "HASH(\"{}\") = {}",
                                        device_name, device_hash
                                    ),
                                }));
                                if let Some(device_display_name) =
                                    crate::hash_utils::get_device_name_for_hash(device_hash)
                                {
                                    parts.push(MarkedString::String(device_display_name.to_string()));
                                }
                                return Ok(Some(Hover {
                                    contents: HoverContents::Array(parts),
                                    range: Some(Range::from(parent.range()).into()),
                                }));
                            }
                        }
                    }
                }

                // Handle defines - show resolved numeric hash if available
                let device_hash_value = definition_data.value.resolved_numeric();
                let device_display_name = device_hash_value
                    .and_then(crate::hash_utils::get_device_name_for_hash);

                if device_display_name.is_some() || device_hash_value.is_some() {
                    let mut parts: Vec<MarkedString> = Vec::new();
                    parts.push(MarkedString::LanguageString(LanguageString {
                        language: "ic10".to_string(),
                        value: format!("define {} {}", name, definition_data.value),
                    }));
                    if let Some(hash) = device_hash_value {
                        parts.push(MarkedString::LanguageString(LanguageString {
                            language: "ic10".to_string(),
                            value: format!("// resolved hash = {}", hash),
                        }));
                    }
                    if let Some(device_name) = device_display_name {
                        parts.push(MarkedString::String(device_name.to_string()));
                    }
                    return Ok(Some(Hover {
                        contents: HoverContents::Array(parts),
                        range: Some(Range::from(node.range()).into()),
                    }));
                } else {
                    return Ok(Some(Hover {
                        contents: HoverContents::Array(vec![MarkedString::LanguageString(
                            LanguageString {
                                language: "ic10".to_string(),
                                value: format!("define {} {}", name, definition_data.value),
                            },
                        )]),
                        range: Some(Range::from(node.range()).into()),
                    }));
                }
            }
            // If an identifier text matches a known logic or slot type name, show its docs
            if let Some(doc) = instructions::LOGIC_TYPE_DOCS.get(name) {
                return Ok(Some(Hover {
                    contents: HoverContents::Array(vec![MarkedString::String(format!(
                        "# `{}` (`logicType`)\n{}",
                        name, doc
                    ))]),
                    range: Some(Range::from(node.range()).into()),
                }));
            }
            if let Some(doc) = instructions::SLOT_TYPE_DOCS.get(name) {
                return Ok(Some(Hover {
                    contents: HoverContents::Array(vec![MarkedString::String(format!(
                        "# `{}` (`logicSlotType`)\n{}",
                        name, doc
                    ))]),
                    range: Some(Range::from(node.range()).into()),
                }));
            }
            if let Some(doc) = instructions::BATCH_MODE_DOCS.get(name) {
                return Ok(Some(Hover {
                    contents: HoverContents::Array(vec![MarkedString::String(format!(
                        "# `{}` (`batchMode`)\n{}",
                        name, doc
                    ))]),
                    range: Some(Range::from(node.range()).into()),
                }));
            }
            if let Some(definition_data) = type_data.aliases.get(name) {
                // Check if this is a register alias and provide value tracking info
                if let AliasValue::Register(_) = &definition_data.value {
                    // Perform register analysis to get current value information
                    let mut register_analyzer = additional_features::RegisterAnalyzer::new();
                    if let Some(tree) = document.tree.as_ref() {
                        register_analyzer.analyze_register_usage(
                            tree,
                            &document.content,
                            &type_data.aliases,
                        );

                        if let Some(register_info) = register_analyzer.get_register_info(name) {
                            let register_name = definition_data.value.to_string();
                            let mut hover_content =
                                vec![MarkedString::LanguageString(LanguageString {
                                    language: "ic10".to_string(),
                                    value: format!("alias {} {}", name, definition_data.value),
                                })];

                            // Add register information with simple operation history
                            let mut value_parts = vec![];

                            value_parts
                                .push(format!("**Register** {} ({})", name, register_name));

                            // Add operation history if available
                            if !register_info.operation_history.is_empty() {
                                value_parts.push("**Operation history:**".to_string());
                                let history_limit = 99; // Show up to 99 operations (tooltip is scrollable)
                                let start_idx =
                                    if register_info.operation_history.len() > history_limit {
                                        register_info.operation_history.len() - history_limit
                                    } else {
                                        0
                                    };

                                for record in &register_info.operation_history[start_idx..] {
                                    value_parts.push(format!(
                                        "  • Line {}: {}",
                                        record.line_number, record.operation
                                    ));
                                }

                                if start_idx > 0 {
                                    value_parts.push(format!(
                                        "  • ... ({} earlier operations)",
                                        start_idx
                                    ));
                                }
                            } else {
                                value_parts.push(
                                    "**Operation history:** No operations found".to_string(),
                                );
                            }

                            let value_info = value_parts.join("\n\n");

                            hover_content.push(MarkedString::String(value_info));

                            return Ok(Some(Hover {
                                contents: HoverContents::Array(hover_content),
                                range: Some(Range::from(node.range()).into()),
                            }));
                        }
                    }
                }

                // Fallback to basic alias information
                return Ok(Some(Hover {
                    contents: HoverContents::Array(vec![MarkedString::LanguageString(
                        LanguageString {
                            language: "ic10".to_string(),
                            value: format!("alias {} {}", name, definition_data.value),
                        },
                    )]),
                    range: Some(Range::from(node.range()).into()),
                }));
            }
            if let Some(definition_data) = type_data.labels.get(name) {
                return Ok(Some(Hover {
                    contents: HoverContents::Scalar(MarkedString::String(format!(
                        "Label on line {}",
                        definition_data.value + 1
                    ))),
                    range: Some(Range::from(node.range()).into()),
                }));
            }
        }
        "operation" | "invalid_instruction" => {
            let canonical_lowered;
            let canonical: &str = if instructions::INSTRUCTIONS.contains_key(name) {
                name
            } else {
                canonical_lowered = name.to_ascii_lowercase();
                canonical_lowered.as_str()
            };
            if let Some(_signature) = instructions::INSTRUCTIONS.get(canonical) {
                // Find the parent instruction node to analyze registers
                let instruction_node = node.find_parent("instruction").unwrap_or(node);

                // Create register analyzer to get operation history
                let mut register_analyzer = additional_features::RegisterAnalyzer::new();
                if let Some(tree) = document.tree.as_ref() {
                    register_analyzer.analyze_register_usage(
                        tree,
                        &document.content,
                        &type_data.aliases,
                    );
                }

                return Ok(Some(Hover {
                    contents: HoverContents::Array(
                        crate::tooltip_documentation::create_enhanced_instruction_hover_with_history(
                            canonical,
                            instruction_node,
                            &document.content,
                            &register_analyzer,
                        ),
                    ),
                    range: Some(Range::from(node.range()).into()),
                }));
            }
        }
        "logictype" => {
            // Try to get contextual information if available
            let candidates = if let Some(instruction_node) = node.find_parent("instruction") {
                if let Some(operation_node) = instruction_node.child_by_field_name("operation") {
                    let operation = operation_node
                        .utf8_text(document.content.as_bytes())
                        .unwrap();

                    let (current_param, _) =
                        get_current_parameter(instruction_node, position.character as usize, document.content.as_bytes());

                    let candidates = instructions::logictype_candidates(name);

                    if let Some(signature) = instructions::INSTRUCTIONS.get(operation) {
                        if let Some(param_type) = signature.0.get(current_param) {
                            param_type.intersection(&candidates)
                        } else {
                            candidates
                        }
                    } else {
                        candidates
                    }
                } else {
                    // No operation node, use all candidates
                    instructions::logictype_candidates(name)
                }
            } else {
                // No instruction context, use all candidates
                instructions::logictype_candidates(name)
            };

            let strings: Vec<MarkedString> = candidates
                .iter()
                .map(|typ| {
                    MarkedString::String(format!("# `{}` (`{}`)\n{}", name, typ, {
                        use instructions::DataType;
                        match typ {
                            DataType::LogicType => instructions::LOGIC_TYPE_DOCS.get(name),
                            DataType::SlotLogicType => instructions::SLOT_TYPE_DOCS.get(name),
                            DataType::BatchMode => instructions::BATCH_MODE_DOCS.get(name),
                            _ => None,
                        }
                        .unwrap_or(&"")
                    }))
                })
                .collect();

            // If no candidates matched but we have documentation, show it anyway
            if strings.is_empty() {
                let mut fallback_parts: Vec<MarkedString> = Vec::new();
                
                if let Some(doc) = instructions::LOGIC_TYPE_DOCS.get(name) {
                    fallback_parts.push(MarkedString::String(format!(
                        "# `{}` (`logicType`)\n{}",
                        name, doc
                    )));
                }
                if let Some(doc) = instructions::SLOT_TYPE_DOCS.get(name) {
                    fallback_parts.push(MarkedString::String(format!(
                        "# `{}` (`logicSlotType`)\n{}",
                        name, doc
                    )));
                }
                if let Some(doc) = instructions::BATCH_MODE_DOCS.get(name) {
                    fallback_parts.push(MarkedString::String(format!(
                        "# `{}` (`batchMode`)\n{}",
                        name, doc
                    )));
                }
                
                if !fallback_parts.is_empty() {
                    return Ok(Some(Hover {
                        contents: HoverContents::Array(fallback_parts),
                        range: Some(Range::from(node.range()).into()),
                    }));
                }
            }

            if !strings.is_empty() {
                return Ok(Some(Hover {
                    contents: HoverContents::Array(strings),
                    range: Some(Range::from(node.range()).into()),
                }));
            }
        }
        "hash_function" | "function_call" | "hash_string" | "hash_keyword" => {
            // For hash_string or hash_keyword, try to get the parent hash_function
            let hash_node = if matches!(node.kind(), "hash_string" | "hash_keyword") {
                node.parent()
            } else {
                Some(node)
            };
            
            if let Some(hash_node) = hash_node {
                let text = hash_node.utf8_text(document.content.as_bytes()).unwrap();
                if let Some(device_name) = crate::hash_utils::extract_hash_argument(text) {
                    if let Some(device_hash) = crate::hash_utils::get_device_hash(&device_name) {
                        let mut parts: Vec<MarkedString> = Vec::new();
                        
                        // Show the hash function and value
                        parts.push(MarkedString::LanguageString(LanguageString {
                            language: "ic10".to_string(),
                            value: format!("HASH(\"{}\") = {}", device_name, device_hash),
                        }));
                        
                        // Add device display name and description if available
                        if let Some((display_name, description)) = 
                            crate::descriptions::get_device_description(&device_name) 
                        {
                            let mut md_text = format!("**{}**", display_name);
                            if !description.is_empty() {
                                md_text.push_str(&format!("\n\n{}", description));
                            }
                            parts.push(MarkedString::String(md_text));
                        } else if let Some(device_display_name) =
                            crate::hash_utils::get_device_name_for_hash(device_hash)
                        {
                            // Fallback to display name only if no description available
                            parts.push(MarkedString::String(device_display_name.to_string()));
                        }
                        
                        return Ok(Some(Hover {
                            contents: HoverContents::Array(parts),
                            range: Some(Range::from(node.range()).into()),
                        }));
                    }
                }
            }
        }
        "register" => {
            // Handle direct register hover (e.g., hovering over "r0", "r1", etc.)
            let mut register_analyzer = additional_features::RegisterAnalyzer::new();
            if let Some(tree) = document.tree.as_ref() {
                register_analyzer.analyze_register_usage(
                    tree,
                    &document.content,
                    &type_data.aliases,
                );

                if let Some(register_info) = register_analyzer.get_register_info(name) {
                    let mut hover_content = vec![];

                    // Add register declaration info
                    let register_display = if let Some(alias) = &register_info.alias_name {
                        format!("alias {} {}", alias, name)
                    } else {
                        format!("register {}", name)
                    };

                    // For direct registers, don't show both the language string and the markdown header
                    let mut value_parts = vec![];

                    let display_name = register_info
                        .alias_name
                        .as_ref()
                        .map(|alias| format!("{} ({})", alias, name))
                        .unwrap_or_else(|| name.to_string());

                    // Only show one header - either the alias info or the register info
                    if register_info.alias_name.is_some() {
                        hover_content.push(MarkedString::LanguageString(LanguageString {
                            language: "ic10".to_string(),
                            value: register_display,
                        }));
                        value_parts.push(format!("**Register** {}", display_name));
                    } else {
                        // For bare registers, just show the register info without duplicate
                        value_parts.push(format!("**Register** {}", display_name));
                    }

                    // Add operation history if available
                    if !register_info.operation_history.is_empty() {
                        value_parts.push("**Operation history:**".to_string());
                        let history_limit = 99; // Show up to 99 operations (tooltip is scrollable)
                        let start_idx = if register_info.operation_history.len() > history_limit
                        {
                            register_info.operation_history.len() - history_limit
                        } else {
                            0
                        };

                        for record in &register_info.operation_history[start_idx..] {
                            value_parts.push(format!(
                                "  • Line {}: {}",
                                record.line_number, record.operation
                            ));
                        }

                        if start_idx > 0 {
                            value_parts
                                .push(format!("  • ... ({} earlier operations)", start_idx));
                        }
                    } else {
                        value_parts
                            .push("**Operation history:** No operations found".to_string());
                    }

                    let value_info = value_parts.join("\n\n");

                    hover_content.push(MarkedString::String(value_info));

                    return Ok(Some(Hover {
                        contents: HoverContents::Array(hover_content),
                        range: Some(Range::from(node.range()).into()),
                    }));
                }
            }
        }
        "number" => {
            // Check if this number is a known device hash
            if let Ok(hash_value) = name.parse::<i32>() {
                if let Some(device_display_name) = crate::hash_utils::get_device_name_for_hash(hash_value) {
                    // Try to find the prefab name from the hash value
                    let mut prefab_name_opt = None;
                    for (prefab, &hash) in crate::device_hashes::DEVICE_NAME_TO_HASH.entries() {
                        if hash == hash_value {
                            prefab_name_opt = Some(*prefab);
                            break;
                        }
                    }
                    
                    let mut parts: Vec<MarkedString> = Vec::new();
                    parts.push(MarkedString::LanguageString(LanguageString {
                        language: "ic10".to_string(),
                        value: format!("Device Hash: {}", hash_value),
                    }));
                    
                    // Try to get description from English.xml
                    if let Some(prefab_name) = prefab_name_opt {
                        if let Some((display_name, description)) = 
                            crate::descriptions::get_device_description(prefab_name) 
                        {
                            let mut md_text = format!("**{}**", display_name);
                            if !description.is_empty() {
                                md_text.push_str(&format!("\n\n{}", description));
                            }
                            md_text.push_str(&format!("\n\n_Prefab: `{}`_", prefab_name));
                            parts.push(MarkedString::String(md_text));
                        } else {
                            // Fallback if no description available
                            parts.push(MarkedString::String(format!(
                                "**{}**\n\nThis is the hash value for the `{}` device/prefab.",
                                device_display_name, prefab_name
                            )));
                        }
                    } else {
                        // Fallback if prefab name not found
                        parts.push(MarkedString::String(format!(
                            "**{}**\n\nDevice hash value.",
                            device_display_name
                        )));
                    }
                    
                    return Ok(Some(Hover {
                        contents: HoverContents::Array(parts),
                        range: Some(Range::from(node.range()).into()),
                    }));
                }
            }
        }
        _ => {}
    }
    Ok(None)
}

/// Handle inlay hint requests
pub async fn handle_inlay_hint(backend: &Backend, params: InlayHintParams) -> Result<Option<Vec<InlayHint>>> {
    let start_total = std::time::Instant::now();
    let mut ret = Vec::new();

    let start_lock = std::time::Instant::now();
    let files = backend.files.read().await;
    eprintln!("[PERF] files.read() lock: {:?}", start_lock.elapsed());
    
    let uri = params.text_document.uri;
    let Some(file_data) = files.get(&uri) else {
        return Err(tower_lsp::jsonrpc::Error::invalid_request());
    };

    let document = &file_data.document_data;

    let Some(ref tree) = document.tree else {
        return Err(tower_lsp::jsonrpc::Error::internal_error());
    };

    // Use cached queries for better performance
    let mut cursor = QueryCursor::new();

    let start_numbers = std::time::Instant::now();
    // Process all number nodes (direct numeric hashes)
    for (capture, _) in cursor.captures(query_number(), tree.root_node(), document.content.as_bytes()) {
        let node = capture.captures[0].node;

        let range = Range::from(node.range());
        if !range.contains(node.range().start_point.into())
            || !range.contains(node.range().end_point.into())
        {
            continue;
        }

        let text = node.utf8_text(document.content.as_bytes()).unwrap();

        // Direct numeric device hash lookup
        if let Ok(number) = text.parse::<i32>() {
            if let Some(item_name) = crate::device_hashes::HASH_TO_DISPLAY_NAME.get(&number) {
                let Some(line_node) = node.find_parent("line") else {
                    continue;
                };
                let endpos = if let Some(newline) =
                    line_node.find_newline(document.content.as_bytes())
                {
                    Position::from(newline.range().start_point)
                } else if let Some(instruction) =
                    line_node.find_instruction(document.content.as_bytes())
                {
                    Position::from(instruction.range().end_point)
                } else {
                    Position::from(node.range().end_point)
                };
                ret.push(InlayHint {
                    position: endpos.into(),
                    label: InlayHintLabel::String(format!(" → {}", item_name)),
                    kind: Some(InlayHintKind::TYPE),
                    text_edits: None,
                    tooltip: None,
                    padding_left: None,
                    padding_right: None,
                    data: None,
                });
            }
        }
    }
    eprintln!("[PERF] number queries: {:?}", start_numbers.elapsed());

    // Also show inlays for HASH("...") functions (hash_function in the grammar)
    let start_hash = std::time::Instant::now();
    let mut cursor_hash = QueryCursor::new();

    for (cap, _) in cursor_hash.captures(query_hash_function(), tree.root_node(), document.content.as_bytes()) {
        let call_node = cap.captures[0].node;
        
        // Skip incomplete HASH() calls - check if node has errors or is missing closing paren
        if call_node.has_error() {
            continue;
        }
        
        let call_text = call_node.utf8_text(document.content.as_bytes()).unwrap();
        
        // Also skip if the text doesn't end with ) - incomplete HASH
        if !call_text.trim().ends_with(')') {
            continue;
        }
        
        if let Some(device_name) = crate::hash_utils::extract_hash_argument(call_text) {
            if let Some(hash_val) = crate::hash_utils::get_device_hash(&device_name) {
                // Look up the display name for this hash
                let display_text = crate::device_hashes::HASH_TO_DISPLAY_NAME
                    .get(&hash_val)
                    .copied()
                    .unwrap_or("Unknown Device");
                
                let Some(line_node) = call_node.find_parent("line") else {
                    continue;
                };

                let endpos = if let Some(newline) =
                    line_node.find_newline(document.content.as_bytes())
                {
                    Position::from(newline.range().start_point)
                } else if let Some(instruction) =
                    line_node.find_instruction(document.content.as_bytes())
                {
                    Position::from(instruction.range().end_point)
                } else {
                    Position::from(call_node.range().end_point)
                };

                ret.push(InlayHint {
                    position: endpos.into(),
                    label: InlayHintLabel::String(format!(" → {}", display_text)),
                    kind: Some(InlayHintKind::TYPE),
                    text_edits: None,
                    tooltip: None,
                    padding_left: None,
                    padding_right: None,
                    data: None,
                });
            }
        }
    }
    eprintln!("[PERF] hash_function queries: {:?}", start_hash.elapsed());
    
    // Also show inlays for STR("...") functions (str_function in the grammar)
    let start_str = std::time::Instant::now();
    let mut cursor_str = QueryCursor::new();

    for (cap, _) in cursor_str.captures(query_str_function(), tree.root_node(), document.content.as_bytes()) {
        let call_node = cap.captures[0].node;
        let call_text = call_node.utf8_text(document.content.as_bytes()).unwrap();
        if let Some(string_content) = crate::hash_utils::extract_str_argument(call_text) {
            // Compute the hash value for the string
            let hash_val = crate::hash_utils::compute_crc32(&string_content);
            
            let Some(line_node) = call_node.find_parent("line") else {
                continue;
            };

            let endpos = if let Some(newline) =
                line_node.find_newline(document.content.as_bytes())
            {
                Position::from(newline.range().start_point)
            } else if let Some(instruction) =
                line_node.find_instruction(document.content.as_bytes())
            {
                Position::from(instruction.range().end_point)
            } else {
                Position::from(call_node.range().end_point)
            };

            ret.push(InlayHint {
                position: endpos.into(),
                label: InlayHintLabel::String(format!(" → {}", hash_val)),
                kind: Some(InlayHintKind::TYPE),
                text_edits: None,
                tooltip: None,
                padding_left: None,
                padding_right: None,
                data: None,
            });
        }
    }
    eprintln!("[PERF] str_function queries: {:?}", start_str.elapsed());

    // Show inlay hints for _unnamed enum members (NotEquals, Equals, Greater, Less)
    let start_ident = std::time::Instant::now();
    let mut cursor_ident = QueryCursor::new();

    for (cap, _) in cursor_ident.captures(query_identifier(), tree.root_node(), document.content.as_bytes()) {
        let ident_node = cap.captures[0].node;
        let ident_text = ident_node.utf8_text(document.content.as_bytes()).unwrap();
        
        // Check if this identifier is a _unnamed enum member
        if let Some(value) = crate::instructions::resolve_unnamed_enum_member(ident_text) {
            let Some(line_node) = ident_node.find_parent("line") else {
                continue;
            };

            let endpos = if let Some(newline) =
                line_node.find_newline(document.content.as_bytes())
            {
                Position::from(newline.range().start_point)
            } else if let Some(instruction) =
                line_node.find_instruction(document.content.as_bytes())
            {
                Position::from(instruction.range().end_point)
            } else {
                Position::from(ident_node.range().end_point)
            };

            ret.push(InlayHint {
                position: endpos.into(),
                label: InlayHintLabel::String(format!(" → {}", value)),
                kind: Some(InlayHintKind::TYPE),
                text_edits: None,
                tooltip: Some(tower_lsp::lsp_types::InlayHintTooltip::String(format!("Constant: {} = {}", ident_text, value))),
                padding_left: None,
                padding_right: None,
                data: None,
            });
        }
    }
    eprintln!("[PERF] identifier queries: {:?}", start_ident.elapsed());
    
    // NOTE: Instruction parameter hints are handled client-side for instant display.
    // The LSP only provides device hash hints and enum value hints.

    eprintln!("[PERF] TOTAL inlay_hint: {:?} (hints: {})", start_total.elapsed(), ret.len());
    Ok(Some(ret))
}
