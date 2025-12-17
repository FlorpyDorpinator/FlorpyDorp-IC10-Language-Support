//! Tree-sitter utility functions and extensions
//!
//! This module provides helper functions and traits for working with tree-sitter nodes,
//! including node navigation, querying, and parameter position detection.

use tree_sitter::{Node, Query, QueryCursor};


/// Extension trait for tree-sitter Node providing convenience methods
pub trait NodeEx: Sized {
    /// Find the nearest parent node of a given kind
    fn find_parent(&self, kind: &str) -> Option<Self>;
    
    /// Execute a query on this node and return the first match
    fn query<'a>(&'a self, query: &str, content: impl AsRef<[u8]>) -> Option<Node<'a>>;
}

impl<'a> NodeEx for Node<'a> {
    fn find_parent(&self, kind: &str) -> Option<Self> {
        let mut cur = self.clone();
        while cur.kind() != kind {
            cur = cur.parent()?;
        }
        Some(cur)
    }

    fn query(&self, query: &str, content: impl AsRef<[u8]>) -> Option<Node<'a>> {
        let mut cursor = QueryCursor::new();
        let query = match Query::new(tree_sitter_ic10::language(), query) {
            Ok(q) => q,
            Err(_e) => {
                // If the node type in the query doesn't exist in this parser build, fail gracefully
                return None;
            }
        };

        let mut captures = cursor.captures(&query, self.clone(), content.as_ref());
        captures
            .next()
            .map(|x| x.0.captures)
            .and_then(|x| x.get(0))
            .map(|x| x.node)
    }
}

/// Get the current parameter index and node at a given cursor position within an instruction
///
/// This function determines which parameter the cursor is currently positioned at within
/// an instruction node, accounting for whitespace and empty operands.
///
/// # Returns
/// A tuple of (parameter_index, optional_operand_node)
pub fn get_current_parameter<'a>(
    instruction_node: Node<'a>,
    cursor_byte: usize,
    content: &'a [u8],
) -> (usize, Option<Node<'a>>) {
    let mut ret: usize = 0;
    let mut cursor = instruction_node.walk();

    for (_idx, operand) in instruction_node
        .children_by_field_name("operand", &mut cursor)
        .enumerate()
    {
        // Skip empty operands (whitespace-only nodes that tree-sitter creates)
        let operand_text = operand.utf8_text(content).unwrap_or("");
        let is_empty = operand_text.trim().is_empty();

        // Only count non-empty operands
        if !is_empty {
            ret += 1;
        }

        // Break if this operand extends past cursor position
        // This means we're either inside it or haven't typed the next parameter yet
        if operand.end_byte() > cursor_byte {
            break;
        }
    }

    let operand = instruction_node
        .children_by_field_name("operand", &mut cursor)
        .nth(ret);

    cursor.reset(instruction_node);
    (ret, operand)
}
