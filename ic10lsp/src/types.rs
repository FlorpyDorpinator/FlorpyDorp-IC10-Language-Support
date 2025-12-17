//! Position and Range types for LSP integration
//!
//! This module provides wrapper types for LSP positions and ranges that integrate
//! with tree-sitter's position system.

use tower_lsp::lsp_types::{Position as LspPosition, Range as LspRange};

/// A position in a document (line, column)
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Position(pub LspPosition);

impl From<tree_sitter::Point> for Position {
    fn from(point: tree_sitter::Point) -> Self {
        Position(LspPosition::new(point.row as u32, point.column as u32))
    }
}

impl From<Position> for tree_sitter::Point {
    fn from(pos: Position) -> Self {
        tree_sitter::Point::new(pos.0.line as usize, pos.0.character as usize)
    }
}

impl From<LspPosition> for Position {
    fn from(pos: LspPosition) -> Self {
        Position(pos)
    }
}

impl From<Position> for LspPosition {
    fn from(pos: Position) -> Self {
        pos.0
    }
}

/// A range in a document (start and end positions)
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Range(pub LspRange);

impl From<tree_sitter::Range> for Range {
    fn from(range: tree_sitter::Range) -> Self {
        Range(LspRange::new(
            Position::from(range.start_point).into(),
            Position::from(range.end_point).into(),
        ))
    }
}

impl From<Range> for LspRange {
    fn from(range: Range) -> Self {
        range.0
    }
}

impl From<LspRange> for Range {
    fn from(range: LspRange) -> Self {
        Range(range)
    }
}

impl Range {
    /// Check if this range contains a given point
    pub fn contains(&self, point: tree_sitter::Point) -> bool {
        let start = tree_sitter::Point::new(
            self.0.start.line as usize,
            self.0.start.character as usize,
        );
        let end = tree_sitter::Point::new(
            self.0.end.line as usize,
            self.0.end.character as usize,
        );

        point >= start && point <= end
    }
}
