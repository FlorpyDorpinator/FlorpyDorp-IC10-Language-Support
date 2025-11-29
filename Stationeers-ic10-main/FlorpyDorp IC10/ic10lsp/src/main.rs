//! # IC10 Language Server (ic10lsp)
//!
//! A comprehensive Language Server Protocol (LSP) implementation for the IC10 MIPS-like
//! assembly language used in the game Stationeers. This server provides rich IDE features
//! including syntax highlighting, autocompletion, hover documentation, diagnostics, and more.
//!
//! ## Key Features
//! - Syntax validation and diagnostics (line/column/byte limits)
//! - Intelligent code completion for instructions, registers, devices, and logic types
//! - Hover documentation with instruction examples and register operation history
//! - Go-to-definition for labels, aliases, and defines
//! - HASH() function support with device name resolution
//! - Semantic token coloring for better syntax highlighting
//! - Inlay hints for device hashes and instruction signatures
//! - Code actions and quick fixes
//!
//! ## Architecture
//! This LSP uses the Tower LSP framework and Tree-sitter for parsing. The main components are:
//! - Document management (parsing and caching)
//! - Type tracking (aliases, defines, labels)
//! - Diagnostic generation (syntax errors, length warnings)
//! - Completion providers (instructions, parameters, enums)
//! - Hover providers (documentation, examples, history)

use ic10lsp::instructions::{self, DataType}; // access library module with instruction metadata
use std::fs;
use std::path::Path;
use std::time::{Duration, Instant};
use std::{
    borrow::Cow,
    collections::{HashMap, HashSet},
    fmt::Display,
    net::Ipv4Addr,
    sync::Arc,
};
use tower_lsp::lsp_types::SemanticTokenType;
use tower_lsp::lsp_types::{Position as LspPosition, Range as LspRange};
use tower_lsp::{LanguageServer, LspService, Server};
use tree_sitter::{Node, Parser, Query, QueryCursor, Tree};

// ============================================================================
// Module Imports
// ============================================================================
// These modules provide specialized functionality for the language server

/// Additional language features like register analysis and code actions
mod additional_features;

/// Command-line interface handling
mod cli;

/// Device hash mappings and resolution (HASH() function support)
mod device_hashes;

/// Utility functions for hash computation and parsing
mod hash_utils;

/// Enhanced tooltip/hover documentation with examples
mod tooltip_documentation;

// ============================================================================
// Constants
// ============================================================================

/// Diagnostic code for absolute jump instructions (should use relative jumps)
const LINT_ABSOLUTE_JUMP: &str = "absolute-jump";

/// Diagnostic code for relative branch to label (should use absolute branch)
const LINT_RELATIVE_BRANCH_TO_LABEL: &str = "relative-branch-to-label";

/// Semantic token types supported by the LSP for syntax highlighting.
/// These map to VSCode's semantic token system for rich colorization.
const SEMANTIC_SYMBOL_LEGEND: &[SemanticTokenType] = &[
    SemanticTokenType::VARIABLE,
    SemanticTokenType::FUNCTION,
    SemanticTokenType::TYPE,
    SemanticTokenType::KEYWORD,
    SemanticTokenType::ENUM,
    SemanticTokenType::STRING,
    SemanticTokenType::NUMBER,
    SemanticTokenType::COMMENT,
    SemanticTokenType::MACRO,
];

// ============================================================================
// Data Type Unions for Parameter Validation
// ============================================================================
// These constants define which data types are acceptable for various
// instruction parameters. Used for completion suggestions and type checking.

/// Parameters that only accept LogicType (e.g., Temperature, Pressure)
const LOGIC_ONLY: [DataType; 1] = [DataType::LogicType];

/// Parameters that only accept SlotLogicType (e.g., Occupant, OccupantHash)
const SLOT_ONLY: [DataType; 1] = [DataType::SlotLogicType];

/// Parameters that only accept BatchMode (e.g., Average, Sum, Maximum)
const BATCH_ONLY: [DataType; 1] = [DataType::BatchMode];

/// Parameters that only accept ReagentMode
const REAGENT_ONLY: [DataType; 1] = [DataType::ReagentMode];

/// Parameters that only accept Name
const NAME_ONLY: [DataType; 1] = [DataType::Name];
const LOGIC_SLOT: [DataType; 2] = [DataType::LogicType, DataType::SlotLogicType];
const LOGIC_BATCH: [DataType; 2] = [DataType::LogicType, DataType::BatchMode];
const LOGIC_REAGENT: [DataType; 2] = [DataType::LogicType, DataType::ReagentMode];
const SLOT_BATCH: [DataType; 2] = [DataType::SlotLogicType, DataType::BatchMode];
const SLOT_REAGENT: [DataType; 2] = [DataType::SlotLogicType, DataType::ReagentMode];
const BATCH_REAGENT: [DataType; 2] = [DataType::BatchMode, DataType::ReagentMode];
const LOGIC_SLOT_BATCH: [DataType; 3] = [
    DataType::LogicType,
    DataType::SlotLogicType,
    DataType::BatchMode,
];
const LOGIC_SLOT_REAGENT: [DataType; 3] = [
    DataType::LogicType,
    DataType::SlotLogicType,
    DataType::ReagentMode,
];
const LOGIC_BATCH_REAGENT: [DataType; 3] = [
    DataType::LogicType,
    DataType::BatchMode,
    DataType::ReagentMode,
];
const SLOT_BATCH_REAGENT: [DataType; 3] = [
    DataType::SlotLogicType,
    DataType::BatchMode,
    DataType::ReagentMode,
];
const LOGIC_SLOT_BATCH_REAGENT: [DataType; 4] = [
    DataType::LogicType,
    DataType::SlotLogicType,
    DataType::BatchMode,
    DataType::ReagentMode,
];

use phf::phf_set;
use crate::hash_utils::{
    compute_crc32, extract_hash_argument, extract_str_argument, get_device_hash,
    is_hash_function_call, is_str_function_call,
};
use serde_json::Value;
use tokio::{
    net::{TcpListener, TcpStream},
    sync::RwLock,
};
use tower_lsp::{async_trait, jsonrpc::Result, lsp_types::*, Client};
struct DocumentData {
    url: Url,
    content: String,
    tree: Option<Tree>,
    parser: Parser,
}

#[derive(Debug, Clone)]
struct DefinitionData<T> {
    range: Range,
    value: T,
}

impl<T> DefinitionData<T> {
    fn new(range: Range, value: T) -> Self {
        DefinitionData { range, value }
    }
}

#[derive(Debug, Clone)]
enum AliasValue {
    Register(String),
    Device(String),
}

impl Display for AliasValue {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let s = match self {
            AliasValue::Register(s) => s,
            AliasValue::Device(s) => s,
        };
        s.fmt(f)
    }
}

impl From<String> for AliasValue {
    fn from(value: String) -> Self {
        use AliasValue::*;
        if value.starts_with("d") {
            Device(value)
        } else {
            Register(value)
        }
    }
}

trait HasType {
    fn get_type(&self) -> instructions::DataType;
}

impl HasType for AliasValue {
    fn get_type(&self) -> instructions::DataType {
        match *self {
            AliasValue::Register(_) => instructions::DataType::Register,
            AliasValue::Device(_) => instructions::DataType::Device,
        }
    }
}

impl HasType for DefinitionData<f64> {
    fn get_type(&self) -> instructions::DataType {
        instructions::DataType::Number
    }
}

impl HasType for DefinitionData<u8> {
    fn get_type(&self) -> instructions::DataType {
        instructions::DataType::Number
    }
}

#[derive(Debug, Clone)]
struct DefineValue {
    original: String,
    resolved_numeric: Option<i32>,
}

impl DefineValue {
    fn resolve_numeric(text: &str) -> Option<i32> {
        if let Ok(value) = text.trim().parse::<i32>() {
            return Some(value);
        }
        if is_hash_function_call(text) {
            if let Some(arg) = extract_hash_argument(text) {
                return Some(compute_crc32(&arg));
            }
        }
        None
    }

    fn resolved_numeric(&self) -> Option<i32> {
        self.resolved_numeric
    }
}

impl From<String> for DefineValue {
    fn from(value: String) -> Self {
        let resolved_numeric = Self::resolve_numeric(&value);
        Self {
            original: value,
            resolved_numeric,
        }
    }
}

impl std::fmt::Display for DefineValue {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        self.original.fmt(f)
    }
}

impl HasType for DefineValue {
    fn get_type(&self) -> instructions::DataType {
        instructions::DataType::Number
    }
}

impl<T> HasType for DefinitionData<T>
where
    T: HasType,
{
    fn get_type(&self) -> instructions::DataType {
        self.value.get_type()
    }
}

#[derive(Default, Debug, Clone)]
struct TypeData {
    defines: HashMap<String, DefinitionData<DefineValue>>,
    aliases: HashMap<String, DefinitionData<AliasValue>>,
    labels: HashMap<String, DefinitionData<u8>>,
}

impl TypeData {
    fn get_range(&self, name: &str) -> Option<Range> {
        if let Some(definition_data) = self.defines.get(name) {
            return Some(definition_data.range.clone());
        }
                    if let Some(definition_data) = self.aliases.get(name) {
            return Some(definition_data.range.clone());
        }
        if let Some(definition_data) = self.labels.get(name) {
            return Some(definition_data.range.clone());
        }
        None
    }
}

struct FileData {
    document_data: DocumentData,
    type_data: TypeData,
    last_diagnostic_run: Option<Instant>,
}

#[derive(Clone, Debug)]
struct Configuration {
    max_lines: usize,
    max_columns: usize,
    max_bytes: usize,
    warn_overline_comment: bool,
    warn_overcolumn_comment: bool,
    suppress_hash_diagnostics: bool,
    enable_control_flow_analysis: bool,
    suppress_register_warnings: bool,
}

impl Default for Configuration {
    fn default() -> Self {
        Self {
            max_lines: 128,
            max_columns: 90,
            max_bytes: 4096,
            warn_overline_comment: true,
            warn_overcolumn_comment: true,
            suppress_hash_diagnostics: false,
            enable_control_flow_analysis: true,
            suppress_register_warnings: false,
        }
    }
}

struct Backend {
    client: Client,
    files: Arc<RwLock<HashMap<Url, FileData>>>,
    config: Arc<RwLock<Configuration>>,
    // Runtime flag to allow diagnostics suppression without restart
    diagnostics_enabled: Arc<RwLock<bool>>,
    // Track when we've warned about too many files
    warned_about_file_count: Arc<tokio::sync::Mutex<bool>>,
}

// Constants for performance tuning
const MAX_RECOMMENDED_FILES: usize = 50;
const DIAGNOSTIC_DEBOUNCE_MS: u64 = 150;

#[async_trait]
impl LanguageServer for Backend {
    async fn initialize(&self, params: InitializeParams) -> Result<InitializeResult> {
        // Debug: log what we receive
        self.client.log_message(MessageType::INFO, format!("Initialize called, has init_options: {}", params.initialization_options.is_some())).await;
        
        // Read initial configuration from initializationOptions if provided
        if let Some(init_options) = params.initialization_options {
            self.client.log_message(MessageType::INFO, format!("Init options: {}", serde_json::to_string_pretty(&init_options).unwrap_or_else(|_| "serialize failed".to_string()))).await;
            
            let mut config = self.config.write().await;
            
            if let Some(warnings) = init_options.get("warnings").and_then(Value::as_object) {
                config.warn_overline_comment = warnings
                    .get("overline_comment")
                    .and_then(Value::as_bool)
                    .unwrap_or(config.warn_overline_comment);
                config.warn_overcolumn_comment = warnings
                    .get("overcolumn_comment")
                    .and_then(Value::as_bool)
                    .unwrap_or(config.warn_overcolumn_comment);
            }
            
            config.max_lines = init_options
                .get("max_lines")
                .and_then(Value::as_u64)
                .map(|x| x as usize)
                .unwrap_or(config.max_lines);
            
            config.max_columns = init_options
                .get("max_columns")
                .and_then(Value::as_u64)
                .map(|x| x as usize)
                .unwrap_or(config.max_columns);
            
            config.max_bytes = init_options
                .get("max_bytes")
                .and_then(Value::as_u64)
                .map(|x| x as usize)
                .unwrap_or(config.max_bytes);
            
            config.suppress_hash_diagnostics = init_options
                .get("suppressHashDiagnostics")
                .and_then(Value::as_bool)
                .unwrap_or(config.suppress_hash_diagnostics);
            
            config.enable_control_flow_analysis = init_options
                .get("enableControlFlowAnalysis")
                .and_then(Value::as_bool)
                .unwrap_or(config.enable_control_flow_analysis);
            
            config.suppress_register_warnings = init_options
                .get("suppressRegisterWarnings")
                .and_then(Value::as_bool)
                .unwrap_or(config.suppress_register_warnings);
            
            self.client.log_message(MessageType::INFO, format!("Initial config - suppress_hash_diagnostics: {}", config.suppress_hash_diagnostics)).await;
        }
        
        let mut utf8_supported = false;
        if let Some(encodings) = params
            .capabilities
            .general
            .and_then(|x| x.position_encodings)
        {
            for encoding in encodings {
                if encoding == PositionEncodingKind::UTF8 {
                    utf8_supported = true;
                }
            }
            // Note: Modern LSP clients handle UTF-16 by default, which is sufficient for IC10.
            // The warning is suppressed to avoid confusion since the vscode-languageclient
            // handles encoding negotiation automatically.
        }
        // Log current counts of static maps/sets so we can verify the running binary contains
        // the latest logic types. This message appears once on server init in the Output panel.
        self.client
            .log_message(
                MessageType::INFO,
                format!(
                    "IC10LSP init: logicTypes={} slotLogicTypes={} batchModes={}",
                    instructions::LOGIC_TYPE_DOCS.len(),
                    instructions::SLOT_TYPE_DOCS.len(),
                    instructions::BATCH_MODE_DOCS.len()
                ),
            )
            .await;
        Ok(InitializeResult {
            capabilities: ServerCapabilities {
                text_document_sync: Some(TextDocumentSyncCapability::Kind(
                    TextDocumentSyncKind::FULL,
                )),
                execute_command_provider: Some(ExecuteCommandOptions {
                    commands: vec![
                        "setDiagnostics".to_string(),
                        "ic10.setHashDiagnostics".to_string(),
                    ],
                    work_done_progress_options: WorkDoneProgressOptions {
                        work_done_progress: None,
                    },
                }),
                inlay_hint_provider: Some(OneOf::Left(true)),
                hover_provider: Some(HoverProviderCapability::Simple(true)),
                definition_provider: Some(OneOf::Left(true)),
                code_action_provider: Some(CodeActionProviderCapability::Simple(true)),
                signature_help_provider: Some(SignatureHelpOptions {
                    trigger_characters: Some(vec![" ".to_string()]),
                    retrigger_characters: None,
                    work_done_progress_options: WorkDoneProgressOptions::default(),
                }),
                position_encoding: utf8_supported.then_some(PositionEncodingKind::UTF8),
                document_symbol_provider: Some(OneOf::Left(true)),
                completion_provider: Some(CompletionOptions {
                    resolve_provider: Some(false),
                    trigger_characters: Some(vec![" ".to_string(), "\"".to_string()]),
                    completion_item: Some(CompletionOptionsCompletionItem {
                        label_details_support: Some(true),
                    }),
                    ..Default::default()
                }),
                semantic_tokens_provider: Some(
                    SemanticTokensServerCapabilities::SemanticTokensOptions(
                        SemanticTokensOptions {
                            range: Some(false),
                            full: Some(SemanticTokensFullOptions::Bool(true)),
                            legend: {
                                SemanticTokensLegend {
                                    token_types: SEMANTIC_SYMBOL_LEGEND.into(),
                                    token_modifiers: vec![],
                                }
                            },
                            ..Default::default()
                        },
                    ),
                ),
                workspace: Some(WorkspaceServerCapabilities {
                    workspace_folders: None,
                    file_operations: None,
                }),
                ..Default::default()
            },
            server_info: Some(ServerInfo {
                name: "ic10lsp".to_string(),
                version: Some("1.0.0".to_string()),
            }),
        })
    }

    async fn initialized(&self, _params: InitializedParams) {}

    async fn execute_command(&self, params: ExecuteCommandParams) -> Result<Option<Value>> {
        match params.command.as_str() {
            "version" => {
                self.client
                    .show_message(
                        MessageType::INFO,
                        concat!("IC10LSP Version: ", env!("CARGO_PKG_VERSION")),
                    )
                    .await;
            }
            "setDiagnostics" => {
                if let Some(enabled) = params.arguments.get(0).and_then(Value::as_bool) {
                    {
                        let mut flag = self.diagnostics_enabled.write().await;
                        *flag = enabled;
                    }
                    // re-run or clear diagnostics for all open documents
                    let uris = {
                        let files = self.files.read().await;
                        files.keys().cloned().collect::<Vec<_>>()
                    };
                    for uri in uris {
                        if enabled {
                            self.run_diagnostics(&uri).await;
                        } else {
                            self.client
                                .publish_diagnostics(uri.clone(), vec![], None)
                                .await;
                        }
                    }
                }
            }
            "ic10.setHashDiagnostics" => {
                if let Some(suppress) = params.arguments.get(0).and_then(Value::as_bool) {
                    self.config.write().await.suppress_hash_diagnostics = suppress;

                    // Re-run diagnostics for all open files
                    let uris = {
                        let files = self.files.read().await;
                        files.keys().cloned().collect::<Vec<_>>()
                    };
                    for uri in uris {
                        self.run_diagnostics(&uri).await;
                    }
                }
            }
            "ic10.suppressAllRegisterDiagnostics" => {
                // Get the document URI from the arguments
                if let Some(uri_value) = params.arguments.get(0) {
                    if let Some(uri_str) = uri_value.as_str() {
                        if let Ok(uri) = Url::parse(uri_str) {
                            let files = self.files.read().await;
                            if let Some(file_data) = files.get(&uri) {
                                let content = &file_data.document_data.content;
                                
                                // Re-run register analysis to get current diagnostics
                                let mut register_analyzer = additional_features::RegisterAnalyzer::new();
                                if let Some(ref tree) = file_data.document_data.tree {
                                    register_analyzer.analyze_register_usage(
                                        tree,
                                        &content,
                                        &file_data.type_data.aliases,
                                    );
                                    
                                    // Collect all register diagnostic errors
                                    let mut registers_with_errors = std::collections::HashSet::new();
                                    let diagnostics = register_analyzer.generate_diagnostics();
                                    
                                    for diag in diagnostics {
                                        if let Some(data) = &diag.data {
                                            if let Some(register_name) = data.as_str() {
                                                registers_with_errors.insert(register_name.to_string());
                                            }
                                        }
                                    }
                                    
                                    if !registers_with_errors.is_empty() {
                                        // Find existing @ignore directive or create new one
                                        let mut ignore_line_index = None;
                                        let mut existing_registers = Vec::new();

                                        for (idx, line) in content.lines().enumerate() {
                                            if line.contains("# ignore") {
                                                ignore_line_index = Some(idx);
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

                                        // Merge with new registers
                                        for reg in registers_with_errors {
                                            if !existing_registers.contains(&reg) {
                                                existing_registers.push(reg);
                                            }
                                        }
                                        
                                        existing_registers.sort();
                                        let new_ignore_line = format!("# ignore {}", existing_registers.join(", "));

                                        let edit = if let Some(line_idx) = ignore_line_index {
                                            tower_lsp::lsp_types::TextEdit {
                                                range: tower_lsp::lsp_types::Range::new(
                                                    tower_lsp::lsp_types::Position::new(line_idx as u32, 0),
                                                    tower_lsp::lsp_types::Position::new(line_idx as u32, content.lines().nth(line_idx).unwrap().len() as u32),
                                                ),
                                                new_text: new_ignore_line,
                                            }
                                        } else {
                                            tower_lsp::lsp_types::TextEdit {
                                                range: tower_lsp::lsp_types::Range::new(
                                                    tower_lsp::lsp_types::Position::new(0, 0),
                                                    tower_lsp::lsp_types::Position::new(0, 0),
                                                ),
                                                new_text: format!("{}\n", new_ignore_line),
                                            }
                                        };

                                        // Apply the workspace edit
                                        let workspace_edit = tower_lsp::lsp_types::WorkspaceEdit {
                                            changes: Some(std::collections::HashMap::from([(uri.clone(), vec![edit])])),
                                            ..Default::default()
                                        };
                                        
                                        let _ = self.client.apply_edit(workspace_edit).await;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            _ => {}
        }
        Ok(None)
    }

    async fn shutdown(&self) -> Result<()> {
        Ok(())
    }

    async fn did_open(&self, params: DidOpenTextDocumentParams) {
        self.update_content(params.text_document.uri.clone(), params.text_document.text)
            .await;
        
        // Check if we have too many files open and warn once
        {
            let files = self.files.read().await;
            let file_count = files.len();
            if file_count > MAX_RECOMMENDED_FILES {
                let mut warned = self.warned_about_file_count.lock().await;
                if !*warned {
                    *warned = true;
                    self.client.show_message(
                        MessageType::WARNING,
                        format!(
                            "IC10 LSP: {} files open (recommended max: {}). Consider closing unused files or using workspace folders to improve performance. Use Ctrl+Alt+D to disable diagnostics if needed.",
                            file_count, MAX_RECOMMENDED_FILES
                        )
                    ).await;
                }
            }
        }
        
        // Run diagnostics for newly opened files
        self.run_diagnostics(&params.text_document.uri).await;
    }

    async fn did_change(&self, params: DidChangeTextDocumentParams) {
        for change in params.content_changes {
            // Should only ever be one, because we are getting full updates
            self.update_content(params.text_document.uri.clone(), change.text)
                .await;
        }
        
        // Debounce diagnostics on change - only run if enough time has passed
        let should_run = {
            let files = self.files.read().await;
            if let Some(file_data) = files.get(&params.text_document.uri) {
                if let Some(last_run) = file_data.last_diagnostic_run {
                    last_run.elapsed() > Duration::from_millis(DIAGNOSTIC_DEBOUNCE_MS)
                } else {
                    true
                }
            } else {
                true
            }
        };
        
        if should_run {
            self.run_diagnostics(&params.text_document.uri).await;
        }
    }

    async fn did_change_configuration(&self, params: DidChangeConfigurationParams) {
        {
            let mut config = self.config.write().await;
            let value = params.settings;

            // Debug logging
            self.client.log_message(MessageType::INFO, "=== Configuration received ===").await;
            self.client.log_message(MessageType::INFO, format!("Config JSON: {}", serde_json::to_string_pretty(&value).unwrap_or_else(|_| "Failed to serialize".to_string()))).await;

            if let Some(warnings) = value.get("warnings").and_then(Value::as_object) {
                config.warn_overline_comment = warnings
                    .get("overline_comment")
                    .and_then(Value::as_bool)
                    .unwrap_or(config.warn_overline_comment);

                config.warn_overcolumn_comment = warnings
                    .get("overcolumn_comment")
                    .and_then(Value::as_bool)
                    .unwrap_or(config.warn_overcolumn_comment);
            }

            config.max_lines = value
                .get("max_lines")
                .and_then(Value::as_u64)
                .map(|x| x as usize)
                .unwrap_or(config.max_lines);

            config.max_columns = value
                .get("max_columns")
                .and_then(Value::as_u64)
                .map(|x| x as usize)
                .unwrap_or(config.max_columns);

            config.max_bytes = value
                .get("max_bytes")
                .and_then(Value::as_u64)
                .map(|x| x as usize)
                .unwrap_or(config.max_bytes);

            config.suppress_hash_diagnostics = value
                .get("suppressHashDiagnostics")
                .and_then(Value::as_bool)
                .unwrap_or(config.suppress_hash_diagnostics);

            config.enable_control_flow_analysis = value
                .get("enableControlFlowAnalysis")
                .and_then(Value::as_bool)
                .unwrap_or(config.enable_control_flow_analysis);

            config.suppress_register_warnings = value
                .get("suppressRegisterWarnings")
                .and_then(Value::as_bool)
                .unwrap_or(config.suppress_register_warnings);

            self.client.log_message(MessageType::INFO, format!("suppress_hash_diagnostics set to: {}", config.suppress_hash_diagnostics)).await;
        }

        // Only re-run diagnostics on a limited set of files to avoid overwhelming the server
        // In large workspaces, we'll only refresh diagnostics for recently-edited files
        let uris = {
            let files = self.files.read().await;
            let mut file_list: Vec<(Url, Option<Instant>)> = files
                .iter()
                .map(|(url, data)| (url.clone(), data.last_diagnostic_run))
                .collect();
            
            // If we have many files, only refresh the most recently diagnosed ones
            if file_list.len() > MAX_RECOMMENDED_FILES {
                file_list.sort_by_key(|(_, last_run)| std::cmp::Reverse(*last_run));
                file_list.truncate(MAX_RECOMMENDED_FILES);
                
                self.client.log_message(
                    MessageType::INFO, 
                    format!("Config changed: refreshing diagnostics for {} of {} files", 
                            MAX_RECOMMENDED_FILES, files.len())
                ).await;
            }
            
            file_list.into_iter().map(|(url, _)| url).collect::<Vec<_>>()
        };
        
        for uri in uris {
            self.run_diagnostics(&uri).await;
        }
    }

    async fn inlay_hint(&self, params: InlayHintParams) -> Result<Option<Vec<InlayHint>>> {
        let mut ret = Vec::new();

        let files = self.files.read().await;
        let uri = params.text_document.uri;
        let Some(file_data) = files.get(&uri) else {
            return Err(tower_lsp::jsonrpc::Error::invalid_request());
        };

        let document = &file_data.document_data;

        let Some(ref tree) = document.tree else {
            return Err(tower_lsp::jsonrpc::Error::internal_error());
        };

        let mut cursor = QueryCursor::new();
        let query = Query::new(tree_sitter_ic10::language(), "(number)@x").unwrap();

        // Process all number nodes (direct numeric hashes)
        for (capture, _) in cursor.captures(&query, tree.root_node(), document.content.as_bytes()) {
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
                        line_node.query("(newline)@x", document.content.as_bytes())
                    {
                        Position::from(newline.range().start_point)
                    } else if let Some(instruction) =
                        line_node.query("(instruction)@x", document.content.as_bytes())
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

        // Also show inlays for HASH("...") functions (hash_function in the grammar)
        let mut cursor_hash = QueryCursor::new();
        let hash_query = Query::new(tree_sitter_ic10::language(), "(hash_function)@call").unwrap();

        for (cap, _) in cursor_hash.captures(&hash_query, tree.root_node(), document.content.as_bytes()) {
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
                        line_node.query("(newline)@x", document.content.as_bytes())
                    {
                        Position::from(newline.range().start_point)
                    } else if let Some(instruction) =
                        line_node.query("(instruction)@x", document.content.as_bytes())
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
        
        // Also show inlays for STR("...") functions (str_function in the grammar)
        let mut cursor_str = QueryCursor::new();
        let str_query = Query::new(tree_sitter_ic10::language(), "(str_function)@call").unwrap();

        for (cap, _) in cursor_str.captures(&str_query, tree.root_node(), document.content.as_bytes()) {
            let call_node = cap.captures[0].node;
            let call_text = call_node.utf8_text(document.content.as_bytes()).unwrap();
            if let Some(string_content) = crate::hash_utils::extract_str_argument(call_text) {
                // Compute the hash value for the string
                let hash_val = crate::hash_utils::compute_crc32(&string_content);
                
                let Some(line_node) = call_node.find_parent("line") else {
                    continue;
                };

                let endpos = if let Some(newline) =
                    line_node.query("(newline)@x", document.content.as_bytes())
                {
                    Position::from(newline.range().start_point)
                } else if let Some(instruction) =
                    line_node.query("(instruction)@x", document.content.as_bytes())
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
        // Persistent parameter hint: when only opcode is typed (no operands yet),
        // show the remaining signature as faint inline text after the opcode.
        // This helps the user until they type the first operand.
        let mut cursor2 = QueryCursor::new();
        let instr_query = Query::new(tree_sitter_ic10::language(), "(instruction)@i").unwrap();
        for (cap, _) in
            cursor2.captures(&instr_query, tree.root_node(), document.content.as_bytes())
        {
            let instr_node = cap.captures[0].node;
            // Get operation node and count operands
            let Some(op_node) = instr_node.child_by_field_name("operation") else {
                continue;
            };
            let mut w = instr_node.walk();
            let operand_count = instr_node.children_by_field_name("operand", &mut w).count();
            if operand_count != 0 {
                continue;
            }

            // Also skip if there's any text after the opcode (even whitespace indicates typing)
            // This prevents the hint from being "accepted" when user presses space/tab
            let opcode_raw = match op_node.utf8_text(document.content.as_bytes()) {
                Ok(t) => t,
                Err(_) => continue,
            };
            
            // Check if there's anything after the opcode on the same line
            let line_text = match instr_node.find_parent("line") {
                Some(line_node) => line_node.utf8_text(document.content.as_bytes()).unwrap_or(""),
                None => continue,
            };
            
            // Get the position where opcode ends
            let opcode_end_byte = op_node.range().end_byte - instr_node.range().start_byte;
            if opcode_end_byte < line_text.len() {
                let after_opcode = &line_text[opcode_end_byte..];
                // If there's ANY character after opcode (including space), skip hint
                // This prevents cursor jumping when space is pressed
                if !after_opcode.is_empty() && !after_opcode.starts_with('\n') && !after_opcode.starts_with('\r') {
                    continue;
                }
            }

            // Build syntax and take the suffix (parameters part) after opcode
            let lowered;
            let opcode: &str = if instructions::INSTRUCTIONS.contains_key(opcode_raw) {
                opcode_raw
            } else {
                lowered = opcode_raw.to_ascii_lowercase();
                lowered.as_str()
            };
            let syntax = crate::tooltip_documentation::get_instruction_syntax(opcode);
            // If there are no parameters (syntax has no space), skip
            if let Some(space_idx) = syntax.find(' ') {
                let params_suffix = syntax[space_idx + 1..].to_string();
                if !params_suffix.is_empty() {
                    let pos = Position::from(op_node.range().end_point);
                    ret.push(InlayHint {
                        position: pos.into(),
                        label: InlayHintLabel::String(params_suffix),
                        kind: Some(InlayHintKind::PARAMETER),
                        text_edits: None,
                        tooltip: None,
                        // add a space between opcode and hint for readability
                        padding_left: Some(true),
                        padding_right: None,
                        data: None,
                    });
                }
            }
        }

        Ok(Some(ret))
    }

    async fn semantic_tokens_full(
        &self,
        params: SemanticTokensParams,
    ) -> Result<Option<SemanticTokensResult>> {
        let mut ret = Vec::new();
        let files = self.files.read().await;
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

            let delta_line = start.row as u32 - previous_line;
            let delta_start = if delta_line == 0 {
                start.column as u32 - previous_col
            } else {
                start.column as u32
            };

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
                    // Determine if this identifier is a branch/jump label reference even if forward‑declared.
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
                                                // Two groups: (a,b,label) form and (a,label) form; plus single‑operand j/jal.
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
                    } else {
                        SemanticTokenType::VARIABLE
                    }
                } else {
                    continue;
                }
            };

            // Calculate token length and clamp to line bounds to prevent "end character > line length" errors
            let calculated_length = node.range().end_point.column as u32 - start.column as u32;
            
            // Get the actual line length from the document to ensure we don't exceed it
            let line_length = document.content.lines().nth(start.row).map(|line| line.len() as u32).unwrap_or(0);
            let max_allowed_length = if line_length > start.column as u32 {
                line_length - start.column as u32
            } else {
                0
            };
            
            // Use the minimum of calculated length and max allowed length
            let safe_length = calculated_length.min(max_allowed_length);
            
            // Skip tokens with zero length (defensive)
            if safe_length == 0 {
                continue;
            }

            ret.push(SemanticToken {
                delta_line,
                delta_start,
                length: safe_length,
                token_type: SEMANTIC_SYMBOL_LEGEND
                    .iter()
                    .position(|x| *x == tokentype)
                    .unwrap() as u32,
                token_modifiers_bitset: 0,
            });

            previous_line = start.row as u32;
            previous_col = start.column as u32;
        }
        Ok(Some(SemanticTokensResult::Tokens(SemanticTokens {
            result_id: None,
            data: ret,
        })))
    }

    async fn document_symbol(
        &self,
        params: DocumentSymbolParams,
    ) -> Result<Option<DocumentSymbolResponse>> {
        let mut ret = Vec::new();
        let files = self.files.read().await;
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

    async fn completion(&self, params: CompletionParams) -> Result<Option<CompletionResponse>> {
        fn instruction_completions(prefix: &str, completions: &mut Vec<CompletionItem>) {
            let start_entries = completions.len();
            for (instruction, signature) in instructions::INSTRUCTIONS.entries() {
                if instruction.starts_with(prefix) {
                    // Use labeled syntax but only show the operand suffix in the detail
                    // to avoid duplicating the mnemonic in the completion list ("subsub ...").
                    let full_syntax =
                        crate::tooltip_documentation::get_instruction_syntax(instruction);
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

        fn param_completions_static(
            prefix: &str,
            detail: &str,
            param_type: &instructions::Union,
            completions: &mut Vec<CompletionItem>,
        ) {
            use instructions::DataType;

            let start_entries = completions.len();

            // Normalize the prefix for matching. We trim leading whitespace (so an operand that
            // begins with spaces still yields logic type completions) and use a case-insensitive
            // comparison so users can type in lowercase and still see PascalCase logic types.
            // If the trimmed prefix is empty, we show the full set of static completions for the
            // given parameter type.
            let prefix_trimmed = prefix.trim_start();
            let prefix_lower = prefix_trimmed.to_ascii_lowercase();

            for typ in param_type.0 {
                let map = match typ {
                    DataType::LogicType => instructions::LOGIC_TYPE_DOCS,
                    DataType::SlotLogicType => instructions::SLOT_TYPE_DOCS,
                    DataType::BatchMode => instructions::BATCH_MODE_DOCS,
                    _ => continue,
                };

                for entry in map.entries() {
                    let name = *entry.0;
                    let docs = *entry.1;
                    // Case-insensitive prefix match; also allow showing everything when prefix empty
                    if prefix_trimmed.is_empty()
                        || name.to_ascii_lowercase().starts_with(&prefix_lower)
                    {
                        completions.push(CompletionItem {
                            label: name.to_string(),
                            label_details: Some(CompletionItemLabelDetails {
                                description: None,
                                detail: Some(detail.to_string()),
                            }),
                            // Use FIELD so the completion UI shows the boxed-with-lines icon
                            // similar to other token-like items (matches the "Setting" visual style).
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

        fn sort_completions_by_usage(
            completions: &mut Vec<CompletionItem>,
            start_idx: usize,
            used_items: &std::collections::HashSet<String>,
        ) {
            // Sort so that used items come first (alphabetically), then unused items (alphabetically)
            let end_idx = completions.len();
            completions[start_idx..end_idx].sort_by(|a, b| {
                let a_used = used_items.contains(&a.label);
                let b_used = used_items.contains(&b.label);
                
                match (a_used, b_used) {
                    (true, false) => std::cmp::Ordering::Less,   // a used, b not used -> a first
                    (false, true) => std::cmp::Ordering::Greater, // b used, a not used -> b first
                    _ => a.label.cmp(&b.label),                   // both used or both unused -> alphabetical
                }
            });
        }

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
                            documentation: Some(Documentation::String(
                                if special == "ra" {
                                    "Return address register".to_string()
                                } else {
                                    "Stack pointer register".to_string()
                                }
                            )),
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
                        documentation: Some(Documentation::String("Device on IC housing".to_string())),
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
            for (_family, member, qualified, value, desc, deprecated) in
                instructions::all_enum_entries()
            {
                let q_lower = qualified.to_ascii_lowercase();
                let member_lower = member.to_ascii_lowercase();
                if prefix_lower.is_empty()
                    || q_lower.starts_with(&prefix_lower)
                    || (!prefix_lower.contains('.') && member_lower.starts_with(&prefix_lower))
                {
                    completions.push(CompletionItem {
                        label: qualified.to_string(),
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
                        ..Default::default()
                    });
                }
            }
            let length = completions.len();
            completions[start_entries..length].sort_by(|x, y| x.label.cmp(&y.label));
        }

        let mut ret = Vec::new();

        let uri = params.text_document_position.text_document.uri;
        let position = {
            let pos = params.text_document_position.position;
            Position::from(tower_lsp::lsp_types::Position::new(
                pos.line,
                pos.character.saturating_sub(1),
            ))
        };

        let files = self.files.read().await;
        let Some(file_data) = files.get(&uri) else {
            return Err(tower_lsp::jsonrpc::Error::invalid_request());
        };

        let document = &file_data.document_data;

        let Some(ref tree) = document.tree else {
            return Err(tower_lsp::jsonrpc::Error::internal_error());
        };

        let Some(node) = self.node_at_position(position, tree) else {
            return Ok(None);
        };

        // Global HASH(" detection - trigger device completions anywhere HASH(" is typed
        // This works in defines, instructions, anywhere a device hash might be used
        if let Some(line_node) = node.find_parent("line") {
            let line_text = line_node.utf8_text(document.content.as_bytes()).unwrap();
            
            // Get cursor position within the line by using byte offsets
            let line_start_byte = line_node.start_byte();
            let cursor_byte = document.content.len().min(
                document.content
                    .lines()
                    .take(position.0.line as usize)
                    .map(|l| l.len() + 1) // +1 for newline
                    .sum::<usize>()
                    + position.0.character as usize
            );
            
            let cursor_pos_in_line = if cursor_byte >= line_start_byte {
                cursor_byte - line_start_byte
            } else {
                0
            };
            
            let line_up_to_cursor = &line_text[..cursor_pos_in_line.min(line_text.len())];
            
            // Check if we're typing inside HASH("
            let last_hash_open = line_up_to_cursor.rfind("HASH(\"").or_else(|| line_up_to_cursor.rfind("hash(\""));
            let last_hash_close = line_up_to_cursor.rfind("\")");
            let typing_in_hash = if let Some(open_pos) = last_hash_open {
                last_hash_close.map_or(true, |close_pos| close_pos < open_pos)
            } else {
                false
            };
            
            if typing_in_hash {
                let search_start = line_up_to_cursor.rfind("HASH(\"").or_else(|| line_up_to_cursor.rfind("hash(\""));
                if let Some(start_pos) = search_start {
                    let search_text = &line_up_to_cursor[start_pos + 6..];
                    let search_lower = search_text.to_lowercase();
                    
                    // Check if already complete
                    let already_complete = if let Some(open_pos) = last_hash_open {
                        line_text[open_pos..].contains("\")")
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
                    ret.sort_by(|x, y| x.label.cmp(&y.label));
                    return Ok(Some(CompletionResponse::Array(ret)));
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

            if global_prefix.chars().all(char::is_whitespace) {
                instruction_completions("", &mut ret);
            } else {
                let Some(line_node) = node.find_parent("line") else {
                    return Ok(None);
                };

                let Some(instruction_node) = line_node.query(
                    "(instruction)@x",
                    file_data.document_data.content.as_bytes(),
                ) else {
                    return Ok(None);
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

                let (current_param, operand_node) =
                    get_current_parameter(instruction_node, position.0.character as usize);

                let operand_text = operand_node
                    .map(|node| node.utf8_text(document.content.as_bytes()).unwrap())
                    .unwrap_or("");

                let prefix = {
                    if let Some(operand_node) = operand_node {
                        let cursor_pos = (position.0.character as usize)
                            .saturating_sub(operand_node.start_position().column);
                        &operand_text[..(cursor_pos + 1).min(operand_text.len())]
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
                // - define: second parameter (index 1) is usually a device hash
                // - lbn/sbn: third parameter (index 2) is the nameHash for targeting specific device by name
                let suggest_hash = (text == "define" && current_param == 1) 
                    || ((text == "lbn" || text == "sbn") && current_param == 2);
                
                if suggest_hash && !prefix.contains("HASH") && !prefix.contains("hash") {
                    ret.push(CompletionItem {
                        label: "HASH(\"".to_string(),
                        kind: Some(CompletionItemKind::SNIPPET),
                        detail: Some("Device hash function".to_string()),
                        documentation: Some(Documentation::String(
                            "Insert HASH(\"\") to get device hash by name".to_string()
                        )),
                        insert_text: Some("HASH(\"".to_string()),
                        insert_text_format: Some(InsertTextFormat::PLAIN_TEXT),
                        sort_text: Some("0000".to_string()), // Sort to top
                        ..Default::default()
                    });
                }

                // Check if we're typing HASH(" even before it's fully parsed
                // This handles the case where user types HASH(" and we want to show device completions immediately
                // Only trigger if HASH(" is being typed RIGHT NOW at cursor (not earlier in line)
                let line_text = line_node.utf8_text(document.content.as_bytes()).unwrap();
                let line_up_to_cursor = &line_text[..cursor_pos.min(line_text.len())];
                // Check if cursor is immediately after HASH(" or within an unclosed HASH("
                let just_opened_hash = line_up_to_cursor.ends_with("HASH(\"") || line_up_to_cursor.ends_with("hash(\"");
                // Check if we're currently inside an unclosed HASH(" - find last occurrence
                let last_hash_open = line_up_to_cursor.rfind("HASH(\"").or_else(|| line_up_to_cursor.rfind("hash(\""));
                let last_hash_close = line_up_to_cursor.rfind("\")");
                let typing_in_hash = if let Some(open_pos) = last_hash_open {
                    // We're typing in HASH if there's an open quote and either no close quote,
                    // or the close quote comes before the open quote
                    last_hash_close.map_or(true, |close_pos| close_pos < open_pos)
                } else {
                    false
                };
                
                if just_opened_hash || typing_in_hash {
                    // Extract the search text after HASH("
                    let search_start = line_up_to_cursor.rfind("HASH(\"").or_else(|| line_up_to_cursor.rfind("hash(\""));
                    if let Some(start_pos) = search_start {
                        let search_text = &line_up_to_cursor[start_pos + 6..]; // Skip past HASH("
                        let start_entries = ret.len();
                        let search_lower = search_text.to_lowercase();

                        // Check if HASH call is already complete (has closing ") somewhere on the line)
                        // Look for ") after the HASH(" opening
                        let already_complete = if let Some(open_pos) = last_hash_open {
                            line_text[open_pos..].contains("\")")
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
                // If so, provide device name completions
                if let Some(hash_func_node) = node.find_parent("hash_function") {
                    if let Some(hash_string_node) = hash_func_node.child_by_field_name("argument") {
                        let string_text = hash_string_node
                            .utf8_text(file_data.document_data.content.as_bytes())
                            .unwrap();
                        
                        // Extract content without quotes
                        let search_text = if string_text.starts_with('"') && string_text.ends_with('"') && string_text.len() >= 2 {
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
                // If so, suppress logic type completions (no completions needed for STR)
                if let Some(_str_func_node) = node.find_parent("str_function") {
                    // Inside STR() - no completions needed
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
                                        let mut edit_range =
                                            Range::from(preproc_string_node.range());
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
                // Only show completions that match the expected parameter type
                
                if !text.starts_with("br") && text.starts_with("b") || text == "j" || text == "jal"
                {
                    // Branch instructions - ONLY show labels
                    // User specifically requested ONLY labels, no defines/aliases/constants
                    param_completions_dynamic(
                        prefix,
                        &file_data.type_data.labels,
                        " label",
                        param_type,
                        &mut ret,
                        None,
                    );
                    
                    // Do NOT show anything else for branch instructions
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
                    if content_lower.contains("ra") { used_items.insert("ra".to_string()); }
                    if content_lower.contains("sp") { used_items.insert("sp".to_string()); }
                    for i in 0..=5 {
                        let dev = format!("d{}", i);
                        if content_lower.contains(&dev) {
                            used_items.insert(dev);
                        }
                    }
                    if content_lower.contains("db") { used_items.insert("db".to_string()); }
                    
                    // Add all defined aliases and defines to used_items
                    for alias_name in file_data.type_data.aliases.keys() {
                        used_items.insert(alias_name.clone());
                    }
                    for define_name in file_data.type_data.defines.keys() {
                        used_items.insert(define_name.clone());
                    }
                    
                    // Special case: batch instructions expect device hash at specific parameter positions
                    // Load batch (lb, lbn, lbs, lbns): device hash at parameter 1
                    // Store batch (sb, sbn, sbs): device hash at parameter 0
                    let is_load_batch = matches!(text, "lb" | "lbn" | "lbs" | "lbns");
                    let is_store_batch = matches!(text, "sb" | "sbn" | "sbs");
                    let is_device_hash_param = (is_load_batch && current_param == 1) || (is_store_batch && current_param == 0);
                    
                    if is_device_hash_param {
                        // This is the device hash parameter for a batch instruction
                        // Show device names for HASH() completion instead of numbers/enums
                        
                        // Check if we're typing HASH(" for device hash
                        let line_text = line_node.utf8_text(document.content.as_bytes()).unwrap();
                        let line_up_to_cursor = &line_text[..cursor_pos.min(line_text.len())];
                        let has_hash_quote = line_up_to_cursor.contains("HASH(\"") || line_up_to_cursor.contains("hash(\"");
                        
                        if has_hash_quote {
                            // Already handled above in HASH detection
                        } else {
                            // Not typing HASH yet - suggest starting with HASH(
                            // Show defines that might be device hashes, and suggest HASH( pattern
                            param_completions_dynamic(
                                prefix,
                                &file_data.type_data.defines,
                                " define",
                                param_type,
                                &mut ret,
                                Some(&used_items),
                            );
                            
                            // Add a helpful hint to use HASH()
                            if prefix.is_empty() || "HASH".starts_with(&prefix.to_uppercase()) {
                                ret.push(CompletionItem {
                                    label: "HASH(\"\")" .to_string(),
                                    label_details: Some(CompletionItemLabelDetails {
                                        description: None,
                                        detail: Some(" device hash".to_string()),
                                    }),
                                    kind: Some(CompletionItemKind::SNIPPET),
                                    documentation: Some(Documentation::String("Start typing device name".to_string())),
                                    insert_text: Some("HASH(\"".to_string()),
                                    ..Default::default()
                                });
                            }
                        }
                        // Skip other completions for device hash parameter
                        return Ok(Some(CompletionResponse::Array(ret)));
                    }
                    
                    // Check if this is a static-only parameter type (BatchMode, LogicType, SlotLogicType)
                    let is_static_only = param_type.0.iter().any(|t| matches!(t, 
                        DataType::BatchMode | DataType::LogicType | DataType::SlotLogicType | DataType::ReagentMode));
                    
                    if is_static_only {
                        // For static-only parameters, ONLY show the predefined constants
                        // Don't show registers, aliases, defines, or enums
                        param_completions_static("", "", param_type, &mut ret);
                    } else {
                        // For other parameters, show the full completion list
                        let builtin_start = ret.len();
                        // 0. Show built-in registers and devices first (always available)
                        param_completions_builtin(prefix, param_type, &mut ret, Some(&used_items));
                        // Already sorted by usage inside param_completions_builtin
                        
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
                        // Defines are often device hashes or important constants that should appear first
                        if param_type.match_type(DataType::Number) {
                            ret.sort_by(|a, b| {
                                let a_is_define = a.label_details.as_ref()
                                    .and_then(|d| d.detail.as_ref())
                                    .map(|s| s.contains("define"))
                                    .unwrap_or(false);
                                let b_is_define = b.label_details.as_ref()
                                    .and_then(|d| d.detail.as_ref())
                                    .map(|s| s.contains("define"))
                                    .unwrap_or(false);
                                let a_used = used_items.contains(&a.label);
                                let b_used = used_items.contains(&b.label);
                                
                                // Priority: used defines > unused defines > used others > unused others
                                match (a_is_define, b_is_define, a_used, b_used) {
                                    (true, false, _, _) => std::cmp::Ordering::Less,  // defines first
                                    (false, true, _, _) => std::cmp::Ordering::Greater,
                                    (true, true, true, false) => std::cmp::Ordering::Less,  // used defines before unused
                                    (true, true, false, true) => std::cmp::Ordering::Greater,
                                    (false, false, true, false) => std::cmp::Ordering::Less,  // used before unused
                                    (false, false, false, true) => std::cmp::Ordering::Greater,
                                    _ => a.label.cmp(&b.label),  // alphabetical within same category
                                }
                            });
                        }
                    }
                }
            }
        }

        Ok(Some(CompletionResponse::Array(ret)))
    }

    async fn signature_help(&self, params: SignatureHelpParams) -> Result<Option<SignatureHelp>> {
        let uri = params.text_document_position_params.text_document.uri;
        let position = Position::from(params.text_document_position_params.position);

        let files = self.files.read().await;
        let Some(file_data) = files.get(&uri) else {
            return Err(tower_lsp::jsonrpc::Error::invalid_request());
        };

        let document = &file_data.document_data;

        let Some(ref tree) = document.tree else {
            return Err(tower_lsp::jsonrpc::Error::internal_error());
        };

        let Some(node) = self.node_at_position(position, tree) else {
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

        let (current_param, _) = get_current_parameter(
            instruction_node,
            position.0.character.saturating_sub(1) as usize,
        );

        let Some(signature) = instructions::INSTRUCTIONS.get(text) else {
            return Ok(None);
        };

        // Use the enriched syntax with labeled parameters for better guidance
        let label = crate::tooltip_documentation::get_instruction_syntax(text);
        let mut parameters: Vec<[u32; 2]> = Vec::new();
        // Derive parameter spans by locating tokens after the opcode
        let tokens: Vec<&str> = label.split(' ').collect();
        if tokens.len() > 1 {
            // Search progressively to get stable ranges
            let mut search_start: usize = 0;
            for tok in tokens.iter().skip(1) {
                if tok.is_empty() {
                    continue;
                }
                if let Some(rel_idx) = label[search_start..].find(tok) {
                    let start = search_start + rel_idx;
                    let end = start + tok.len();
                    parameters.push([start as u32, end as u32]);
                    search_start = end;
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
                active_parameter: Some(current_param as u32),
            }],
            active_signature: None,
            active_parameter: None,
        }))
    }

    async fn code_action(
        &self,
        params: CodeActionParams,
    ) -> Result<Option<Vec<CodeActionOrCommand>>> {
        let mut ret = Vec::new();

        let files = self.files.read().await;
        let Some(file_data) = files.get(&params.text_document.uri) else {
            return Err(tower_lsp::jsonrpc::Error::invalid_request());
        };

        let document = &file_data.document_data;
        let uri = &document.url;

        let Some(ref tree) = document.tree else {
            return Err(tower_lsp::jsonrpc::Error::invalid_request());
        };

        let Some(node) = self.node_at_range(params.range.into(), tree) else {
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
                                let line_start = content.lines().take(line_idx).map(|l| l.len() + 1).sum::<usize>();
                                let line_end = line_start + content.lines().nth(line_idx).unwrap().len();
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
            additional_features::get_instruction_code_actions(&node, &document.content)
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

    async fn goto_definition(
        &self,
        params: GotoDefinitionParams,
    ) -> Result<Option<GotoDefinitionResponse>> {
        let files = self.files.read().await;
        let Some(file_data) = files.get(&params.text_document_position_params.text_document.uri)
        else {
            return Err(tower_lsp::jsonrpc::Error::internal_error());
        };
        let document = &file_data.document_data;
        let mut type_data = file_data.type_data.clone();

        let position = params.text_document_position_params.position;

        if let Some(tree) = document.tree.as_ref() {
            if let Some(node) = self.node_at_position(position.into(), tree) {
                if node.kind() == "identifier" {
                    let name = node.utf8_text(document.content.as_bytes()).unwrap();
                    if let Some(range) = type_data.get_range(name) {
                        return Ok(Some(GotoDefinitionResponse::Scalar(Location::new(
                            document.url.clone(),
                            range.0,
                        ))));
                    }
                }
            }
        }
        Ok(None)
    }

    async fn hover(&self, params: HoverParams) -> Result<Option<Hover>> {
        let files = self.files.read().await;
        let Some(file_data) = files.get(&params.text_document_position_params.text_document.uri)
        else {
            return Err(tower_lsp::jsonrpc::Error::internal_error());
        };
        let document = &file_data.document_data;
        let mut type_data = file_data.type_data.clone();

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
                            tooltip_documentation::create_enhanced_instruction_hover_with_history(
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
                let Some(instruction_node) = node.find_parent("instruction") else {
                    return Ok(None);
                };

                let Some(operation_node) = instruction_node.child_by_field_name("operation") else {
                    return Ok(None);
                };

                let operation = operation_node
                    .utf8_text(document.content.as_bytes())
                    .unwrap();

                let (current_param, _) =
                    get_current_parameter(instruction_node, position.character as usize);

                let candidates = instructions::logictype_candidates(name);

                let types = if let Some(signature) = instructions::INSTRUCTIONS.get(operation) {
                    if let Some(param_type) = signature.0.get(current_param) {
                        param_type.intersection(&candidates)
                    } else {
                        candidates
                    }
                } else {
                    candidates
                };

                let strings = types
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

                return Ok(Some(Hover {
                    contents: HoverContents::Array(strings),
                    range: Some(Range::from(node.range()).into()),
                }));
            }
            "function_call" => {
                let text = node.utf8_text(document.content.as_bytes()).unwrap();
                if let Some(device_name) = crate::hash_utils::extract_hash_argument(text) {
                    if let Some(device_hash) = crate::hash_utils::get_device_hash(&device_name) {
                        if let Some(device_display_name) =
                            crate::hash_utils::get_device_name_for_hash(device_hash)
                        {
                            return Ok(Some(Hover {
                                contents: HoverContents::Scalar(MarkedString::String(
                                    device_display_name.to_string(),
                                )),
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
            _ => {}
        }
        Ok(None)
    }
}

impl Backend {
    fn node_at_position<'a>(&'a self, position: Position, tree: &'a Tree) -> Option<Node<'a>> {
        self.node_at_range(
            tower_lsp::lsp_types::Range::new(position.into(), position.into()).into(),
            tree,
        )
    }

    fn node_at_range<'a>(&'a self, range: Range, tree: &'a Tree) -> Option<Node<'a>> {
        let root = tree.root_node();
        let start = Position::from(range.0.start);
        let end = Position::from(range.0.end);
        let node = root.named_descendant_for_point_range(start.into(), end.into());

        node
    }

    fn should_ignore_limits(content: &str) -> bool {
        // Check for #IgnoreLimits directive in comments (case-insensitive)
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

    fn should_ignore_register_warnings(content: &str) -> bool {
        // Check for #IgnoreRegisterWarnings directive in comments (case-insensitive)
        for line in content.lines() {
            let trimmed = line.trim();
            if let Some(comment_start) = trimmed.find('#') {
                let comment = trimmed[comment_start + 1..].trim().to_lowercase();
                if comment.starts_with("ignoreregisterwarnings") {
                    return true;
                }
            }
        }
        false
    }

    async fn update_content(&self, uri: Url, mut text: String) {
        let mut files = self.files.write().await;

        if !text.ends_with("\n") {
            text.push('\n');
        }
        match files.entry(uri) {
            std::collections::hash_map::Entry::Vacant(entry) => {
                let mut parser = Parser::new();
                parser
                    .set_language(tree_sitter_ic10::language())
                    .expect("Could not set language");
                let key = entry.key().clone();
                entry.insert(FileData {
                    document_data: DocumentData {
                        url: key,
                        tree: parser.parse(&text, None),
                        content: text,
                        parser,
                    },
                    type_data: TypeData::default(),
                    last_diagnostic_run: None,
                });
            }
            std::collections::hash_map::Entry::Occupied(mut entry) => {
                let entry = entry.get_mut();
                entry.document_data.tree = entry.document_data.parser.parse(&text, None); // TODO
                entry.document_data.content = text;
                // Don't reset last_diagnostic_run here - it will be updated when diagnostics actually run
            }
        }
    }

    async fn update_definitions(&self, uri: &Url, diagnostics: &mut Vec<Diagnostic>) {
        let mut files = self.files.write().await;
        let Some(file_data) = files.get_mut(uri) else {
            return;
        };
        let document = &file_data.document_data;
        let type_data = &mut file_data.type_data;

        if let Some(tree) = document.tree.as_ref() {
            type_data.defines.clear();
            type_data.aliases.clear();
            type_data.labels.clear();

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

            let captures = cursor.captures(&query, tree.root_node(), document.content.as_bytes());

            for (capture, _) in captures {
                let capture_idx = capture.captures[0].index;
                if capture_idx == define_idx || capture_idx == alias_idx {
                    if let Some(name_node) = capture.captures[0].node.child_by_field_name("operand")
                    {
                        // Prefer the inner identifier text to avoid whitespace/operand wrapper differences
                        let name = if let Some(inner) = name_node.child(0) {
                            inner.utf8_text(document.content.as_bytes()).unwrap()
                        } else {
                            name_node.utf8_text(document.content.as_bytes()).unwrap()
                        }.trim();
                        let previous_range = {
                            if let Some(previous) = type_data.defines.get(name) {
                                Some(previous.range.clone())
                            } else if let Some(previous) = type_data.aliases.get(name) {
                                Some(previous.range.clone())
                            } else {
                                None
                            }
                        };
                        if let Some(previous_range) = previous_range {
                            diagnostics.push(Diagnostic::new(
                                Range::from(name_node.range()).into(),
                                Some(DiagnosticSeverity::ERROR),
                                None,
                                None,
                                "Duplicate definition".to_string(),
                                Some(vec![DiagnosticRelatedInformation {
                                    location: Location::new(
                                        document.url.clone(),
                                        previous_range.into(),
                                    ),
                                    message: "Previously defined here".to_string(),
                                }]),
                                None,
                            ));
                            continue;
                        } else {
                            let mut cursor = capture.captures[0].node.walk();
                            let value_node = capture.captures[0]
                                .node
                                .children_by_field_name("operand", &mut cursor)
                                .last();

                            if let Some(value_node) = value_node {
                                let value =
                                    value_node.utf8_text(document.content.as_bytes()).unwrap();
                                if capture.captures[0].index == define_idx {
                                    // Allow defines to be numeric or function-call / preproc strings / identifiers
                                    // (e.g. HASH(...) or STR(...)) so user can define hash or string constants.
                                    let child_kind =
                                        value_node.child(0).map(|x| x.kind()).unwrap_or("");
                                    if child_kind != "number"
                                        && child_kind != "function_call"
                                        && child_kind != "hash_function"
                                        && child_kind != "str_function"
                                        && child_kind != "preproc_string"
                                        && child_kind != "identifier"
                                    {
                                        continue;
                                    }
                                    type_data.defines.insert(
                                        name.to_owned(),
                                        DefinitionData::new(
                                            name_node.range().into(),
                                            value.to_string().into(),
                                        ),
                                    );
                                } else if capture.captures[0].index == alias_idx {
                                    if value_node
                                        .child(0)
                                        .map(|x| x.kind())
                                        .map_or(false, |x| x != "register" && x != "device_spec")
                                    {
                                        continue;
                                    }
                                    type_data.aliases.insert(
                                        name.to_owned(),
                                        DefinitionData::new(
                                            name_node.range().into(),
                                            value.to_owned().into(),
                                        ),
                                    );
                                }
                            }
                        }
                    }
                } else if capture_idx == label_idx {
                    let name_node = capture.captures[0].node;
                    let name = name_node.utf8_text(document.content.as_bytes()).unwrap();
                    if let Some(previous) = type_data.get_range(name) {
                        diagnostics.push(Diagnostic::new(
                            Range::from(name_node.range()).into(),
                            Some(DiagnosticSeverity::ERROR),
                            None,
                            None,
                            "Duplicate definition".to_string(),
                            Some(vec![DiagnosticRelatedInformation {
                                location: Location::new(document.url.clone(), previous.into()),
                                message: "Previously defined here".to_string(),
                            }]),
                            None,
                        ));
                        continue;
                    }
                    type_data.labels.insert(
                        name.to_owned(),
                        DefinitionData {
                            range: name_node.range().into(),
                            value: name_node.start_position().row as u8,
                        },
                    );
                }
                //println!("{:#?}", capture);
            }
            // println!("{:#?}", type_data.defines);
            // println!("{:#?}", type_data.aliases);
            // println!("{:#?}", type_data.labels);
        }
    }

    async fn check_types(&self, uri: &Url, diagnostics: &mut Vec<Diagnostic>) {
        let files = self.files.read().await;
        let Some(file_data) = files.get(uri) else {
            return;
        };
        let document = &file_data.document_data;
        let mut type_data = file_data.type_data.clone();

        let Some(tree) = document.tree.as_ref() else {
            return;
        };

        // Read config before the loop to avoid await across non-Send types
        let suppress_hash_diagnostics = self.config.read().await.suppress_hash_diagnostics;
        self.client.log_message(MessageType::INFO, format!("Running diagnostics with suppress_hash_diagnostics: {}", suppress_hash_diagnostics)).await;

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

    async fn run_diagnostics(&self, uri: &Url) {
        // If diagnostics disabled, clear and bail
        if !*self.diagnostics_enabled.read().await {
            self.client
                .publish_diagnostics(uri.clone(), vec![], None)
                .await;
            return;
        }
        
        // Update the last diagnostic run time
        {
            let mut files = self.files.write().await;
            if let Some(file_data) = files.get_mut(uri) {
                file_data.last_diagnostic_run = Some(Instant::now());
            }
        }
        
        let mut diagnostics = Vec::new();

        // Collect definitions
        self.update_definitions(uri, &mut diagnostics).await;

        let config = self.config.read().await;
        let files = self.files.read().await;
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

        // Type check
        self.check_types(uri, &mut diagnostics).await;

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
            if !Self::should_ignore_limits(&document.content) {
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
            if !Self::should_ignore_limits(&document.content) {
                let mut byte_count = 0;
                let mut start_pos: Option<LspPosition> = None;
                let lines: Vec<&str> = document.content.lines().collect();

                // Stationeers byte counting (matches UpdateFileSize() method):
                // After paste, each line is trimmed with TrimEnd()
                // Then UpdateFileSize() counts: line.Length + 2 bytes (CRLF) for all except last line
                // This matches the file loading behavior from InputSourceCode.cs lines 562-568
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
            use crate::hash_utils::{extract_hash_argument, is_numeric_string};
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
            use std::collections::HashSet;
            let mut seen: HashSet<(u32, u32, u32, u32, String)> = HashSet::new();
            diagnostics.retain(|d| seen.insert(diagnostic_identity(d)));
        }

        self.client
            .publish_diagnostics(uri.to_owned(), diagnostics, None)
            .await;
    }
}

#[derive(Clone, Copy)]
struct KeywordFlags(u8);

impl KeywordFlags {
    fn from_bools(logic: bool, slot: bool, batch: bool, reagent: bool) -> Self {
        KeywordFlags(
            (logic as u8) | ((slot as u8) << 1) | ((batch as u8) << 2) | ((reagent as u8) << 3),
        )
    }

    fn any(self) -> bool {
        self.0 != 0
    }

    fn to_union(self) -> instructions::Union<'static> {
        union_from_mask(self.0)
    }
}

fn classify_exact_keyword(ident: &str) -> KeywordFlags {
    KeywordFlags::from_bools(
        instructions::LOGIC_TYPES.contains(ident),
        instructions::SLOT_LOGIC_TYPES.contains(ident),
        instructions::BATCH_MODES.contains(ident),
        instructions::REAGENT_MODES.contains(ident),
    )
}

fn classify_ci_keyword(ident: &str) -> KeywordFlags {
    KeywordFlags::from_bools(
        instructions::LOGIC_TYPES
            .iter()
            .any(|x| x.eq_ignore_ascii_case(ident)),
        instructions::SLOT_LOGIC_TYPES
            .iter()
            .any(|x| x.eq_ignore_ascii_case(ident)),
        instructions::BATCH_MODES
            .iter()
            .any(|x| x.eq_ignore_ascii_case(ident)),
        instructions::REAGENT_MODES
            .iter()
            .any(|x| x.eq_ignore_ascii_case(ident)),
    )
}

fn union_from_mask(mask: u8) -> instructions::Union<'static> {
    match mask {
        0 => instructions::Union(&[]),
        0b0001 => instructions::Union(&LOGIC_ONLY),
        0b0010 => instructions::Union(&SLOT_ONLY),
        0b0100 => instructions::Union(&BATCH_ONLY),
        0b1000 => instructions::Union(&REAGENT_ONLY),
        0b0011 => instructions::Union(&LOGIC_SLOT),
        0b0101 => instructions::Union(&LOGIC_BATCH),
        0b1001 => instructions::Union(&LOGIC_REAGENT),
        0b0110 => instructions::Union(&SLOT_BATCH),
        0b1010 => instructions::Union(&SLOT_REAGENT),
        0b1100 => instructions::Union(&BATCH_REAGENT),
        0b0111 => instructions::Union(&LOGIC_SLOT_BATCH),
        0b1011 => instructions::Union(&LOGIC_SLOT_REAGENT),
        0b1101 => instructions::Union(&LOGIC_BATCH_REAGENT),
        0b1110 => instructions::Union(&SLOT_BATCH_REAGENT),
        0b1111 => instructions::Union(&LOGIC_SLOT_BATCH_REAGENT),
        _ => instructions::Union(&[]),
    }
}

fn diagnostic_identity(diag: &Diagnostic) -> (u32, u32, u32, u32, String) {
    (
        diag.range.start.line,
        diag.range.start.character,
        diag.range.end.line,
        diag.range.end.character,
        diag.message.clone(),
    )
}

/// Compute diagnostics for a single text buffer using the same logic as the LSP diagnostics.
fn compute_diagnostics_for_text(content: &str) -> Vec<tower_lsp::lsp_types::Diagnostic> {
    use tower_lsp::lsp_types::{
        Diagnostic, DiagnosticSeverity, Position as LspPosition, Range as LspRange,
    };
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

fn get_current_parameter(instruction_node: Node, position: usize) -> (usize, Option<Node>) {
    let mut ret: usize = 0;
    let mut cursor = instruction_node.walk();
    for operand in instruction_node.children_by_field_name("operand", &mut cursor) {
        if operand.end_position().column > position {
            break;
        }
        ret += 1;
    }

    let operand = instruction_node
        .children_by_field_name("operand", &mut cursor)
        .nth(ret);

    cursor.reset(instruction_node);
    (ret, operand)
}

trait NodeEx: Sized {
    fn find_parent(&self, kind: &str) -> Option<Self>;
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hash_define_is_recognized() {
        let script = r#"define StartButton HASH("StructureLogicButton")
sb StartButton Setting 34"#;
        let diagnostics = compute_diagnostics_for_text(script);
        assert!(
            diagnostics
                .iter()
                .filter(|d| d.severity == Some(tower_lsp::lsp_types::DiagnosticSeverity::ERROR))
                .all(|d| !d.message.contains("Unknown identifier")),
            "Unexpected Unknown identifier diagnostics: {:?}",
            diagnostics
        );
    }
}

#[tokio::main]
async fn main() {
    use clap::Parser as _;
    let cli = cli::Cli::parse();

    // Diagnostic runner mode: if files provided with --diagnose, run the diagnostic logic
    // on each file and print the results to stdout, then exit.
    if !cli.diagnose.is_empty() {
        for path in &cli.diagnose {
            let path_ref = Path::new(path);
            let content = match fs::read_to_string(path_ref) {
                Ok(c) => c,
                Err(e) => {
                    eprintln!("Could not read {}: {}", path_ref.display(), e);
                    continue;
                }
            };

            let diagnostics = compute_diagnostics_for_text(&content);

            println!("Diagnostics for {}:", path_ref.display());
            if diagnostics.is_empty() {
                println!("  (no diagnostics)");
            } else {
                for d in diagnostics {
                    let sev = match d.severity {
                        Some(tower_lsp::lsp_types::DiagnosticSeverity::ERROR) => "ERROR",
                        Some(tower_lsp::lsp_types::DiagnosticSeverity::WARNING) => "WARN",
                        Some(tower_lsp::lsp_types::DiagnosticSeverity::INFORMATION) => "INFO",
                        Some(tower_lsp::lsp_types::DiagnosticSeverity::HINT) => "HINT",
                        _ => "UNKNOWN",
                    };
                    // Print range start line/char and message
                    let range = d.range;
                    println!(
                        "  {}:{}:{} - {}",
                        sev, range.start.line, range.start.character, d.message
                    );
                }
            }
            println!("");
        }
        return;
    }

    let mut parser = Parser::new();
    parser
        .set_language(tree_sitter_ic10::language())
        .expect("Failed to set language");

    let (service, socket) = LspService::new(|client| Backend {
        client,
        files: Arc::new(RwLock::new(HashMap::new())),
        config: Arc::new(RwLock::new(Configuration::default())),
        diagnostics_enabled: Arc::new(RwLock::new(true)),
        warned_about_file_count: Arc::new(tokio::sync::Mutex::new(false)),
    });

    if !cli.listen && cli.host.is_none() {
        // stdin/stdout
        Server::new(tokio::io::stdin(), tokio::io::stdout(), socket)
            .serve(service)
            .await;
    } else if cli.listen {
        // listen

        let host = cli
            .host
            .map(Cow::Owned)
            .unwrap_or(Cow::Borrowed("127.0.0.1"))
            .parse::<Ipv4Addr>()
            .expect("Could not parse IP address");

        let port = cli.port.unwrap_or(9257);

        let stream = {
            let listener = TcpListener::bind((host, port)).await.unwrap();
            let (stream, _) = listener.accept().await.unwrap();
            stream
        };

        let (input, output) = tokio::io::split(stream);
        Server::new(input, output, socket).serve(service).await;
    } else {
        let host = cli.host.expect("No host given");
        let port = cli.port.expect("No port given");

        let stream = TcpStream::connect((host, port))
            .await
            .expect("Could not open TCP stream");

        let (input, output) = tokio::io::split(stream);
        Server::new(input, output, socket).serve(service).await;
    }
}

#[derive(Clone, Copy)]
struct Position(tower_lsp::lsp_types::Position);

#[derive(Clone, Debug)]
struct Range(tower_lsp::lsp_types::Range);

impl Range {
    pub fn contains(&self, position: Position) -> bool {
        let (start_line, start_char) = (self.0.start.line, self.0.start.character);
        let (end_line, end_char) = (self.0.end.line, self.0.end.character);
        let (line, character) = (position.0.line, position.0.character);

        (line > start_line && line < end_line)
            || (line == start_line && character >= start_char)
            || (line == end_line && character <= end_char)
    }
}

impl From<tree_sitter::Point> for Position {
    fn from(value: tree_sitter::Point) -> Self {
        Position(tower_lsp::lsp_types::Position::new(
            value.row as u32,
            value.column as u32,
        ))
    }
}

impl From<tower_lsp::lsp_types::Position> for Position {
    fn from(value: tower_lsp::lsp_types::Position) -> Self {
        Position(value)
    }
}

impl From<Position> for tower_lsp::lsp_types::Position {
    fn from(value: Position) -> Self {
        value.0
    }
}

impl From<Position> for tree_sitter::Point {
    fn from(value: Position) -> Self {
        tree_sitter::Point {
            row: value.0.line as usize,
            column: value.0.character as usize,
        }
    }
}

impl From<tree_sitter::Range> for Range {
    fn from(value: tree_sitter::Range) -> Self {
        Range(tower_lsp::lsp_types::Range::new(
            Position::from(value.start_point).into(),
            Position::from(value.end_point).into(),
        ))
    }
}

impl From<tower_lsp::lsp_types::Range> for Range {
    fn from(value: tower_lsp::lsp_types::Range) -> Self {
        Range(value)
    }
}

impl From<Range> for tower_lsp::lsp_types::Range {
    fn from(value: Range) -> Self {
        value.0
    }
}
