//! # Completion Handler Module
//!
//! This module provides LSP completion functionality for IC10 assembly language.
//! It handles:
//! - Instruction completions (mnemonics like `add`, `sub`, etc.)
//! - Parameter completions (registers, devices, logic types, etc.)
//! - Dynamic completions (aliases, defines, labels, enums)
//! - HASH() function completions for device names
//! - Context-aware completions based on parameter types

use crate::document::{DefinitionData, HasType};
use crate::instructions::{self, DataType};
use crate::performance;
use crate::tree_utils::{get_current_parameter, NodeEx};
use crate::types::Position;
use std::collections::HashMap;
use tower_lsp::jsonrpc::Result;
use tower_lsp::lsp_types::*;

/// Main completion handler function that processes completion requests
/// 
/// # Arguments
/// * `backend` - Reference to the Backend struct for accessing files and configuration
/// * `params` - LSP completion parameters containing position and document info
/// 
/// # Returns
/// Optional completion response with array of completion items
pub async fn handle_completion(
    backend: &crate::Backend,
    params: CompletionParams,
) -> Result<Option<CompletionResponse>> {
    let _timer = performance::TimingGuard::new(&backend.perf_tracker, "lsp.server.completion");
    backend.perf_tracker.increment("lsp.server.completion.calls", 1);

    let mut ret = Vec::new();

    let uri = params.text_document_position.text_document.uri;
    let original_position = params.text_document_position.position;
    let position = {
        let pos = params.text_document_position.position;
        Position::from(tower_lsp::lsp_types::Position::new(
            pos.line,
            pos.character.saturating_sub(1),
        ))
    };

    let files = backend.files.read().await;
    let Some(file_data) = files.get(&uri) else {
        return Err(tower_lsp::jsonrpc::Error::invalid_request());
    };

    let document = &file_data.document_data;

    let Some(ref tree) = document.tree else {
        return Err(tower_lsp::jsonrpc::Error::internal_error());
    };

    let Some(node) = backend.node_at_position(position, tree) else {
        // Tree-sitter hasn't parsed this position yet (common after typing space)
        // Fall back to text-based completion logic
        let actual_line = document
            .content
            .lines()
            .nth(original_position.line as usize)
            .unwrap_or("");
        let cursor_col = original_position.character as usize;
        let text_up_to_cursor = if cursor_col <= actual_line.len() {
            &actual_line[..cursor_col]
        } else {
            actual_line
        };

        let first_word = actual_line.split_whitespace().next().unwrap_or("");

        // Check if this line starts with a known instruction
        if let Some(_signature) = instructions::INSTRUCTIONS.get(first_word) {
            let param_count = text_up_to_cursor
                .split_whitespace()
                .count()
                .saturating_sub(1);
            let suggest_hash = (first_word == "sbn" && (param_count == 0 || param_count == 1))
                || (first_word == "lbn" && (param_count == 1 || param_count == 2))
                || (first_word == "define" && param_count == 1);

            if suggest_hash {
                // Check if already typing HASH(
                let last_hash_open = text_up_to_cursor
                    .rfind("HASH(\"")
                    .or_else(|| text_up_to_cursor.rfind("hash(\""));
                let last_hash_close = text_up_to_cursor.rfind("\")");
                let typing_in_hash = if let Some(open_pos) = last_hash_open {
                    last_hash_close.map_or(true, |close_pos| close_pos < open_pos)
                } else {
                    false
                };

                if typing_in_hash {
                    // Offer device name completions
                    let search_start = text_up_to_cursor
                        .rfind("HASH(\"")
                        .or_else(|| text_up_to_cursor.rfind("hash(\""));
                    if let Some(start_pos) = search_start {
                        let search_text = &text_up_to_cursor[start_pos + 6..];
                        let search_lower = search_text.to_lowercase();

                        for hash_name in crate::device_hashes::DEVICE_NAME_TO_HASH.keys() {
                            let hash_value = crate::device_hashes::DEVICE_NAME_TO_HASH[hash_name];
                            let display_name = crate::device_hashes::HASH_TO_DISPLAY_NAME
                                .get(&hash_value)
                                .unwrap_or(hash_name);

                            let matches = search_text.is_empty()
                                || hash_name.to_lowercase().contains(&search_lower)
                                || display_name.to_lowercase().contains(&search_lower);

                            if matches {
                                ret.push(CompletionItem {
                                    label: hash_name.to_string(),
                                    kind: Some(CompletionItemKind::CONSTANT),
                                    detail: Some(format!("{} → {}", display_name, hash_value)),
                                    documentation: Some(Documentation::String(format!(
                                        "Device: {}\nHash: {}",
                                        display_name, hash_value
                                    ))),
                                    insert_text: Some(format!("{}\")", hash_name)),
                                    insert_text_format: Some(InsertTextFormat::PLAIN_TEXT),
                                    ..Default::default()
                                });
                            }
                        }
                    }
                } else {
                    // Offer HASH(" completion
                    ret.insert(
                        0,
                        CompletionItem {
                            label: "HASH(\"…)".to_string(),
                            kind: Some(CompletionItemKind::SNIPPET),
                            detail: Some("→ Device hash by name".to_string()),
                            documentation: Some(Documentation::String(
                                "Type device name inside quotes to get its hash value".to_string(),
                            )),
                            insert_text: Some("HASH(\"".to_string()),
                            filter_text: Some("H".to_string()),
                            insert_text_format: Some(InsertTextFormat::PLAIN_TEXT),
                            sort_text: Some("!".to_string()),
                            preselect: Some(true),
                            ..Default::default()
                        },
                    );
                }
            }
        }

        return Ok(Some(CompletionResponse::Array(ret)));
    };

    // Global HASH(" detection - trigger device completions anywhere HASH(" is typed
    // This works in defines, instructions, anywhere a device hash might be used
    if let Some(line_node) = node.find_parent("line") {
        let line_text = line_node.utf8_text(document.content.as_bytes()).unwrap();

        // Get cursor position within the line by using byte offsets
        // Use original_position (before saturating_sub) for accurate byte calculation
        // Account for actual line ending bytes in the document (could be \n or \r\n)
        let line_start_byte = line_node.start_byte();

        // Calculate byte position by counting actual bytes including line endings
        let cursor_byte = if original_position.line == 0 {
            original_position.character as usize
        } else {
            // Find the actual byte offset by iterating through the content
            let mut byte_offset = 0;
            let mut line_count = 0;
            for (i, ch) in document.content.char_indices() {
                if line_count >= original_position.line as usize {
                    byte_offset = i + original_position.character as usize;
                    break;
                }
                if ch == '\n' {
                    line_count += 1;
                }
            }
            byte_offset
        };

        let cursor_pos_in_line = if cursor_byte >= line_start_byte {
            cursor_byte - line_start_byte
        } else {
            0
        };

        let line_up_to_cursor = &line_text[..cursor_pos_in_line.min(line_text.len())];

        // Check if we're typing inside HASH("
        let last_hash_open = line_up_to_cursor
            .rfind("HASH(\"")
            .or_else(|| line_up_to_cursor.rfind("hash(\""));
        let last_hash_close = line_up_to_cursor.rfind("\")");

        let typing_in_hash = if let Some(open_pos) = last_hash_open {
            last_hash_close.map_or(true, |close_pos| close_pos < open_pos)
        } else {
            false
        };

        if typing_in_hash {
            let search_start = line_up_to_cursor
                .rfind("HASH(\"")
                .or_else(|| line_up_to_cursor.rfind("hash(\""));
            if let Some(start_pos) = search_start {
                let search_text = &line_up_to_cursor[start_pos + 6..];
                let search_lower = search_text.to_lowercase();

                // Check if already complete
                let already_complete = if let Some(open_pos) = last_hash_open {
                    let slice_result = line_text.get(open_pos..);
                    if let Some(slice) = slice_result {
                        slice.contains("\")")
                    } else {
                        false
                    }
                } else {
                    false
                };

                // If HASH is already complete (has closing "), don't offer device completions
                // Let it fall through to normal parameter completion logic
                if already_complete {
                    // Don't return here - let it continue to normal parameter completion
                } else {
                    // Provide device name completions
                    #[allow(unused_variables)]
                    let mut match_count = 0;
                    for hash_name in crate::device_hashes::DEVICE_NAME_TO_HASH.keys() {
                        let hash_value = crate::device_hashes::DEVICE_NAME_TO_HASH[hash_name];
                        let display_name = crate::device_hashes::HASH_TO_DISPLAY_NAME
                            .get(&hash_value)
                            .unwrap_or(hash_name);

                        let matches = search_text.is_empty()
                            || hash_name.to_lowercase().contains(&search_lower)
                            || display_name.to_lowercase().contains(&search_lower);

                        if matches {
                            match_count += 1;
                            let insert_text = format!("{}\")", hash_name);

                            ret.push(CompletionItem {
                                label: hash_name.to_string(),
                                kind: Some(CompletionItemKind::CONSTANT),
                                detail: Some(format!("{} → {}", display_name, hash_value)),
                                documentation: Some(Documentation::String(format!(
                                    "Device: {}\nHash: {}",
                                    display_name, hash_value
                                ))),
                                insert_text: Some(insert_text),
                                insert_text_format: Some(InsertTextFormat::PLAIN_TEXT),
                                ..Default::default()
                            });
                        }
                    }
                    ret.sort_by(|x, y| x.label.cmp(&y.label));
                    return Ok(Some(CompletionResponse::Array(ret)));
                }
            }
        }
    }

    if let Some(node) = node.find_parent("operation") {
        let raw = node.utf8_text(document.content.as_bytes()).unwrap();
        let lowered;
        let text: &str = if instructions::INSTRUCTIONS.contains_key(raw) {
            raw
        } else {
            lowered = raw.to_ascii_lowercase();
            lowered.as_str()
        };
        let cursor_pos = position.0.character as usize - node.start_position().column;
        let prefix = &text[..cursor_pos + 1];

        instruction_completions(prefix, &mut ret);
    } else if let Some(node) = node.find_parent("invalid_instruction") {
        let raw = node.utf8_text(document.content.as_bytes()).unwrap();
        let lowered;
        let text: &str = if instructions::INSTRUCTIONS.contains_key(raw) {
            raw
        } else {
            lowered = raw.to_ascii_lowercase();
            lowered.as_str()
        };
        let cursor_pos = position.0.character as usize - node.start_position().column;
        let prefix = &text[..cursor_pos + 1];

        instruction_completions(prefix, &mut ret);
    } else if let Some(line_node) = node.find_parent("line") {
        let text = line_node.utf8_text(document.content.as_bytes()).unwrap();
        let cursor_pos = position.0.character as usize - line_node.start_position().column;
        let global_prefix = &text[..cursor_pos as usize + 1];

        // Check if cursor is at start of line (all whitespace before cursor)
        // OR if we're in a whitespace gap after an instruction (for parameter completion)
        let at_line_start = global_prefix.chars().all(char::is_whitespace);

        if at_line_start {
            instruction_completions("", &mut ret);
        } else {
            // Try to find instruction node that contains cursor, fallback to querying line
            let instruction_node_opt = if let Some(inst) = node.find_parent("instruction") {
                Some(inst)
            } else {
                // No instruction parent found - try querying the line for any instruction
                // This handles cases where tree-sitter parsing is incomplete
                let result = line_node.query(
                    "(instruction)@x",
                    file_data.document_data.content.as_bytes(),
                );
                if let Some(ref inst) = result {
                    // Calculate cursor byte position using original_position
                    // Account for actual line endings in the document
                    let cursor_byte = if original_position.line == 0 {
                        original_position.character as usize
                    } else {
                        let mut byte_offset = 0;
                        let mut line_count = 0;
                        for (i, ch) in document.content.char_indices() {
                            if line_count >= original_position.line as usize {
                                byte_offset = i + original_position.character as usize;
                                break;
                            }
                            if ch == '\n' {
                                line_count += 1;
                            }
                        }
                        byte_offset
                    };

                    // CRITICAL: Verify the instruction actually contains the cursor!
                    // query() returns the FIRST match, which might be from a different line
                    if cursor_byte < inst.start_byte() || cursor_byte > inst.end_byte() {
                        None
                    } else {
                        result
                    }
                } else {
                    None
                }
            };

            let Some(instruction_node) = instruction_node_opt else {
                // No valid instruction found via tree-sitter, but we might still be able to help
                // Check if there's instruction text at the start of the line that we can use

                // IMPORTANT: Get the ACTUAL line from the document at the cursor position,
                // not from tree-sitter's line_node which may span multiple physical lines!
                let actual_line = document
                    .content
                    .lines()
                    .nth(original_position.line as usize)
                    .unwrap_or("");
                let cursor_col = original_position.character as usize;

                let first_word = actual_line.split_whitespace().next().unwrap_or("");

                if let Some(_signature) = instructions::INSTRUCTIONS.get(first_word) {
                    // We found an instruction at the start of the line!
                    // Try to determine which parameter position we're at based on whitespace/text
                    let text_up_to_cursor = &actual_line[..cursor_col.min(actual_line.len())];
                    let param_count = text_up_to_cursor
                        .split_whitespace()
                        .count()
                        .saturating_sub(1);

                    // Check if we should suggest HASH(" for this position
                    // Match the same logic as the main path
                    let suggest_hash = (first_word == "define" && param_count == 1)
                        || (first_word == "lbn" && (param_count == 1 || param_count == 2))
                        || (first_word == "lbns" && (param_count == 1 || param_count == 2))
                        || (first_word == "sbn" && (param_count == 0 || param_count == 1))
                        || (first_word == "lb" && param_count == 1)
                        || (first_word == "lbs" && param_count == 1)
                        || (first_word == "sb" && param_count == 0)
                        || (first_word == "sbs" && param_count == 0);

                    if suggest_hash {
                        ret.insert(
                            0,
                            CompletionItem {
                                label: "HASH(\"…)".to_string(),
                                kind: Some(CompletionItemKind::SNIPPET),
                                detail: Some("→ Device hash by name".to_string()),
                                documentation: Some(Documentation::String(
                                    "Type device name inside quotes to get its hash value"
                                        .to_string(),
                                )),
                                insert_text: Some("HASH(\"".to_string()),
                                filter_text: Some("HASH".to_string()),
                                insert_text_format: Some(InsertTextFormat::PLAIN_TEXT),
                                sort_text: Some("!0000".to_string()),
                                preselect: Some(true),
                                ..Default::default()
                            },
                        );
                    }

                    // Check if we're typing inside HASH(" to offer device name completions
                    let line_up_to_cursor = &actual_line[..cursor_col.min(actual_line.len())];
                    let last_hash_open = line_up_to_cursor
                        .rfind("HASH(\"")
                        .or_else(|| line_up_to_cursor.rfind("hash(\""));
                    let last_hash_close = line_up_to_cursor.rfind("\")");
                    let typing_in_hash = if let Some(open_pos) = last_hash_open {
                        if let Some(close_pos) = last_hash_close {
                            close_pos < open_pos
                                || (cursor_col > open_pos + 6 && cursor_col <= close_pos)
                        } else {
                            true
                        }
                    } else {
                        false
                    };

                    if typing_in_hash {
                        let search_start = line_up_to_cursor
                            .rfind("HASH(\"")
                            .or_else(|| line_up_to_cursor.rfind("hash(\""));
                        if let Some(start_pos) = search_start {
                            let search_text = &line_up_to_cursor[start_pos + 6..];
                            let search_lower = search_text.to_lowercase();
                            let already_complete = actual_line[start_pos..].contains("\")");

                            for hash_name in crate::device_hashes::DEVICE_NAME_TO_HASH.keys() {
                                let hash_value =
                                    crate::device_hashes::DEVICE_NAME_TO_HASH[hash_name];
                                let display_name = crate::device_hashes::HASH_TO_DISPLAY_NAME
                                    .get(&hash_value)
                                    .unwrap_or(hash_name);

                                let matches = search_text.is_empty()
                                    || hash_name.to_lowercase().contains(&search_lower)
                                    || display_name.to_lowercase().contains(&search_lower);

                                if matches {
                                    let insert_text = if already_complete {
                                        hash_name.to_string()
                                    } else {
                                        format!("{}\")", hash_name)
                                    };

                                    ret.push(CompletionItem {
                                        label: hash_name.to_string(),
                                        kind: Some(CompletionItemKind::CONSTANT),
                                        detail: Some(format!("{} → {}", display_name, hash_value)),
                                        insert_text: Some(insert_text),
                                        insert_text_format: Some(InsertTextFormat::PLAIN_TEXT),
                                        ..Default::default()
                                    });
                                }
                            }
                        }
                    } else {
                        // Not typing in HASH - provide regular parameter completions
                        if let Some(signature) = instructions::INSTRUCTIONS.get(first_word) {
                            if param_count < signature.0.len() {
                                let param_type = &signature.0[param_count];

                                // Extract the text after the last space (the current parameter being typed)
                                let prefix = if let Some(last_space) = text_up_to_cursor.rfind(' ')
                                {
                                    &text_up_to_cursor[last_space + 1..]
                                } else {
                                    text_up_to_cursor
                                };

                                // For branch/jump instructions, provide label completions on the jump target parameter
                                let is_branch = first_word.starts_with('b');
                                let is_jump = first_word.starts_with('j');

                                // Labels only show for:
                                // - Jump instructions (j, jal, jr): parameter 0 (the jump target)
                                // - Branch instructions (beq, bne, etc.): LAST parameter (the jump target)
                                let should_show_labels = if is_jump {
                                    param_count == 0 // j/jal/jr first parameter
                                } else if is_branch {
                                    param_count == signature.0.len() - 1 // Last parameter of branch
                                } else {
                                    false
                                };

                                if should_show_labels {
                                    // Add label completions
                                    for (label_name, _) in &file_data.type_data.labels {
                                        if prefix.is_empty() || label_name.starts_with(prefix) {
                                            ret.push(CompletionItem {
                                                label: label_name.clone(),
                                                kind: Some(CompletionItemKind::CONSTANT),
                                                detail: Some(" label".to_string()),
                                                ..Default::default()
                                            });
                                        }
                                    }

                                    // Add ra register for instructions that store return address
                                    if first_word.ends_with("al") {
                                        ret.push(CompletionItem {
                                            label: "ra".to_string(),
                                            kind: Some(CompletionItemKind::VARIABLE),
                                            detail: Some(" return address register".to_string()),
                                            ..Default::default()
                                        });
                                    }
                                }

                                // For non-label parameters, provide both builtin and static completions
                                if !should_show_labels {
                                    param_completions_builtin(prefix, param_type, &mut ret, None);
                                    param_completions_static(prefix, "", param_type, &mut ret);
                                }
                            }
                        }
                    }
                } else {
                    // Not continuing an instruction - offer instruction completions
                    instruction_completions("", &mut ret);
                }

                return Ok(Some(CompletionResponse::Array(ret)));
            };

            let Some(operation_node) = instruction_node.child_by_field_name("operation") else {
                return Ok(None);
            };

            let raw = operation_node
                .utf8_text(file_data.document_data.content.as_bytes())
                .unwrap();
            let lowered;
            let text: &str = if instructions::INSTRUCTIONS.contains_key(raw) {
                raw
            } else {
                lowered = raw.to_ascii_lowercase();
                lowered.as_str()
            };

            // Convert LSP position to byte offset in document
            let cursor_byte = document
                .content
                .lines()
                .take(position.0.line as usize)
                .map(|l| l.len() + 1)
                .sum::<usize>()
                + position.0.character as usize;

            let (current_param, operand_node) = get_current_parameter(
                instruction_node,
                cursor_byte,
                document.content.as_bytes(),
            );

            let operand_text = operand_node
                .map(|node| node.utf8_text(document.content.as_bytes()).unwrap())
                .unwrap_or("");

            let prefix = {
                if let Some(operand_node) = operand_node {
                    let cursor_pos = (position.0.character as usize)
                        .saturating_sub(operand_node.start_position().column);
                    let result = &operand_text[..(cursor_pos + 1).min(operand_text.len())];
                    result
                } else {
                    ""
                }
            };

            let Some(signature) = instructions::INSTRUCTIONS.get(text) else {
                return Ok(None);
            };

            let Some(param_type) = signature.0.get(current_param) else {
                return Ok(None);
            };

            // Special case: suggest HASH(" for instructions that commonly use device hashes
            let suggest_hash = (text == "define" && current_param == 1)
                || (text == "lbn" && (current_param == 1 || current_param == 2))
                || (text == "lbns" && (current_param == 1 || current_param == 2))
                || (text == "sbn" && (current_param == 0 || current_param == 1))
                || (text == "lb" && current_param == 1)
                || (text == "lbs" && current_param == 1)
                || (text == "sb" && current_param == 0)
                || (text == "sbs" && current_param == 0);

            // Only suggest HASH(" if we're typing in the right parameter position
            // Don't show it if the current parameter already starts with HASH (typed or autocompleted)
            // Use trim() to handle spaces, and check prefix (what's typed so far) not operand_text
            let prefix_trimmed = prefix.trim_start();

            if suggest_hash
                && !prefix_trimmed.starts_with("HASH")
                && !prefix_trimmed.starts_with("hash")
            {
                // Add HASH(" with multiple strategies to ensure visibility
                ret.insert(
                    0,
                    CompletionItem {
                        label: "HASH(\"…)".to_string(),
                        kind: Some(CompletionItemKind::SNIPPET),
                        detail: Some("→ Device hash by name".to_string()),
                        documentation: Some(Documentation::String(
                            "Type device name inside quotes to get its hash value".to_string(),
                        )),
                        insert_text: Some("HASH(\"".to_string()),
                        filter_text: Some("HASH".to_string()),
                        insert_text_format: Some(InsertTextFormat::PLAIN_TEXT),
                        sort_text: Some("!0000".to_string()),
                        preselect: Some(true),
                        ..Default::default()
                    },
                );
            }

            // Check if we're typing HASH(" even before it's fully parsed
            let actual_line = document
                .content
                .lines()
                .nth(position.0.line as usize)
                .unwrap_or("");
            let cursor_pos_in_actual_line = position.0.character as usize;
            let line_up_to_cursor =
                &actual_line[..cursor_pos_in_actual_line.min(actual_line.len())];

            // Check if cursor is immediately after HASH(" or within an unclosed HASH("
            let just_opened_hash =
                line_up_to_cursor.ends_with("HASH(\"") || line_up_to_cursor.ends_with("hash(\"");
            // Check if we're currently inside an unclosed HASH(" - find last occurrence
            let last_hash_open = line_up_to_cursor
                .rfind("HASH(\"")
                .or_else(|| line_up_to_cursor.rfind("hash(\""));
            let last_hash_close = line_up_to_cursor.rfind("\")");
            let typing_in_hash = if let Some(open_pos) = last_hash_open {
                // We're typing in HASH if there's an open quote and either no close quote,
                // or the close quote comes before the open quote
                if let Some(close_pos) = last_hash_close {
                    // If close comes before open, we're in an unclosed HASH
                    // If close comes after open, check cursor is BETWEEN open and close (not after)
                    close_pos < open_pos
                        || (cursor_pos_in_actual_line > open_pos + 6
                            && cursor_pos_in_actual_line <= close_pos)
                } else {
                    // No close found, we're definitely typing in HASH
                    true
                }
            } else {
                false
            };

            if just_opened_hash || typing_in_hash {
                // Extract the search text after HASH("
                let search_start = line_up_to_cursor
                    .rfind("HASH(\"")
                    .or_else(|| line_up_to_cursor.rfind("hash(\""));
                if let Some(start_pos) = search_start {
                    let search_text = &line_up_to_cursor[start_pos + 6..]; // Skip past HASH("
                    let start_entries = ret.len();
                    let search_lower = search_text.to_lowercase();

                    // Check if HASH call is already complete (has closing ") somewhere on the line)
                    let already_complete = if let Some(open_pos) = last_hash_open {
                        actual_line[open_pos..].contains("\")")
                    } else {
                        false
                    };

                    // Provide device name completions
                    for hash_name in crate::device_hashes::DEVICE_NAME_TO_HASH.keys() {
                        let hash_value = crate::device_hashes::DEVICE_NAME_TO_HASH[hash_name];
                        let display_name = crate::device_hashes::HASH_TO_DISPLAY_NAME
                            .get(&hash_value)
                            .unwrap_or(hash_name);

                        let matches = search_text.is_empty()
                            || hash_name.to_lowercase().contains(&search_lower)
                            || display_name.to_lowercase().contains(&search_lower);

                        if matches {
                            // If already complete, just insert device name
                            // Otherwise, insert device name + closing ")
                            let insert_text = if already_complete {
                                hash_name.to_string()
                            } else {
                                format!("{}\")", hash_name)
                            };

                            ret.push(CompletionItem {
                                label: hash_name.to_string(),
                                kind: Some(CompletionItemKind::CONSTANT),
                                detail: Some(format!("{} → {}", display_name, hash_value)),
                                documentation: Some(Documentation::String(format!(
                                    "Device: {}\nHash: {}",
                                    display_name, hash_value
                                ))),
                                insert_text: Some(insert_text),
                                insert_text_format: Some(InsertTextFormat::PLAIN_TEXT),
                                ..Default::default()
                            });
                        }
                    }
                    let length = ret.len();
                    ret[start_entries..length].sort_by(|x, y| x.label.cmp(&y.label));

                    // Return early - we're typing HASH(), don't show other completions
                    return Ok(Some(CompletionResponse::Array(ret)));
                }
            }

            // Check if we're inside a HASH() function's string argument
            if let Some(hash_func_node) = node.find_parent("hash_function") {
                if let Some(hash_string_node) = hash_func_node.child_by_field_name("argument") {
                    let string_text = hash_string_node
                        .utf8_text(file_data.document_data.content.as_bytes())
                        .unwrap();

                    // Extract content without quotes
                    let search_text = if string_text.starts_with('"')
                        && string_text.ends_with('"')
                        && string_text.len() >= 2
                    {
                        &string_text[1..string_text.len() - 1]
                    } else {
                        string_text
                    };

                    let start_entries = ret.len();
                    let search_lower = search_text.to_lowercase();

                    // Provide device name completions
                    for hash_name in crate::device_hashes::DEVICE_NAME_TO_HASH.keys() {
                        let hash_value = crate::device_hashes::DEVICE_NAME_TO_HASH[hash_name];
                        let display_name = crate::device_hashes::HASH_TO_DISPLAY_NAME
                            .get(&hash_value)
                            .unwrap_or(hash_name);

                        let matches = search_text.is_empty()
                            || hash_name.to_lowercase().contains(&search_lower)
                            || display_name.to_lowercase().contains(&search_lower);

                        if matches {
                            ret.push(CompletionItem {
                                label: hash_name.to_string(),
                                kind: Some(CompletionItemKind::CONSTANT),
                                detail: Some(format!("{} → {}", display_name, hash_value)),
                                documentation: Some(Documentation::String(format!(
                                    "Device: {}\nHash: {}",
                                    display_name, hash_value
                                ))),
                                insert_text: Some(hash_name.to_string()),
                                ..Default::default()
                            });
                        }
                    }
                    let length = ret.len();
                    ret[start_entries..length].sort_by(|x, y| x.label.cmp(&y.label));

                    // Return early - we're inside HASH(), don't show other completions
                    return Ok(Some(CompletionResponse::Array(ret)));
                }
            }

            // Check if we're inside a STR() function's string argument
            if let Some(_str_func_node) = node.find_parent("str_function") {
                // Inside STR() - no completions needed
                return Ok(Some(CompletionResponse::Array(ret)));
            }

            // Special case: batch instructions expect device hash at specific parameter positions
            let is_load_batch = matches!(text, "lb" | "lbn" | "lbs" | "lbns");
            let is_store_batch = matches!(text, "sb" | "sbn" | "sbs");
            let is_device_hash_param =
                (is_load_batch && current_param == 1) || (is_store_batch && current_param == 0);
            let is_name_hash_param =
                (text == "lbn" && current_param == 2) || (text == "sbn" && current_param == 1);

            if is_device_hash_param || is_name_hash_param {
                // Collect items that are actually used in the script for smart sorting
                let mut used_items = std::collections::HashSet::new();
                // Scan the document for used registers, devices, aliases, defines
                let content_lower = document.content.to_lowercase();
                for i in 0..=15 {
                    let reg = format!("r{}", i);
                    if content_lower.contains(&reg) {
                        used_items.insert(reg);
                    }
                }
                if content_lower.contains("ra") {
                    used_items.insert("ra".to_string());
                }
                if content_lower.contains("sp") {
                    used_items.insert("sp".to_string());
                }
                for i in 0..=5 {
                    let dev = format!("d{}", i);
                    if content_lower.contains(&dev) {
                        used_items.insert(dev);
                    }
                }
                if content_lower.contains("db") {
                    used_items.insert("db".to_string());
                }

                // Add all defined aliases and defines to used_items
                for alias_name in file_data.type_data.aliases.keys() {
                    used_items.insert(alias_name.clone());
                }
                for define_name in file_data.type_data.defines.keys() {
                    used_items.insert(define_name.clone());
                }

                // 1. Show built-in registers (if valid)
                param_completions_builtin(prefix, param_type, &mut ret, Some(&used_items));

                // 2. Show aliases (if valid)
                param_completions_dynamic(
                    prefix,
                    &file_data.type_data.aliases,
                    " alias",
                    param_type,
                    &mut ret,
                    Some(&used_items),
                );

                // 3. Show defines
                param_completions_dynamic(
                    prefix,
                    &file_data.type_data.defines,
                    " define",
                    param_type,
                    &mut ret,
                    Some(&used_items),
                );
                return Ok(Some(CompletionResponse::Array(ret)));
            }

            // Legacy preproc_string support (for backwards compatibility)
            if let Some(preproc_string_node) = instruction_node.query(
                "(preproc_string)@x",
                file_data.document_data.content.as_bytes(),
            ) {
                let string_text = preproc_string_node
                    .utf8_text(file_data.document_data.content.as_bytes())
                    .unwrap();

                let start_entries = ret.len();

                // Use comprehensive device registry with fuzzy search
                for hash_name in crate::device_hashes::DEVICE_NAME_TO_HASH.keys() {
                    // Fuzzy search: match if search text appears anywhere in device name or display name
                    let search_lower = string_text.to_lowercase();
                    let hash_value = crate::device_hashes::DEVICE_NAME_TO_HASH[hash_name];
                    let display_name = crate::device_hashes::HASH_TO_DISPLAY_NAME
                        .get(&hash_value)
                        .unwrap_or(hash_name);

                    let matches = hash_name.to_lowercase().contains(&search_lower)
                        || display_name.to_lowercase().contains(&search_lower);

                    if matches {
                        ret.push(CompletionItem {
                            label: hash_name.to_string(),
                            detail: Some(format!("→ {} ({})", display_name, hash_value)),
                            text_edit: Some(CompletionTextEdit::Edit(TextEdit {
                                range: {
                                    let mut edit_range = crate::types::Range::from(preproc_string_node.range());
                                    edit_range.0.end.character -= 1;
                                    edit_range.into()
                                },
                                new_text: hash_name.to_string(),
                            })),
                            ..Default::default()
                        });
                    }
                }
                let length = ret.len();
                ret[start_entries..length].sort_by(|x, y| x.label.cmp(&y.label));
            };

            // Context-aware completions based on parameter type
            if !text.starts_with("br") && text.starts_with("b") || text == "j" || text == "jal" {
                // Branch instructions - ONLY show labels
                param_completions_dynamic(
                    prefix,
                    &file_data.type_data.labels,
                    " label",
                    param_type,
                    &mut ret,
                    None,
                );
            } else {
                // Regular instructions - prioritize script-specific items first
                // Collect items that are actually used in the script for smart sorting
                let mut used_items = std::collections::HashSet::new();

                // Scan the document for used registers, devices, aliases, defines
                let content_lower = document.content.to_lowercase();
                for i in 0..=15 {
                    let reg = format!("r{}", i);
                    if content_lower.contains(&reg) {
                        used_items.insert(reg);
                    }
                }
                if content_lower.contains("ra") {
                    used_items.insert("ra".to_string());
                }
                if content_lower.contains("sp") {
                    used_items.insert("sp".to_string());
                }
                for i in 0..=5 {
                    let dev = format!("d{}", i);
                    if content_lower.contains(&dev) {
                        used_items.insert(dev);
                    }
                }
                if content_lower.contains("db") {
                    used_items.insert("db".to_string());
                }

                // Add all defined aliases and defines to used_items
                for alias_name in file_data.type_data.aliases.keys() {
                    used_items.insert(alias_name.clone());
                }
                for define_name in file_data.type_data.defines.keys() {
                    used_items.insert(define_name.clone());
                }

                // Check if this is a static-only parameter type
                let is_static_only = param_type.0.iter().any(|t| {
                    matches!(
                        t,
                        DataType::BatchMode
                            | DataType::LogicType
                            | DataType::SlotLogicType
                            | DataType::ReagentMode
                    )
                });

                if is_static_only {
                    // For static-only parameters, ONLY show the predefined constants
                    param_completions_static("", "", param_type, &mut ret);
                } else {
                    // For other parameters, show the full completion list
                    // 0. Show built-in registers and devices first (always available)
                    param_completions_builtin(prefix, param_type, &mut ret, Some(&used_items));

                    // 1. Show aliases (registers and devices) - MOST RELEVANT, script-specific
                    param_completions_dynamic(
                        prefix,
                        &file_data.type_data.aliases,
                        " alias",
                        param_type,
                        &mut ret,
                        Some(&used_items),
                    );

                    // 2. Show defines (often used for device hashes and constants) - script-specific
                    param_completions_dynamic(
                        prefix,
                        &file_data.type_data.defines,
                        " define",
                        param_type,
                        &mut ret,
                        Some(&used_items),
                    );

                    // 3. Show labels (less common for non-branch instructions) - script-specific
                    param_completions_dynamic(
                        prefix,
                        &file_data.type_data.labels,
                        " label",
                        param_type,
                        &mut ret,
                        Some(&used_items),
                    );

                    // 4. Show enum completions last - global numeric constants
                    if param_type.match_type(DataType::Number) {
                        enum_completions(prefix, param_type, &mut ret);
                    }

                    // Final sort: prioritize defines for numeric/value parameters
                    if param_type.match_type(DataType::Number) {
                        ret.sort_by(|a, b| {
                            let a_is_define = a
                                .label_details
                                .as_ref()
                                .and_then(|d| d.detail.as_ref())
                                .map(|s| s.contains("define"))
                                .unwrap_or(false);
                            let b_is_define = b
                                .label_details
                                .as_ref()
                                .and_then(|d| d.detail.as_ref())
                                .map(|s| s.contains("define"))
                                .unwrap_or(false);
                            let a_used = used_items.contains(&a.label);
                            let b_used = used_items.contains(&b.label);

                            // Priority: used defines > unused defines > used others > unused others
                            match (a_is_define, b_is_define, a_used, b_used) {
                                (true, false, _, _) => std::cmp::Ordering::Less, // defines first
                                (false, true, _, _) => std::cmp::Ordering::Greater,
                                (true, true, true, false) => std::cmp::Ordering::Less, // used defines before unused
                                (true, true, false, true) => std::cmp::Ordering::Greater,
                                (false, false, true, false) => std::cmp::Ordering::Less, // used before unused
                                (false, false, false, true) => std::cmp::Ordering::Greater,
                                _ => a.label.cmp(&b.label), // alphabetical within same category
                            }
                        });
                    }
                }
            }
        }
    }

    Ok(Some(CompletionResponse::Array(ret)))
}

// ============================================================================
// Helper Functions (extracted from nested functions)
// ============================================================================

/// Provides instruction completions based on prefix matching
fn instruction_completions(prefix: &str, completions: &mut Vec<CompletionItem>) {
    let start_entries = completions.len();
    for (instruction, _signature) in instructions::INSTRUCTIONS.entries() {
        if instruction.starts_with(prefix) {
            // Use labeled syntax but only show the operand suffix in the detail
            let full_syntax = crate::tooltip_documentation::get_instruction_syntax(instruction);
            let operand_suffix_core = full_syntax
                .strip_prefix(&format!("{} ", instruction))
                .unwrap_or(full_syntax.as_str())
                .to_string();
            let operand_suffix = if operand_suffix_core.is_empty() {
                String::new()
            } else {
                format!(" {}", operand_suffix_core)
            };
            completions.push(CompletionItem {
                label: instruction.to_string(),
                label_details: Some(CompletionItemLabelDetails {
                    // Show only operands in detail to prevent duplicated mnemonic
                    detail: Some(operand_suffix),
                    description: None,
                }),
                kind: Some(CompletionItemKind::FUNCTION),
                documentation: instructions::INSTRUCTION_DOCS
                    .get(instruction)
                    .map(|x| Documentation::String(x.to_string())),
                deprecated: Some(*instruction == "label"),
                ..Default::default()
            });
        }
    }
    let length = completions.len();
    completions[start_entries..length].sort_by(|x, y| x.label.cmp(&y.label));
}

/// Provides static parameter completions (LogicType, SlotLogicType, BatchMode)
fn param_completions_static(
    prefix: &str,
    detail: &str,
    param_type: &instructions::Union,
    completions: &mut Vec<CompletionItem>,
) {
    use instructions::DataType;

    let start_entries = completions.len();

    // Normalize the prefix for matching
    let prefix_trimmed = prefix.trim_start();
    let prefix_lower = prefix_trimmed.to_ascii_lowercase();

    for typ in param_type.0 {
        let map = match typ {
            DataType::LogicType => instructions::LOGIC_TYPE_DOCS,
            DataType::SlotLogicType => instructions::SLOT_TYPE_DOCS,
            DataType::BatchMode => instructions::BATCH_MODE_DOCS,
            _ => {
                continue;
            }
        };

        for entry in map.entries() {
            let name = *entry.0;
            let docs = *entry.1;
            // Case-insensitive prefix match
            if prefix_trimmed.is_empty() || name.to_ascii_lowercase().starts_with(&prefix_lower) {
                completions.push(CompletionItem {
                    label: name.to_string(),
                    label_details: Some(CompletionItemLabelDetails {
                        description: None,
                        detail: Some(detail.to_string()),
                    }),
                    kind: Some(CompletionItemKind::FIELD),
                    documentation: Some(Documentation::String(docs.to_string())),
                    ..Default::default()
                });
            }
        }
    }
    let length = completions.len();
    completions[start_entries..length].sort_by(|x, y| x.label.cmp(&y.label));
}

/// Provides built-in completions (registers and devices)
fn param_completions_builtin(
    prefix: &str,
    param_type: &instructions::Union,
    completions: &mut Vec<CompletionItem>,
    used_items: Option<&std::collections::HashSet<String>>,
) {
    use instructions::DataType;

    let prefix_trimmed = prefix.trim_start();
    let start_entries = completions.len();

    // Show registers if parameter accepts Register or Number
    if param_type.match_type(DataType::Register) || param_type.match_type(DataType::Number) {
        // Standard registers r0-r15
        for i in 0..=15 {
            let reg = format!("r{}", i);
            if prefix_trimmed.is_empty() || reg.starts_with(prefix_trimmed) {
                completions.push(CompletionItem {
                    label: reg.clone(),
                    label_details: Some(CompletionItemLabelDetails {
                        description: None,
                        detail: Some(" register".to_string()),
                    }),
                    kind: Some(CompletionItemKind::VARIABLE),
                    documentation: Some(Documentation::String(format!("Register {}", reg))),
                    ..Default::default()
                });
            }
        }

        // Special registers
        for special in ["ra", "sp"] {
            if prefix_trimmed.is_empty() || special.starts_with(prefix_trimmed) {
                completions.push(CompletionItem {
                    label: special.to_string(),
                    label_details: Some(CompletionItemLabelDetails {
                        description: None,
                        detail: Some(" register".to_string()),
                    }),
                    kind: Some(CompletionItemKind::VARIABLE),
                    documentation: Some(Documentation::String(if special == "ra" {
                        "Return address register".to_string()
                    } else {
                        "Stack pointer register".to_string()
                    })),
                    ..Default::default()
                });
            }
        }
    }

    // Show devices if parameter accepts Device
    if param_type.match_type(DataType::Device) {
        // Standard devices d0-d5
        for i in 0..=5 {
            let dev = format!("d{}", i);
            if prefix_trimmed.is_empty() || dev.starts_with(prefix_trimmed) {
                completions.push(CompletionItem {
                    label: dev.clone(),
                    label_details: Some(CompletionItemLabelDetails {
                        description: None,
                        detail: Some(" device".to_string()),
                    }),
                    kind: Some(CompletionItemKind::VARIABLE),
                    documentation: Some(Documentation::String(format!("Device pin {}", dev))),
                    ..Default::default()
                });
            }
        }

        // Special device db
        if prefix_trimmed.is_empty() || "db".starts_with(prefix_trimmed) {
            completions.push(CompletionItem {
                label: "db".to_string(),
                label_details: Some(CompletionItemLabelDetails {
                    description: None,
                    detail: Some(" device".to_string()),
                }),
                kind: Some(CompletionItemKind::VARIABLE),
                documentation: Some(Documentation::String(
                    "Device on IC housing".to_string(),
                )),
                ..Default::default()
            });
        }
    }

    let length = completions.len();
    // Apply usage-based sorting if provided
    if let Some(used) = used_items {
        completions[start_entries..length].sort_by(|a, b| {
            let a_used = used.contains(&a.label);
            let b_used = used.contains(&b.label);
            match (a_used, b_used) {
                (true, false) => std::cmp::Ordering::Less,
                (false, true) => std::cmp::Ordering::Greater,
                _ => a.label.cmp(&b.label),
            }
        });
    } else {
        completions[start_entries..length].sort_by(|x, y| x.label.cmp(&y.label));
    }
}

/// Provides dynamic completions (aliases, defines, labels)
fn param_completions_dynamic<T>(
    prefix: &str,
    map: &HashMap<String, DefinitionData<T>>,
    detail: &str,
    param_type: &instructions::Union,
    completions: &mut Vec<CompletionItem>,
    used_items: Option<&std::collections::HashSet<String>>,
) where
    DefinitionData<T>: HasType,
    T: std::fmt::Display,
{
    let start_entries = completions.len();
    for (identifier, value_data) in map.iter() {
        let value = &value_data.value;
        if identifier.starts_with(prefix) && param_type.match_type(value_data.get_type()) {
            completions.push(CompletionItem {
                label: identifier.to_string(),
                label_details: Some(CompletionItemLabelDetails {
                    description: Some(format!("{value}")),
                    detail: Some(detail.to_string()),
                }),
                kind: Some(CompletionItemKind::VARIABLE),
                ..Default::default()
            });
        }
    }
    let length = completions.len();
    // Apply usage-based sorting if provided
    if let Some(used) = used_items {
        completions[start_entries..length].sort_by(|a, b| {
            let a_used = used.contains(&a.label);
            let b_used = used.contains(&b.label);
            match (a_used, b_used) {
                (true, false) => std::cmp::Ordering::Less,
                (false, true) => std::cmp::Ordering::Greater,
                _ => a.label.cmp(&b.label),
            }
        });
    } else {
        completions[start_entries..length].sort_by(|x, y| x.label.cmp(&y.label));
    }
}

/// Provides enum completions for numeric parameters
fn enum_completions(
    prefix: &str,
    param_type: &instructions::Union,
    completions: &mut Vec<CompletionItem>,
) {
    use instructions::DataType;
    if !param_type.match_type(DataType::Number) {
        return;
    }
    let prefix_lower = prefix.trim_start().to_ascii_lowercase();
    let start_entries = completions.len();
    for (family, member, qualified, value, desc, deprecated) in instructions::all_enum_entries() {
        let q_lower = qualified.to_ascii_lowercase();
        let member_lower = member.to_ascii_lowercase();
        if prefix_lower.is_empty()
            || q_lower.starts_with(&prefix_lower)
            || (!prefix_lower.contains('.') && member_lower.starts_with(&prefix_lower))
        {
            // For _unnamed enum members, show just the member name without the prefix
            let display_label = if family == "_unnamed" {
                member.to_string()
            } else {
                qualified.to_string()
            };

            completions.push(CompletionItem {
                label: display_label.clone(),
                label_details: Some(CompletionItemLabelDetails {
                    detail: Some(format!("= {}", value)),
                    description: None,
                }),
                kind: Some(CompletionItemKind::ENUM),
                documentation: if desc.is_empty() {
                    None
                } else {
                    Some(Documentation::String(desc.to_string()))
                },
                deprecated: Some(deprecated),
                insert_text: Some(display_label),
                ..Default::default()
            });
        }
    }
    let length = completions.len();
    completions[start_entries..length].sort_by(|x, y| x.label.cmp(&y.label));
}
