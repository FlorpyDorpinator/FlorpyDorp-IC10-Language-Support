//! Document data structures and type tracking
//!
//! This module provides the core data structures for tracking documents, their parse trees,
//! and the types (defines, aliases, labels) declared within them.

use ic10lsp::instructions::DataType;
use std::collections::HashMap;
use std::fmt;
use std::time::Instant;
use tower_lsp::lsp_types::{Range as LspRange, Url};
use tree_sitter::{Parser, Tree};

use crate::types::Range;

/// Configuration for the language server
#[derive(Debug, Clone)]
pub struct Configuration {
    pub max_lines: usize,
    pub max_columns: usize,
    pub max_bytes: usize,
    pub warn_overline_comment: bool,
    pub warn_overcolumn_comment: bool,
    pub suppress_hash_diagnostics: bool,
    pub enable_control_flow_analysis: bool,
    pub suppress_register_warnings: bool,
}

impl Default for Configuration {
    fn default() -> Self {
        Configuration {
            max_lines: 128,
            max_columns: 52,
            max_bytes: 8192,
            warn_overline_comment: true,
            warn_overcolumn_comment: true,
            suppress_hash_diagnostics: false,
            enable_control_flow_analysis: false,
            suppress_register_warnings: false,
        }
    }
}

/// Represents a define value (can be numeric or a function call like HASH())
#[derive(Debug, Clone)]
pub enum DefineValue {
    Number(String),
    FunctionCall(String),
    Identifier(String),
}

impl DefineValue {
    /// Try to resolve this define to a numeric hash value
    pub fn resolved_numeric(&self) -> Option<i32> {
        match self {
            DefineValue::Number(s) => s.parse().ok(),
            DefineValue::FunctionCall(s) => {
                // Try to extract HASH("...") and resolve it
                if let Some(device_name) = crate::hash_utils::extract_hash_argument(s) {
                    crate::hash_utils::get_device_hash(&device_name)
                } else {
                    None
                }
            }
            DefineValue::Identifier(_) => None,
        }
    }
}

impl From<String> for DefineValue {
    fn from(s: String) -> Self {
        if s.starts_with("HASH(") || s.starts_with("hash(") {
            DefineValue::FunctionCall(s)
        } else if s.parse::<f64>().is_ok() {
            DefineValue::Number(s)
        } else {
            DefineValue::Identifier(s)
        }
    }
}

impl fmt::Display for DefineValue {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            DefineValue::Number(s) | DefineValue::FunctionCall(s) | DefineValue::Identifier(s) => {
                write!(f, "{}", s)
            }
        }
    }
}

/// Represents an alias value (register or device)
#[derive(Debug, Clone)]
pub enum AliasValue {
    Register(String),
    Device(String),
}

impl From<String> for AliasValue {
    fn from(s: String) -> Self {
        if s.starts_with('r') || s == "ra" || s == "sp" {
            AliasValue::Register(s)
        } else {
            AliasValue::Device(s)
        }
    }
}

impl fmt::Display for AliasValue {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            AliasValue::Register(s) | AliasValue::Device(s) => write!(f, "{}", s),
        }
    }
}

/// A definition with its location and value
#[derive(Debug, Clone)]
pub struct DefinitionData<T> {
    pub range: Range,
    pub value: T,
}

impl<T> DefinitionData<T> {
    pub fn new(range: Range, value: T) -> Self {
        DefinitionData { range, value }
    }
}

/// Trait for getting the data type of a definition value
pub trait HasType {
    fn get_type(&self) -> DataType;
}

impl HasType for DefinitionData<DefineValue> {
    fn get_type(&self) -> DataType {
        DataType::Number
    }
}

impl HasType for DefinitionData<AliasValue> {
    fn get_type(&self) -> DataType {
        match self.value {
            AliasValue::Register(_) => DataType::Register,
            AliasValue::Device(_) => DataType::Device,
        }
    }
}

impl HasType for DefinitionData<u8> {
    fn get_type(&self) -> DataType {
        DataType::Number // Labels are line numbers (numeric)
    }
}

/// Type tracking data for a document (defines, aliases, labels)
#[derive(Debug, Clone, Default)]
pub struct TypeData {
    pub defines: HashMap<String, DefinitionData<DefineValue>>,
    pub aliases: HashMap<String, DefinitionData<AliasValue>>,
    pub labels: HashMap<String, DefinitionData<u8>>,
}

impl TypeData {
    /// Get the range for a named definition (define, alias, or label)
    pub fn get_range(&mut self, name: &str) -> Option<LspRange> {
        self.defines
            .get(name)
            .map(|x| x.range)
            .or_else(|| self.aliases.get(name).map(|x| x.range))
            .or_else(|| self.labels.get(name).map(|x| x.range))
            .map(|r| r.into())
    }
}

/// Document data including content, parse tree, and parser
pub struct DocumentData {
    pub url: Url,
    pub tree: Option<Tree>,
    pub content: String,
    pub parser: Parser,
}

// Manual Debug impl because Parser doesn't implement Debug
impl std::fmt::Debug for DocumentData {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("DocumentData")
            .field("url", &self.url)
            .field("tree", &self.tree)
            .field("content", &format!("{}...", &self.content.chars().take(50).collect::<String>()))
            .field("parser", &"<Parser>")
            .finish()
    }
}

/// Complete file data including document and type information
#[derive(Debug)]
pub struct FileData {
    pub document_data: DocumentData,
    pub type_data: TypeData,
    pub last_diagnostic_run: Option<Instant>,
}
