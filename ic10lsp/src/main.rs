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

use ic10lsp::instructions; // access library module with instruction metadata
use std::fs;
use std::path::Path;
use std::time::{Duration, Instant};
use std::{
    borrow::Cow,
    collections::HashMap,
    net::Ipv4Addr,
    sync::Arc,
};
use tower_lsp::lsp_types::SemanticTokenType;
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

/// Performance benchmarking and tracking
mod performance;

/// Device hash mappings and resolution (HASH() function support)
mod device_hashes;

/// Device descriptions from English.xml
mod descriptions;

/// Utility functions for hash computation and parsing
mod hash_utils;

/// Enhanced tooltip/hover documentation with examples
mod tooltip_documentation;

/// Type conversions and position/range utilities
mod types;

/// Document data structures and type tracking  
mod document;

/// Tree-sitter node utilities and extensions
mod tree_utils;

/// Type classification helpers for parameter validation
mod type_classification;

/// Diagnostic helper utilities
mod diagnostic_helpers;

/// LSP completion handler
mod lsp_completion;

/// LSP diagnostics handler
mod lsp_diagnostics;

/// LSP hover and inlay hints handler
mod lsp_hover;

/// LSP handlers for semantic tokens, symbols, signature help, code actions, goto definition
mod lsp_handlers;

// Re-export commonly used items
use types::{Position, Range};
use document::*;

// ============================================================================
// Constants
// ============================================================================

/// Diagnostic code for absolute jump instructions (should use relative jumps)
const LINT_ABSOLUTE_JUMP: &str = "absolute-jump";

/// Diagnostic code for relative branch to label (should use absolute branch)
const LINT_RELATIVE_BRANCH_TO_LABEL: &str = "relative-branch-to-label";

/// Parameters that only accept Name (used in diagnostics)
pub(crate) const NAME_ONLY: [instructions::DataType; 1] = [instructions::DataType::Name];

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

use serde_json::Value;
use tokio::{
    net::{TcpListener, TcpStream},
    sync::RwLock,
};
use tower_lsp::{async_trait, jsonrpc::Result, lsp_types::*, Client};
struct Backend {
    client: Client,
    files: Arc<RwLock<HashMap<Url, FileData>>>,
    config: Arc<RwLock<Configuration>>,
    // Runtime flag to allow diagnostics suppression without restart
    diagnostics_enabled: Arc<RwLock<bool>>,
    // Track when we've warned about too many files
    warned_about_file_count: Arc<tokio::sync::Mutex<bool>>,
    // Performance tracking
    perf_tracker: Arc<performance::PerformanceTracker>,
    // Debounce: Store pending diagnostic tasks per file
    pending_diagnostics: Arc<tokio::sync::Mutex<HashMap<Url, tokio::task::JoinHandle<()>>>>,
    // Cache: Store diagnostics by content hash (DashMap is lock-free concurrent)
    diagnostic_cache: Arc<dashmap::DashMap<String, Vec<Diagnostic>>>,
}

// Constants for performance tuning
const MAX_RECOMMENDED_FILES: usize = 50;
const DIAGNOSTIC_DEBOUNCE_MS: u64 = 250; // Increased from 150ms for better batching with cache
const DIAGNOSTIC_DEBOUNCE_LARGE_FILE_MS: u64 = 400; // For files >500 lines

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

    async fn initialized(&self, _params: InitializedParams) {
        // Warm up tree-sitter parser by parsing a minimal document
        // This moves the initialization cost to startup rather than first user edit
        let mut parser = Parser::new();
        let _ = parser.set_language(tree_sitter_ic10::language());
        let _ = parser.parse("add r0 r0 1\n", None);
        
        // Warm up all cached queries by calling the functions that initialize OnceLock
        // This ensures queries are compiled at startup, not on first inlay hint request
        crate::lsp_hover::warmup_queries();
        crate::tree_utils::warmup_queries();
    }

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
            "ic10.server.enableBenchmarking" => {
                if let Some(enabled) = params.arguments.get(0).and_then(Value::as_bool) {
                    self.perf_tracker.set_enabled(enabled);
                    let message = if enabled {
                        "IC10 LSP Server benchmarking enabled. Collecting performance data..."
                    } else {
                        "IC10 LSP Server benchmarking disabled."
                    };
                    self.client.show_message(MessageType::INFO, message).await;
                }
            }
            "ic10.server.getBenchmarkReport" => {
                let report = self.perf_tracker.generate_report();
                self.client.log_message(MessageType::INFO, report.clone()).await;
                return Ok(Some(Value::String(report)));
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
        
        // Request inlay hint refresh to show device hashes in newly opened file
        let _ = self.client.send_request::<tower_lsp::lsp_types::request::InlayHintRefreshRequest>(()).await;
        
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
        
        // Request inlay hint refresh to show updated device hashes
        let _ = self.client.send_request::<tower_lsp::lsp_types::request::InlayHintRefreshRequest>(()).await;
        
        // Proper debouncing: Cancel any pending diagnostic task and schedule a new one
        // This ensures diagnostics run X ms after the LAST keystroke, not just throttling
        let uri = params.text_document.uri.clone();
        let debounce_ms = {
            let files = self.files.read().await;
            if let Some(file_data) = files.get(&uri) {
                let line_count = file_data.document_data.content.lines().count();
                if line_count > 500 {
                    DIAGNOSTIC_DEBOUNCE_LARGE_FILE_MS
                } else {
                    DIAGNOSTIC_DEBOUNCE_MS
                }
            } else {
                DIAGNOSTIC_DEBOUNCE_MS
            }
        };
        
        // Cancel any existing pending diagnostic task for this file
        {
            let mut pending = self.pending_diagnostics.lock().await;
            if let Some(handle) = pending.remove(&uri) {
                handle.abort(); // Cancel the old task
            }
        }
        
        // Schedule new diagnostic task
        let uri_for_task = uri.clone();
        let backend = Self {
            client: self.client.clone(),
            files: self.files.clone(),
            config: self.config.clone(),
            diagnostics_enabled: self.diagnostics_enabled.clone(),
            warned_about_file_count: self.warned_about_file_count.clone(),
            perf_tracker: self.perf_tracker.clone(),
            pending_diagnostics: self.pending_diagnostics.clone(),
            diagnostic_cache: self.diagnostic_cache.clone(),
        };
        
        let handle = tokio::spawn(async move {
            tokio::time::sleep(Duration::from_millis(debounce_ms)).await;
            backend.run_diagnostics(&uri_for_task).await;
            // Remove self from pending map after completion
            backend.pending_diagnostics.lock().await.remove(&uri_for_task);
        });
        
        // Store the new task handle
        self.pending_diagnostics.lock().await.insert(uri, handle);
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
        lsp_hover::handle_inlay_hint(self, params).await
    }

    async fn semantic_tokens_full(
        &self,
        params: SemanticTokensParams,
    ) -> Result<Option<SemanticTokensResult>> {
        lsp_handlers::handle_semantic_tokens_full(self, params).await
    }

    async fn document_symbol(
        &self,
        params: DocumentSymbolParams,
    ) -> Result<Option<DocumentSymbolResponse>> {
        lsp_handlers::handle_document_symbol(self, params).await
    }

    async fn completion(&self, params: CompletionParams) -> Result<Option<CompletionResponse>> {
        lsp_completion::handle_completion(self, params).await
    }

    async fn signature_help(&self, params: SignatureHelpParams) -> Result<Option<SignatureHelp>> {
        lsp_handlers::handle_signature_help(self, params).await
    }

    async fn code_action(
        &self,
        params: CodeActionParams,
    ) -> Result<Option<Vec<CodeActionOrCommand>>> {
        lsp_handlers::handle_code_action(self, params).await
    }

    async fn goto_definition(
        &self,
        params: GotoDefinitionParams,
    ) -> Result<Option<GotoDefinitionResponse>> {
        lsp_handlers::handle_goto_definition(self, params).await
    }

    async fn hover(&self, params: HoverParams) -> Result<Option<Hover>> {
        lsp_hover::handle_hover(self, params).await
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
                let tree = {
                    let _timer = performance::TimingGuard::new(&self.perf_tracker, "lsp.server.parsing");
                    self.perf_tracker.increment("lsp.server.parsing.calls", 1);
                    parser.parse(&text, None)
                };
                entry.insert(FileData {
                    document_data: DocumentData {
                        url: key,
                        tree,
                        content: text,
                        parser,
                    },
                    type_data: TypeData::default(),
                    last_diagnostic_run: None,
                });
            }
            std::collections::hash_map::Entry::Occupied(mut entry) => {
                let entry = entry.get_mut();
                let tree = {
                    let _timer = performance::TimingGuard::new(&self.perf_tracker, "lsp.server.parsing");
                    self.perf_tracker.increment("lsp.server.parsing.calls", 1);
                    entry.document_data.parser.parse(&text, None)
                };
                entry.document_data.tree = tree; // TODO
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

    /// Run full diagnostics on a document - delegates to lsp_diagnostics module
    async fn run_diagnostics(&self, uri: &Url) {
        lsp_diagnostics::run_diagnostics(self, uri).await
    }
}

/// Compute diagnostics for a single text buffer - delegates to lsp_diagnostics module
fn compute_diagnostics_for_text(content: &str) -> Vec<Diagnostic> {
    lsp_diagnostics::compute_diagnostics_for_text(content)
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
                Err(_e) => {
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
        perf_tracker: Arc::new(performance::PerformanceTracker::new()),
        pending_diagnostics: Arc::new(tokio::sync::Mutex::new(HashMap::new())),
        diagnostic_cache: Arc::new(dashmap::DashMap::new()),
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

