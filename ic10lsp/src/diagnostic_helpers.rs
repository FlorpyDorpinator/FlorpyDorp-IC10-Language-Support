//! Diagnostic helper utilities
//!
//! This module provides utility functions for working with LSP diagnostics,
//! including deduplication and identity checking.

use tower_lsp::lsp_types::Diagnostic;

/// Create a unique identity tuple for a diagnostic
///
/// Used for deduplication - two diagnostics with the same identity are considered duplicates.
/// The identity includes the range (start/end line/character) and the message text.
pub fn diagnostic_identity(diag: &Diagnostic) -> (u32, u32, u32, u32, String) {
    (
        diag.range.start.line,
        diag.range.start.character,
        diag.range.end.line,
        diag.range.end.character,
        diag.message.clone(),
    )
}

/// Check if content should ignore size/line limits
///
/// Looks for the `#IgnoreLimits` directive in comments (case-insensitive).
pub fn should_ignore_limits(content: &str) -> bool {
    for line in content.lines() {
        let trimmed = line.trim();
        if let Some(comment_start) = trimmed.find('#') {
            let comment = trimmed[comment_start + 1..].trim().to_lowercase();
            if comment.starts_with("ignorelimits") {
                return true;
            }
        }
    }
    false
}
