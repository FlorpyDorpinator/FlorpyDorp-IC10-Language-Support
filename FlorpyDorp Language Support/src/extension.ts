/**
 * IC10 Language Support Extension for Visual Studio Code
 * 
 * This extension provides comprehensive language support for the IC10 MIPS-like assembly
 * language used in the game Stationeers. It connects VSCode to the ic10lsp language server
 * and provides additional client-side enhancements.
 * 
 * Key Features:
 * - Language server client initialization and management
 * - Enhanced hover tooltips with game-style instruction signatures
 * - Smart completion filtering and formatting
 * - Diagnostic control (enable/disable syntax checking)
 * - Inlay hints for instruction parameters
 * - Command palette integration
 * 
 * Architecture:
 * - Communicates with the Rust-based ic10lsp language server via LSP
 * - Enhances server responses with client-side middleware
 * - Manages server lifecycle (start/stop/restart)
 * - Provides custom VS Code commands and UI features
 * 
 * @module extension
 */

// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';
import * as path from 'path';
import * as net from 'net';
import * as fs from 'fs';
import * as os from 'os';
import {
    DidChangeConfigurationNotification,
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    StreamInfo,
    ExecuteCommandParams
} from 'vscode-languageclient/node';

// ============================================================================
// Helper Functions
// ============================================================================

/**
 * Retrieves instruction examples for hover tooltips and documentation.
 * 
 * These examples provide users with practical usage patterns for common IC10
 * instructions. Each instruction includes 2-3 examples ranging from simple
 * to intermediate complexity.
 * 
 * @param instruction - The IC10 instruction name (e.g., 'add', 'l', 's')
 * @returns Array of example code strings with inline comments
 */
function getInstructionExamples(instruction: string): string[] {
    // Basic examples for common instructions - this could be expanded
    const examples: { [key: string]: string[] } = {
        'add': [
            'add r0 r1 r2      # Simple: r0 = r1 + r2',
            'add r7 r5 r6      # Total charge from both batteries',
            'add r10 r8 r9     # Total max power'
        ],
        'sub': [
            'sub r0 r1 r2      # Simple: r0 = r1 - r2',
            'sub currentRoomTemperature currentRoomTemperature 273.15',
            'sub temp temp 10  # temp = temp - 10'
        ],
        'mul': [
            'mul r0 r1 r2      # Simple: r0 = r1 * r2',
            'mul r3 r1 2       # PowerRequired in 1 second',
            'mul r15 r15 r14   # Temperature * TotalMoles'
        ],
        'l': [
            'l r0 d0 Temperature     # Simple: read temperature from device 0',
            'l currentRoomPressure gasSensor Pressure',
            'l leverState01 leverSwitch01 Open'
        ],
        's': [
            's d1 Setting r0         # Simple: set device 1 setting to r0',
            's pressureRegulator Setting targetPipePressure',
            's db Setting currentRoomPressure'
        ]
    };
    
    return examples[instruction.toLowerCase()] || [];
}

/**
 * Retrieves the IC10 LSP configuration from VS Code settings.
 * 
 * This includes settings like max_lines, max_columns, max_bytes, and other
 * language server configuration options that control diagnostics and validation.
 * 
 * @returns Configuration object with IC10 LSP settings
 */
function getLSPIC10Configurations(): any {
    const config = vscode.workspace.getConfiguration('ic10.lsp');
    return {
        max_lines: config.get('max_lines'),
        max_columns: config.get('max_columns'),
        max_bytes: config.get('max_bytes'),
        warnings: {
            overline_comment: config.get('warnings.overline_comment'),
            overcolumn_comment: config.get('warnings.overcolumn_comment')
        },
        suppressHashDiagnostics: config.get('suppressHashDiagnostics'),
        enableControlFlowAnalysis: config.get('enableControlFlowAnalysis'),
        suppressRegisterWarnings: config.get('suppressRegisterWarnings')
    };
}

// ============================================================================
// Extension Activation
// ============================================================================

/**
 * Called when the extension is activated (first time an IC10 file is opened).
 * 
 * This function:
 * 1. Sets up the language server connection (local binary or remote TCP)
 * 2. Registers middleware to enhance hover, completion, and diagnostic behavior
 * 3. Starts the language server client
 * 4. Registers custom commands (restart server, toggle diagnostics, etc.)
 * 5. Sets up configuration change listeners
 * 
 * @param context - The extension context provided by VS Code
 */
/**
 * Resolves VS Code variables in a string (e.g., ${workspaceFolder}, ${extensionPath})
 * 
 * @param str - String potentially containing VS Code variables
 * @param context - Extension context for resolving paths
 * @returns String with variables resolved to actual paths
 */
function resolveVariables(str: string, context: vscode.ExtensionContext): string {
    if (!str) return str;
    
    // Get workspace folder (use first workspace if multiple)
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath || '';
    
    // Replace common VS Code variables
    return str
        .replace(/\$\{workspaceFolder\}/gi, workspaceFolder)
        .replace(/\$\{workspaceRoot\}/gi, workspaceFolder)
        .replace(/\$\{extensionPath\}/gi, context.extensionPath)
        .replace(/\$\{userHome\}/gi, process.env.HOME || process.env.USERPROFILE || '')
        .replace(/~/g, process.env.HOME || process.env.USERPROFILE || '');
}

export function activate(context: vscode.ExtensionContext) {

    // Activate Notification through VSCode Notifications
    vscode.window.showInformationMessage('IC10 Language Server is now active!');

    // Determine the correct binary name based on platform and architecture
    let serverBinary: string;
    if (process.platform === "win32") {
        serverBinary = "ic10lsp-win32.exe";
    } else if (process.platform === "linux") {
        serverBinary = "ic10lsp-linux";
    } else if (process.platform === "darwin") {
        // macOS - check for Apple Silicon (ARM64) vs Intel (x64)
        serverBinary = process.arch === "arm64" ? "ic10lsp-darwin-arm64" : "ic10lsp-darwin";
    } else {
        // Fallback for unknown platforms
        vscode.window.showErrorMessage(`IC10 LSP: Unsupported platform ${process.platform}. Please compile ic10lsp manually and set ic10.lsp.serverPath in settings.`);
        serverBinary = "ic10lsp";
    }
    
    // Allow overriding the server path to avoid copy/lock issues during development
    const serverOverride = vscode.workspace.getConfiguration('ic10.lsp').get('serverPath') as string | undefined;
    
    // Resolve VS Code variables in the server path (e.g., ${workspaceFolder})
    const resolvedServerOverride = serverOverride && serverOverride.trim().length > 0
        ? resolveVariables(serverOverride.trim(), context)
        : undefined;
    
    // The server is implemented in the upstream language server
    const serverModule = resolvedServerOverride || context.asAbsolutePath(path.join('bin', serverBinary));
    
    // Log the resolved server path for debugging
    console.log(`IC10 LSP: Server path resolved to: ${serverModule}`);
    
    // Ensure executable permissions on Unix-like systems
    const fs = require('fs');
    if (process.platform !== "win32" && fs.existsSync(serverModule)) {
        try {
            fs.chmodSync(serverModule, 0o755);
            console.log(`IC10 LSP: Set executable permissions for ${serverModule}`);
        } catch (err) {
            console.warn(`IC10 LSP: Could not set executable permissions: ${err}`);
        }
    }
    
    // Verify server binary exists
    if (!fs.existsSync(serverModule)) {
        const errorMsg = `IC10 Language Server binary not found at: ${serverModule}\n\n` +
            `If using a custom serverPath, ensure the path is correct and uses forward slashes or escaped backslashes.\n` +
            `You can use VS Code variables like \${workspaceFolder} for portable paths.`;
        vscode.window.showErrorMessage(errorMsg);
        console.error(`IC10 LSP: ${errorMsg}`);
    }

    const config = vscode.workspace.getConfiguration();

    const useRemoteLanguageServer = config.get('ic10.useRemoteLanguageServer') as boolean;

    let serverOptions: ServerOptions;

    if (useRemoteLanguageServer) {

        const remoteLanguageServerHost = config.get('ic10.remoteLanguageServerHost') as string;
        const remoteLanguageServerPort = config.get('ic10.remoteLanguageServerPort') as number;

        let connectionInfo = {
            host: remoteLanguageServerHost,
            port: remoteLanguageServerPort
        };
        serverOptions = () => {
            // Connect to language server via socket
            let socket = net.connect(connectionInfo);
            let result: StreamInfo = {
                writer: socket,
                reader: socket
            };
            return Promise.resolve(result);
        };
    }
    else {
        serverOptions = {
            run: { command: serverModule },
            debug: { command: serverModule },
        };
    }

    // Optionally prompt to switch to IC10 theme on first install (only once)
    const hasAskedAboutTheme = context.globalState.get<boolean>('ic10.hasAskedAboutTheme', false);
    
    console.log('[IC10] Theme prompt - hasAsked:', hasAskedAboutTheme);
    
    if (!hasAskedAboutTheme) {
        const currentTheme = vscode.workspace.getConfiguration('workbench').get('colorTheme') as string | undefined;
        const themeOptions = [
            'Stationeers IC10 Syntax Only',
            'Stationeers Full Color Theme'
        ];
        
        console.log('[IC10] Current theme:', currentTheme);
        
        // Only prompt if not already using one of our themes
        if (!themeOptions.includes(currentTheme || '')) {
            console.log('[IC10] Showing theme selection prompt');
            vscode.window.showInformationMessage(
                'Apply IC10 syntax colors? (Dark+ UI + Stationeers in-game colors, or full custom theme)', 
                'Syntax Colors Only', 
                'Full Custom Theme', 
                'No Thanks'
            ).then((choice: string | undefined) => {
                console.log('[IC10] User selected:', choice);
                if (choice === 'Syntax Colors Only') {
                    vscode.workspace.getConfiguration('workbench').update('colorTheme', 'Stationeers IC10 Syntax Only', vscode.ConfigurationTarget.Global);
                } else if (choice === 'Full Custom Theme') {
                    vscode.workspace.getConfiguration('workbench').update('colorTheme', 'Stationeers Full Color Theme', vscode.ConfigurationTarget.Global);
                }
                // Mark that we've asked, regardless of choice
                context.globalState.update('ic10.hasAskedAboutTheme', true);
            });
        } else {
            // Already using one of our themes, mark as asked so we don't prompt in the future
            console.log('[IC10] Already using IC10 theme, skipping prompt');
            context.globalState.update('ic10.hasAskedAboutTheme', true);
        }
    }

    // Options to control the language client
    const clientOptions: LanguageClientOptions = {
        // Register the server for IC10 MIPS-like language documents
        documentSelector: [
            { scheme: 'file', language: 'ic10' },
            { scheme: 'untitled', language: 'ic10' }
        ],
        // Use UTF-8 encoding for proper handling of special characters
        outputChannelName: 'IC10 Language Server',
        initializationOptions: getLSPIC10Configurations(),
        // Add completion trigger characters - space bar triggers parameter completions
        synchronize: {
            configurationSection: 'ic10'
        },
        middleware: {
            provideHover: async (document: vscode.TextDocument, position: vscode.Position, token: vscode.CancellationToken, next: any) => {
                const useGameStyle = vscode.workspace.getConfiguration().get('ic10.hover.useGameStyle') as boolean;
                if (!useGameStyle) {
                    return next(document, position, token);
                }

                const hover = await next(document, position, token);
                if (!hover) return hover;

                // If the server already provides an IC10 code block (game-style signature), keep it.
                // Only prepend a small fallback signature for a few opcodes when none is present.

                const asArray: any[] = Array.isArray(hover.contents) ? (hover.contents as any[]) : [hover.contents];

                // Utility: extract first ic10 code block contents if present
                const extractIc10Block = (): string | undefined => {
                    for (const c of asArray) {
                        if (typeof c === 'string') {
                            const m = c.match(/```ic10\n([\s\S]*?)```/i);
                            if (m) return m[1].trim();
                        } else if (c && typeof c === 'object') {
                            if ('language' in c && 'value' in c && typeof c.language === 'string') {
                                if ((c.language as string).toLowerCase() === 'ic10') {
                                    return (c as any).value.trim();
                                }
                            } else if (typeof (c as vscode.MarkdownString).value === 'string') {
                                const raw = (c as vscode.MarkdownString).value;
                                const m = raw.match(/```ic10\n([\s\S]*?)```/i);
                                if (m) return m[1].trim();
                            }
                        }
                    }
                    return undefined;
                };

                const existingBlock = extractIc10Block();
                let hasIc10Block = existingBlock !== undefined;
                let overridingIncomplete = false;

                // Try to detect opcode under cursor for a minimal fallback signature.
                const range = document.getWordRangeAtPosition(position);
                const opcode = range ? document.getText(range).trim().toLowerCase() : '';

                const fallback: Record<string, string> = {
                    'move': 'move r? a(r?|num)',
                    'add': 'add r? a(r?|num) b(r?|num)',
                    'sub': 'sub r? a(r?|num) b(r?|num)',
                    'mul': 'mul r? a(r?|num) b(r?|num)',
                    'div': 'div r? a(r?|num) b(r?|num)',
                    'mod': 'mod r? a(r?|num) b(r?|num)',
                    's': 's device(d?|r?|id) logicType r?',
                    'l': 'l r? device(d?|r?|id) logicType',
                    'ls': 'ls r? device(d?|r?|id) slotIndex logicSlotType',
                    'lr': 'lr r? device(d?|r?|id) reagentMode int',
                    'lb': 'lb r? deviceHash logicType batchMode',
                    'sb': 'sb deviceHash logicType r?',
                    'lbn': 'lbn r? deviceHash nameHash logicType batchMode',
                    'lbns': 'lbns r? deviceHash nameHash slotIndex logicSlotType batchMode',
                    'lbs': 'lbs r? deviceHash slotIndex logicSlotType batchMode',
                    'sbn': 'sbn deviceHash nameHash logicType r?',
                    'sbs': 'sbs deviceHash slotIndex logicSlotType r?'
                };

                let example = fallback[opcode];
                // Special-case: for logicType under cursor, ensure we treat ReferenceId and BestContactFilter like other logic tokens
                // by adding a tiny inline doc if server didn’t supply one due to identifier parsing.
                if (!example) {
                    const wordRange = document.getWordRangeAtPosition(position);
                    const word = wordRange ? document.getText(wordRange) : '';
                    if (/^(ReferenceId|BestContactFilter)$/i.test(word)) {
                        const md = new vscode.MarkdownString(`# \`${word}\` (logicType)`);
                        md.appendMarkdown(`\nUsed with l/lb/lbn to read or filter contacts; ReferenceId supports batch aggregators like Minimum/Maximum.`);
                        return new vscode.Hover([md, ...(Array.isArray(hover.contents) ? hover.contents as any[] : [hover.contents])], hover.range ?? range);
                    }
                }
                // Decide if we should override an existing (possibly minimal) ic10 block.
                if (hasIc10Block && existingBlock) {
                    // Decide completeness: if any required token for this opcode is missing (like deviceHash for lbn) then override.
                    const needsTokens: Record<string, string[]> = {
                        'lbn': ['deviceHash','nameHash','logicType','batchMode'],
                        'lbns': ['deviceHash','nameHash','slotIndex','logicSlotType','batchMode'],
                        'lbs': ['deviceHash','slotIndex','logicSlotType','batchMode'],
                        'lb': ['deviceHash','logicType','batchMode'],
                        'sbn': ['deviceHash','nameHash','logicType','r?'],
                        'sbs': ['deviceHash','slotIndex','logicSlotType','r?'],
                        'sb': ['deviceHash','logicType','r?'],
                        's': ['device','logicType','r?'],
                        'l': ['r?','device','logicType'],
                        'ls': ['r?','device','slotIndex','logicSlotType'],
                        'lr': ['r?','device','reagentMode','int'],
                        'move': ['r?','a(r?|num)'],
                        'add': ['r?','a(r?|num)','b(r?|num)']
                    };
                    const required = needsTokens[opcode] || [];
                    const lowerBlock = existingBlock.toLowerCase();
                    // Normalize by stripping '?' for comparison on both sides to avoid false negatives
                    const lowerBlockNormalized = lowerBlock.replace(/\?/g, '');
                    const missing = required.some(t => {
                        const tokenNorm = t.replace(/\?/g, '').toLowerCase();
                        return !lowerBlockNormalized.includes(tokenNorm);
                    });
                    if (!missing) {
                        return hover; // appears complete
                    }
                    hasIc10Block = false; // force override
                    overridingIncomplete = true;
                }
                // If server returned multiple ic10 signature blocks (duplicate/minimal variants), keep the most complete one
                if (hasIc10Block) {
                    const icBlocks: { idx: number; value: string }[] = [];
                    asArray.forEach((c, idx) => {
                        if (typeof c === 'string') {
                            const m = c.match(/```ic10\n([\s\S]*?)```/i);
                            if (m) icBlocks.push({ idx, value: m[1].trim() });
                        } else if (c && typeof c === 'object') {
                            if ('language' in c && 'value' in c && typeof (c as any).language === 'string' && ((c as any).language as string).toLowerCase() === 'ic10') {
                                icBlocks.push({ idx, value: (c as any).value.trim() });
                            } else if (typeof (c as vscode.MarkdownString).value === 'string') {
                                const raw = (c as vscode.MarkdownString).value;
                                const m = raw.match(/```ic10\n([\s\S]*?)```/i);
                                if (m) icBlocks.push({ idx, value: m[1].trim() });
                            }
                        }
                    });
                        if (icBlocks.length > 1) {
                            // Prefer the most descriptive (longest) signature; remove exact duplicates
                            const longest = icBlocks.reduce((a, b) => (b.value.length > a.value.length ? b : a));
                            const seenValues = new Set<string>();
                            const filtered: any[] = [];
                            asArray.forEach((c, idx) => {
                                const ic = icBlocks.find(b => b.idx === idx);
                                if (ic) {
                                    if (idx !== longest.idx) return; // keep only longest signature block
                                    if (seenValues.has(ic.value)) return; // remove duplicates
                                    seenValues.add(ic.value);
                                }
                                filtered.push(c);
                            });
                            return new vscode.Hover(filtered as any, hover.range ?? range);
                        }
                }

                if (!example) {
                    return hover; // No fallback enrichment available
                }

                // Convert existing contents to MarkdownString safely, preserving code blocks and text.
                const toMarkdown = (c: any): vscode.MarkdownString => {
                    if (typeof c === 'string') {
                        return new vscode.MarkdownString(c);
                    }
                    // vscode.MarkdownString
                    if (c && typeof (c as vscode.MarkdownString).value === 'string') {
                        return c as vscode.MarkdownString;
                    }
                    // MarkedString { language, value }
                    if (c && typeof c === 'object' && 'language' in c && 'value' in c) {
                        const lang = (c as { language: string }).language || '';
                        const val = (c as { value: string }).value || '';
                        return new vscode.MarkdownString('```' + lang + '\n' + val + '\n```');
                    }
                    return new vscode.MarkdownString('');
                };

                const newContents: (vscode.MarkdownString | string)[] = [];
                const head = new vscode.MarkdownString('```ic10\n' + example + '\n```');
                head.isTrusted = true;
                newContents.push(head);

                for (const c of asArray) {
                    // If overriding an incomplete existing ic10 block, skip that block to avoid duplicates
                    if (overridingIncomplete) {
                        if (typeof c === 'string' && /```ic10/i.test(c)) {
                            continue;
                        }
                        if (c && typeof c === 'object' && 'language' in c && (c as any).language && ((c as any).language as string).toLowerCase() === 'ic10') {
                            continue;
                        }
                    }
                    const md = toMarkdown(c);
                    if (md.value && md.value.trim().length > 0) {
                        newContents.push(md);
                    }
                }

                return new vscode.Hover(newContents, hover.range ?? range);
            },
            handleDiagnostics: (uri: vscode.Uri, diagnostics: vscode.Diagnostic[], next: (uri: vscode.Uri, diagnostics: vscode.Diagnostic[]) => void) => {
                const enabled = vscode.workspace.getConfiguration().get('ic10.diagnostics.enabled') as boolean;
                if (!enabled) {
                    // Suppress diagnostics when disabled
                    next(uri, []);
                    return;
                }
                next(uri, diagnostics);
            }
            ,
            // Ensure opcode completion inserts only the mnemonic (plus a space), preventing any ghost-signature text
            provideCompletionItem: async (
                document: vscode.TextDocument,
                position: vscode.Position,
                context: vscode.CompletionContext,
                token: vscode.CancellationToken,
                next: any
            ) => {
                const result = await next(document, position, context, token);
                const normalize = (item: vscode.CompletionItem): vscode.CompletionItem => {
                    // Only adjust likely opcodes (function kind and simple word labels)
                    const isWord = typeof item.label === 'string' && /^[a-z][a-z0-9]*$/.test(item.label as string);
                    if (item.kind === vscode.CompletionItemKind.Function && isWord) {
                        item.insertText = (item.label as string) + ' ';
                        item.textEdit = undefined; // avoid replacing ranges with signatures
                        item.additionalTextEdits = undefined;
                        item.command = undefined; // avoid auto-triggering snippets/actions
                        item.filterText = item.label as string;
                        item.detail = item.detail; // keep detail visible in UI
                    }
                    return item;
                };
                if (!result) return result;
                if (Array.isArray(result)) {
                    return result.map(normalize);
                }
                if ('items' in result && Array.isArray(result.items)) {
                    result.items = result.items.map(normalize);
                    return result;
                }
                return result;
            }
        }
    };

    // Create the language client and start the client.
    const lc = new LanguageClient(
        'ic10',
        'IC10 Language Server',
        serverOptions,
        clientOptions
    );

    let clientRegisteredWithContext = false;
    let clientRunning = false;
    let stopInFlight: Promise<void> | undefined;
    let pendingConfigPayload: { settings: any } | undefined;
    let pendingDiagnosticsState: boolean | undefined;

    const sendConfigPayload = (payload: { settings: any }) => {
        lc.sendNotification(DidChangeConfigurationNotification.type, payload).catch((err: unknown) => {
            console.error('Failed to sync IC10 configuration with the language server', err);
            pendingConfigPayload = payload;
        });
    };

    const sendDiagnosticsState = (enabled: boolean) => {
        const options: ExecuteCommandParams = {
            command: 'setDiagnostics',
            arguments: [enabled]
        };
        lc.sendRequest('workspace/executeCommand', options).catch((err: unknown) => {
            console.error('Failed to push diagnostics state to the language server', err);
            pendingDiagnosticsState = enabled;
        });
    };

    const flushPendingServerState = () => {
        if (pendingConfigPayload) {
            const payload = pendingConfigPayload;
            pendingConfigPayload = undefined;
            sendConfigPayload(payload);
        }
        if (pendingDiagnosticsState !== undefined) {
            const enabled = pendingDiagnosticsState;
            pendingDiagnosticsState = undefined;
            sendDiagnosticsState(enabled);
        }
    };

    const scheduleConfigSync = () => {
        const payload = { settings: getLSPIC10Configurations() };
        if (!clientRunning) {
            pendingConfigPayload = payload;
            return;
        }
        sendConfigPayload(payload);
    };

    const scheduleDiagnosticsSync = (enabled: boolean) => {
        pendingDiagnosticsState = enabled;
        if (!clientRunning) {
            return;
        }
        sendDiagnosticsState(enabled);
    };

    const startClient = async () => {
        if (!clientRegisteredWithContext) {
            context.subscriptions.push(lc);
            clientRegisteredWithContext = true;
        }
        try {
            await lc.start();
            clientRunning = true;
            // Give server a moment to fully initialize before sending commands
            setTimeout(() => flushPendingServerState(), 100);
        } catch (err) {
            vscode.window.showErrorMessage(`IC10 Language Server failed to start: ${err instanceof Error ? err.message : String(err)}`);
        }
    };

    const stopClient = async () => {
        if (stopInFlight) {
            return stopInFlight;
        }
        if (!clientRunning) {
            return;
        }
        clientRunning = false;
        stopInFlight = lc
            .stop()
            .catch((err: unknown) => {
                vscode.window.showErrorMessage(`Failed to stop IC10 Language Server: ${err instanceof Error ? err.message : String(err)}`);
                throw err;
            })
            .finally(() => {
                stopInFlight = undefined;
            });
        return stopInFlight;
    };

    const restartClient = async () => {
        await stopClient();
        await startClient();
    };

    const initialDiagnosticsEnabled = (vscode.workspace.getConfiguration().get('ic10.diagnostics.enabled') as boolean | undefined) ?? true;
    scheduleConfigSync();
    scheduleDiagnosticsSync(initialDiagnosticsEnabled);
    void startClient();

    // Register configuration changes to keep the server in sync.
    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration((e: vscode.ConfigurationChangeEvent) => {
            if (e.affectsConfiguration('ic10.lsp')) {
                scheduleConfigSync();
            }
            if (e.affectsConfiguration('ic10.diagnostics.enabled')) {
                const diag = (vscode.workspace.getConfiguration().get('ic10.diagnostics.enabled') as boolean | undefined) ?? true;
                scheduleDiagnosticsSync(diag);
            }
        })
    );

    // Dynamic example extraction removed; using static examples only.

    // Register commands
    context.subscriptions.push(vscode.commands.registerCommand('ic10.lsp.restart', async () => {
        vscode.window.showInformationMessage('Restarting IC10 Language Server...');
        await restartClient();
    }));

    // Register ic10.lsp.version command
    context.subscriptions.push(vscode.commands.registerCommand('ic10.lsp.version', () => {
        // ExecuteCommandOptions
        const options: ExecuteCommandParams = {
            command: 'version',
            arguments: []
        };

        lc.sendRequest('workspace/executeCommand', options);
    }   ));

    // Register ic10.showRelated command
    context.subscriptions.push(vscode.commands.registerCommand('ic10.showRelated', async (instruction?: string) => {
        // If instruction not provided, try to get current word at cursor
        if (!instruction) {
            const editor = vscode.window.activeTextEditor;
            if (!editor) {
                vscode.window.showInformationMessage('No active editor found');
                return;
            }
            
            const position = editor.selection.active;
            const range = editor.document.getWordRangeAtPosition(position);
            if (!range) {
                vscode.window.showInformationMessage('No instruction found at cursor');
                return;
            }
            
            instruction = editor.document.getText(range);
        }

        // Map of instruction to related instructions (simplified version for demo)
        const relatedInstructions: { [key: string]: string[] } = {
            'add': ['sub', 'mul', 'div', 'mod'],
            'sub': ['add', 'mul', 'div', 'mod'],
            'mul': ['add', 'sub', 'div', 'mod'],
            'div': ['add', 'sub', 'mul', 'mod'],
            'l': ['s', 'lb', 'sb', 'lr', 'ls', 'ld', 'sd'],
            's': ['l', 'lb', 'sb', 'lr', 'ls', 'ld', 'sd'],
            'beq': ['bne', 'blt', 'bgt', 'ble', 'bge', 'breq', 'beqz'],
            'bne': ['beq', 'blt', 'bgt', 'ble', 'bge', 'brne', 'bnez']
        };

    const related = instruction ? relatedInstructions[instruction.toLowerCase()] : undefined;
        if (!related || related.length === 0) {
            vscode.window.showInformationMessage(`No related instructions found for '${instruction}'`);
            return;
        }

        const selectedInstruction = await vscode.window.showQuickPick(
            related.map(instr => ({
                label: instr,
                description: `Related to ${instruction}`
            })),
            {
                placeHolder: `Instructions related to ${instruction}`,
                canPickMany: false
            }
        );

        if (selectedInstruction) {
            // Insert the selected instruction at cursor
            const editor = vscode.window.activeTextEditor;
            if (editor) {
                const position = editor.selection.active;
                editor.edit((editBuilder: vscode.TextEditorEdit) => {
                    editBuilder.insert(position, selectedInstruction.label);
                });
            }
        }
    }));

    // Inlay hints for instruction signatures (game-style inline guidance)
    if (vscode.workspace.getConfiguration().get('ic10.inlayHints.enabled')) {
        const signatureMap: Record<string,string> = {
            'move': 'r? a(r?|num)',
            'add': 'r? a(r?|num) b(r?|num)',
            'sub': 'r? a(r?|num) b(r?|num)',
            'mul': 'r? a(r?|num) b(r?|num)',
            'div': 'r? a(r?|num) b(r?|num)',
            'mod': 'r? a(r?|num) b(r?|num)',
            'l': 'r? device(d?|r?|id) logicType',
            's': 'device(d?|r?|id) logicType r?',
            'ls': 'r? device(d?|r?|id) slotIndex logicSlotType',
            'lr': 'r? device(d?|r?|id) reagentMode int',
            'lb': 'r? deviceHash logicType batchMode',
            'lbn': 'r? deviceHash nameHash logicType batchMode',
            'lbns': 'r? deviceHash nameHash slotIndex logicSlotType batchMode',
            'lbs': 'r? deviceHash slotIndex logicSlotType batchMode',
            'sb': 'deviceHash logicType r?',
            'sbn': 'deviceHash nameHash logicType r?',
            'sbs': 'deviceHash slotIndex logicSlotType r?'
        };

        // Derive signatures for control-flow opcodes without enumerating all variants.
        const computeSignature = (opcodeLower: string): string | undefined => {
            // Known directly
            if (signatureMap[opcodeLower]) return signatureMap[opcodeLower];
            // Jumps
            if (opcodeLower === 'j' || opcodeLower === 'jal') return 'label(r?|num)';
            // Device status branches (bdse, bdns, brdse, brdns, bdseal, bdnsal)
            if (opcodeLower === 'bdse' || opcodeLower === 'bdns' || 
                opcodeLower === 'brdse' || opcodeLower === 'brdns' ||
                opcodeLower === 'bdseal' || opcodeLower === 'bdnsal') {
                return 'device(d?|r?|id) label(r?|num)';
            }
            // Branch family: default is three operands (a, b, label); *z variants use implicit zero (a, label)
            if (opcodeLower.startsWith('b')) {
                // Check for *zal variants first (beqzal, bnezal, etc.) - they're 2 operands like *z
                const twoOp = opcodeLower.endsWith('zal') || opcodeLower.endsWith('z');
                return twoOp ? 'a(r?|num) label(r?|num)' : 'a(r?|num) b(r?|num) label(r?|num)';
            }
            return undefined;
        };
            // Show dynamic inline hints for remaining operands as you type.
            context.subscriptions.push(vscode.languages.registerInlayHintsProvider({ language: 'ic10', scheme: 'file' }, {
                provideInlayHints(document: vscode.TextDocument, range: vscode.Range): vscode.InlayHint[] {
                    const hints: vscode.InlayHint[] = [];
                    for (let line = range.start.line; line <= range.end.line; line++) {
                        const text = document.lineAt(line).text;
                        const m = text.match(/^\s*([a-zA-Z][a-zA-Z0-9]*)\b(.*)$/);
                        if (!m) continue;
                        
                        // Skip labels (word followed by colon)
                        if (m[2].trimStart().startsWith(':')) continue;
                        
                        const opcode = m[1].toLowerCase();
                        const sig = signatureMap[opcode] ?? computeSignature(opcode);
                        if (!sig) continue;
                        // Truncate at comment
                        let after = m[2];
                        const commentIdx = after.indexOf('#');
                        const beforeComment = commentIdx >= 0 ? after.substring(0, commentIdx) : after;
                        // Tokenize what the user has already typed
                        const typedTokens = beforeComment.trim().length === 0 ? [] : beforeComment.trim().split(/\s+/);
                        const parts = sig.split(/\s+/);

                        // Let the LSP show the very first suffix after the opcode when nothing typed yet to avoid duplicate hints
                        if (typedTokens.length === 0) {
                            continue;
                        }

                        // Remove the slot currently being edited and show only the remaining ones to the right
                        const remaining = parts.slice(typedTokens.length);
                        if (remaining.length === 0) continue;

                        // Anchor the hint immediately after what the user has typed (before any comment)
                        const opcodeEnd = text.indexOf(m[1]) + m[1].length;
                        const typedSpan = beforeComment; // includes any spaces the user typed
                        const anchorCol = opcodeEnd + typedSpan.length;
                        const pos = new vscode.Position(line, Math.max(anchorCol, opcodeEnd));
                        // Emit one short hint per remaining token to avoid UI truncation of a long single hint
                        for (const token of remaining) {
                            const hint = new vscode.InlayHint(pos, ' ' + token, vscode.InlayHintKind.Parameter);
                            hints.push(hint);
                        }
                    }
                    return hints;
                }
            }));
    }

    // Register ic10.searchCategory command
    context.subscriptions.push(vscode.commands.registerCommand('ic10.searchCategory', async (category?: string) => {
        // Map of categories to instructions
        const categories: { [key: string]: string[] } = {
            'Arithmetic': ['add', 'sub', 'mul', 'div', 'mod', 'abs', 'sqrt'],
            'Device I/O': ['l', 's', 'lr', 'ls', 'ld', 'sd', 'ss'],
            'Batch Operations': ['lb', 'sb', 'lbn', 'lbs', 'sbn', 'sbs'],
            'Branching': ['beq', 'bne', 'blt', 'bgt', 'ble', 'bge', 'beqz', 'bnez'],
            'Control Flow': ['j', 'jr', 'jal'],
            'Comparison': ['slt', 'sgt', 'sle', 'sge', 'seq', 'sne'],
            'Logic': ['and', 'or', 'xor', 'nor']
        };

        if (!category) {
            // Show category picker first
            const selectedCategory = await vscode.window.showQuickPick(
                Object.keys(categories).map(cat => ({
                    label: cat,
                    description: `${categories[cat].length} instructions`
                })),
                {
                    placeHolder: 'Select instruction category',
                    canPickMany: false
                }
            );

            if (!selectedCategory) {
                return;
            }
            category = selectedCategory.label;
        }

    const instructions = category ? categories[category] : undefined;
        if (!instructions || instructions.length === 0) {
            vscode.window.showInformationMessage(`No instructions found in category '${category}'`);
            return;
        }

        const selectedInstruction = await vscode.window.showQuickPick(
            instructions.map((instr: string) => ({
                label: instr,
                description: `${category} instruction`
            })),
            {
                placeHolder: `${category} instructions`,
                canPickMany: false
            }
        );

        if (selectedInstruction) {
            // Insert the selected instruction at cursor
            const editor = vscode.window.activeTextEditor;
            if (editor) {
                const position = editor.selection.active;
                editor.edit((editBuilder: vscode.TextEditorEdit) => {
                    editBuilder.insert(position, selectedInstruction.label);
                });
            }
        }
    }));

    // Register ic10.showExamples command
    context.subscriptions.push(vscode.commands.registerCommand('ic10.showExamples', async (instruction?: string) => {
        // If instruction not provided, try to get current word at cursor
        if (!instruction) {
            const editor = vscode.window.activeTextEditor;
            if (!editor) {
                vscode.window.showInformationMessage('No active editor found');
                return;
            }
            
            const position = editor.selection.active;
            const range = editor.document.getWordRangeAtPosition(position);
            if (!range) {
                vscode.window.showInformationMessage('No instruction found at cursor');
                return;
            }
            
            instruction = editor.document.getText(range);
        }

        // Show examples in an information message (could be enhanced to use a webview)
    const examples = getInstructionExamples(instruction!);
        if (examples.length === 0) {
            vscode.window.showInformationMessage(`No examples found for instruction '${instruction}'`);
            return;
        }

        // For now, show examples in a simple information dialog
        // In a more advanced implementation, this could show in a dedicated panel
        const exampleText = examples.join('\n');
        vscode.window.showInformationMessage(
            `Examples for ${instruction}:\n\n${exampleText}`,
            { modal: false }
        );
    }));

    // Toggle diagnostics on/off (force refresh by restarting client to clear stale squiggles immediately)
    context.subscriptions.push(vscode.commands.registerCommand('ic10.toggleDiagnostics', async () => {
        const configuration = vscode.workspace.getConfiguration();
        const current = configuration.get('ic10.diagnostics.enabled') as boolean | undefined;
        const nextVal = !(current ?? true);
        await configuration.update('ic10.diagnostics.enabled', nextVal, vscode.ConfigurationTarget.Workspace);
        scheduleDiagnosticsSync(nextVal);
        // Actively clear client-side squiggles when disabling to ensure immediate visual feedback
        if (!nextVal) {
            const collectionName = (lc as any)?._clientOptions?.diagnosticCollectionName;
            const diagCollection = collectionName
                ? vscode.languages.createDiagnosticCollection(collectionName)
                : vscode.languages.createDiagnosticCollection();
            for (const doc of vscode.workspace.textDocuments) {
                if (doc.languageId === 'ic10') {
                    diagCollection.set(doc.uri, []);
                }
            }
            diagCollection.dispose();
        } else {
            // Force a re-validation when diagnostics are re-enabled.
            scheduleConfigSync();
        }
        await restartClient();
        vscode.window.showInformationMessage(`IC10 diagnostics ${nextVal ? 'enabled' : 'disabled'} (client + server sync).`);
    }));

    // Suppress all register diagnostics by adding @ignore directive
    context.subscriptions.push(vscode.commands.registerCommand('ic10.suppressAllRegisterDiagnostics', async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor || editor.document.languageId !== 'ic10') {
            vscode.window.showInformationMessage('No active IC10 file');
            return;
        }

        const uri = editor.document.uri.toString();
        const options: ExecuteCommandParams = {
            command: 'ic10.suppressAllRegisterDiagnostics',
            arguments: [uri]
        };

        try {
            await lc.sendRequest('workspace/executeCommand', options);
            vscode.window.showInformationMessage('Added ignore directive for all register diagnostics');
        } catch (err) {
            vscode.window.showErrorMessage(`Failed to suppress register diagnostics: ${err instanceof Error ? err.message : String(err)}`);
        }
    }));

    // Toggle hash-related diagnostics
    context.subscriptions.push(vscode.commands.registerCommand('ic10.suppressHashDiagnostics', async () => {
        console.log('[IC10] suppressHashDiagnostics command invoked');
        const config = vscode.workspace.getConfiguration('ic10.lsp');
        const currentValue = config.get<boolean>('suppressHashDiagnostics', false);
        const newValue = !currentValue;
        
        console.log('[IC10] Current value:', currentValue, '-> New value:', newValue);
        console.log('[IC10] Client running:', clientRunning);
        
        // Update the config setting
        await config.update('suppressHashDiagnostics', newValue, vscode.ConfigurationTarget.Global);
        console.log('[IC10] Config updated');
        
        // Send custom command to LSP to update the setting and refresh diagnostics
        if (clientRunning && lc) {
            try {
                console.log('[IC10] Sending command to LSP server...');
                const options: ExecuteCommandParams = {
                    command: 'ic10.setHashDiagnostics',
                    arguments: [newValue]
                };
                const result = await lc.sendRequest('workspace/executeCommand', options);
                console.log('[IC10] LSP server response:', result);
                
                vscode.window.showInformationMessage(
                    `HASH() warnings ${newValue ? 'suppressed' : 'enabled'}`
                );
            } catch (error) {
                console.error('[IC10] Error sending command to server:', error);
                vscode.window.showErrorMessage(`Failed to update hash diagnostics: ${error}`);
            }
        } else {
            console.log('[IC10] Client not running (clientRunning=' + clientRunning + ')');
            vscode.window.showInformationMessage(
                `HASH() warnings ${newValue ? 'suppressed' : 'enabled'} (restart required)`
            );
        }
    }));

    // Add #IgnoreRegisterWarnings directive to suppress register warnings
    context.subscriptions.push(vscode.commands.registerCommand('ic10.ignoreRegisterWarnings', async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor || editor.document.languageId !== 'ic10') {
            vscode.window.showInformationMessage('No active IC10 file');
            return;
        }

        const document = editor.document;
        const text = document.getText();
        
        // Check if directive already exists
        const directiveRegex = /#IgnoreRegisterWarnings/i;
        if (directiveRegex.test(text)) {
            vscode.window.showInformationMessage('#IgnoreRegisterWarnings already present');
            return;
        }

        // Find the best position to insert the directive (after other directives at the top)
        const lines = text.split('\n');
        let insertLine = 0;
        
        // Skip shebang and find last directive/comment at the top
        for (let i = 0; i < lines.length; i++) {
            const trimmed = lines[i].trim();
            if (trimmed.startsWith('#') || trimmed === '') {
                insertLine = i + 1;
            } else {
                break; // Stop at first non-comment/non-empty line
            }
        }

        const position = new vscode.Position(insertLine, 0);
        const directive = '#IgnoreRegisterWarnings\n';
        
        await editor.edit(editBuilder => {
            editBuilder.insert(position, directive);
        });

        vscode.window.showInformationMessage('Added #IgnoreRegisterWarnings directive');
    }));

    // Toggle between Stationeers theme and user's previous theme
    context.subscriptions.push(vscode.commands.registerCommand('ic10.toggleStationeersTheme', async () => {
        const config = vscode.workspace.getConfiguration();
        const currentTheme = config.get<string>('workbench.colorTheme');
        const stationeersTheme = 'Stationeers Full Color Theme';
        
        // Get or set the stored previous theme
        const previousTheme = context.globalState.get<string>('ic10.previousTheme');
        
        if (currentTheme === stationeersTheme) {
            // Switch back to previous theme (or default if none stored)
            const targetTheme = previousTheme || 'Dark+ (default dark)';
            await config.update('workbench.colorTheme', targetTheme, vscode.ConfigurationTarget.Global);
            vscode.window.showInformationMessage(`Switched to ${targetTheme}`);
        } else {
            // Store current theme and switch to Stationeers
            await context.globalState.update('ic10.previousTheme', currentTheme);
            await config.update('workbench.colorTheme', stationeersTheme, vscode.ConfigurationTarget.Global);
            vscode.window.showInformationMessage('Switched to Stationeers Full Color Theme');
        }
    }));

    // Command to reset theme prompt state (for testing)
    context.subscriptions.push(vscode.commands.registerCommand('ic10.resetThemePrompt', async () => {
        await context.globalState.update('ic10.hasAskedAboutTheme', undefined);
        vscode.window.showInformationMessage('Theme prompt state reset. Reload window to see the prompt again.');
    }));

    // ============================================================================
    // Branch Visualization Feature
    // ============================================================================
    
    let branchVisualizationActive = false;
    const branchDecorations: vscode.TextEditorDecorationType[] = [];
    let svgTempDir: string | undefined;
    
    // Create SVG icons for branch visualization
    function createBranchSVGIcons() {
        if (!svgTempDir) {
            svgTempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ic10-branch-'));
        }
        
        const icons: { [key: string]: string } = {};
        
        branchColors.forEach((color, index) => {
            const rgbColor = color.border.replace('rgba(', 'rgb(').replace(/, 0\.[0-9]+\)/, ')');
            
            // Vertical line
            const lineSVG = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="20" viewBox="0 0 16 20">
                <line x1="8" y1="0" x2="8" y2="20" stroke="${rgbColor}" stroke-width="2"/>
            </svg>`;
            const linePath = path.join(svgTempDir, `line-${index}.svg`);
            fs.writeFileSync(linePath, lineSVG);
            icons[`line-${index}`] = linePath;
            
            // Up arrow
            const upArrowSVG = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="20" viewBox="0 0 16 20">
                <line x1="8" y1="4" x2="8" y2="20" stroke="${rgbColor}" stroke-width="2"/>
                <polygon points="8,2 4,6 12,6" fill="${rgbColor}"/>
            </svg>`;
            const upArrowPath = path.join(svgTempDir, `up-arrow-${index}.svg`);
            fs.writeFileSync(upArrowPath, upArrowSVG);
            icons[`up-arrow-${index}`] = upArrowPath;
            
            // Down arrow
            const downArrowSVG = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="20" viewBox="0 0 16 20">
                <line x1="8" y1="0" x2="8" y2="16" stroke="${rgbColor}" stroke-width="2"/>
                <polygon points="8,18 4,14 12,14" fill="${rgbColor}"/>
            </svg>`;
            const downArrowPath = path.join(svgTempDir, `down-arrow-${index}.svg`);
            fs.writeFileSync(downArrowPath, downArrowSVG);
            icons[`down-arrow-${index}`] = downArrowPath;
            
            // Dot (target)
            const dotSVG = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="20" viewBox="0 0 16 20">
                <line x1="8" y1="0" x2="8" y2="20" stroke="${rgbColor}" stroke-width="2"/>
                <circle cx="8" cy="10" r="4" fill="${rgbColor}"/>
            </svg>`;
            const dotPath = path.join(svgTempDir, `dot-${index}.svg`);
            fs.writeFileSync(dotPath, dotSVG);
            icons[`dot-${index}`] = dotPath;
        });
        
        return icons;
    }
    
    // Store the webview panel for branch visualization
    let branchVisualizationPanel: vscode.WebviewPanel | undefined;
    
    // Color palette for matching branch pairs
    const branchColors = [
        { bg: 'rgba(255, 215, 0, 0.15)', border: 'rgba(255, 215, 0, 0.5)' },    // Gold
        { bg: 'rgba(138, 43, 226, 0.15)', border: 'rgba(138, 43, 226, 0.5)' },  // Purple
        { bg: 'rgba(0, 191, 255, 0.15)', border: 'rgba(0, 191, 255, 0.5)' },    // Sky Blue
        { bg: 'rgba(255, 105, 180, 0.15)', border: 'rgba(255, 105, 180, 0.5)' }, // Hot Pink
        { bg: 'rgba(50, 205, 50, 0.15)', border: 'rgba(50, 205, 50, 0.5)' },    // Lime Green
        { bg: 'rgba(255, 140, 0, 0.15)', border: 'rgba(255, 140, 0, 0.5)' },    // Dark Orange
    ];
    
    /**
     * Identifies relative branch instructions (jr, br*, bnan, brnan)
     */
    const relativeBranchOpcodes = new Set([
        'jr', 'brdse', 'brdns', 'brlt', 'brgt', 'brle', 'brge', 
        'breq', 'brne', 'brap', 'brna', 'brltz', 'brgez', 'brlez', 
        'brgtz', 'breqz', 'brnez', 'brapz', 'brnaz', 'brnan', 'brnaz'
    ]);
    
    /**
     * Parse a line to extract relative branch information
     * Returns: { opcode: string, offset: number, targetLine: number } or null
     */
    function parseRelativeBranch(lineText: string, currentLine: number): { opcode: string, offset: number, targetLine: number } | null {
        // Match instruction pattern: opcode [operands...] offset
        const match = lineText.match(/^\s*([a-z]+)\s+(.+)$/i);
        if (!match) return null;
        
        const opcode = match[1].toLowerCase();
        if (!relativeBranchOpcodes.has(opcode)) return null;
        
        const operandsStr = match[2].trim();
        
        // Extract the last operand (the offset) - skip comments
        const beforeComment = operandsStr.split('#')[0].trim();
        const operands = beforeComment.split(/\s+/);
        
        if (operands.length === 0) return null;
        
        const offsetStr = operands[operands.length - 1];
        const offset = parseInt(offsetStr, 10);
        
        if (isNaN(offset)) return null;
        
        // Calculate target line (current line + offset, 0-indexed)
        const targetLine = currentLine + offset;
        
        return { opcode, offset, targetLine };
    }
    
    /**
     * Create transparent webview overlay for branch visualization
     */
    function createBranchVisualizationWebview(
        editor: vscode.TextEditor,
        branches: Array<{ sourceLine: number, targetLine: number, offset: number, opcode: string }>,
        branchDepths: Map<number, number>,
        maxDepth: number,
        branchColors: Array<{ bg: string, border: string }>,
        lineToColors: Map<number, number[]>
    ) {
        // Close existing panel if any
        if (branchVisualizationPanel) {
            branchVisualizationPanel.dispose();
        }
        
        // Create webview panel - positioned beside editor
        branchVisualizationPanel = vscode.window.createWebviewPanel(
            'branchVisualization',
            'Branch Flow',
            { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                enableFindWidget: false
            }
        );
        
        // Get editor metrics for positioning
        const document = editor.document;
        const fontSize = vscode.workspace.getConfiguration('editor').get<number>('fontSize', 14);
        const lineHeight = fontSize * 1.5; // Approximate line height
        
        // Build visualization data
        const visualizationData = branches.map((branch, idx) => {
            const depth = branchDepths.get(idx) || 0;
            const minLine = Math.min(branch.sourceLine, branch.targetLine);
            const maxLine = Math.max(branch.sourceLine, branch.targetLine);
            const isUpward = branch.offset < 0;
            const color = branchColors[depth % branchColors.length];
            
            return {
                branchIndex: idx,
                depth,
                sourceLine: branch.sourceLine,
                targetLine: branch.targetLine,
                minLine,
                maxLine,
                isUpward,
                color: color.border,
                bgColor: color.bg
            };
        });
        
        // Generate HTML for webview
        branchVisualizationPanel.webview.html = getBranchVisualizationHTML(
            visualizationData,
            lineHeight,
            fontSize,
            maxDepth,
            document.lineCount
        );
        
        // Handle panel disposal
        branchVisualizationPanel.onDidDispose(() => {
            branchVisualizationPanel = undefined;
        });
    }
    
    /**
     * Generate HTML for branch visualization webview
     */
    function getBranchVisualizationHTML(
        branches: Array<{
            branchIndex: number,
            depth: number,
            sourceLine: number,
            targetLine: number,
            minLine: number,
            maxLine: number,
            isUpward: boolean,
            color: string,
            bgColor: string
        }>,
        lineHeight: number,
        fontSize: number,
        maxDepth: number,
        lineCount: number
    ): string {
        // Calculate dimensions
        const depthWidth = 30; // pixels per depth level
        const startX = 50; // left margin
        const totalHeight = lineCount * lineHeight;
        const totalWidth = startX + (maxDepth + 1) * depthWidth + 50;
        
        // Build SVG paths for each branch
        const svgPaths = branches.map(branch => {
            const x = startX + branch.depth * depthWidth;
            const y1 = branch.sourceLine * lineHeight + lineHeight / 2;
            const y2 = branch.targetLine * lineHeight + lineHeight / 2;
            
            // Arrow pointing LEFT (into the code)
            const arrowSize = 8;
            const arrowX = x - 5; // Position arrow slightly before the line end
            const arrowPoints = `${arrowX},${y2} ${arrowX + arrowSize},${y2 - arrowSize} ${arrowX + arrowSize},${y2 + arrowSize}`;
            
            // Draw corner indicators at source
            const horizontalLineLength = 15;
            let cornerPath = '';
            
            if (branch.isUpward) {
                // Bottom-right corner for upward branch
                cornerPath = `M ${x} ${y1} L ${x + horizontalLineLength} ${y1}`;
            } else {
                // Top-right corner for downward branch
                cornerPath = `M ${x} ${y1} L ${x + horizontalLineLength} ${y1}`;
            }
            
            return `
                <!-- Branch ${branch.branchIndex} (depth ${branch.depth}) -->
                <!-- Main vertical line -->
                <line x1="${x}" y1="${y1}" x2="${x}" y2="${y2}" 
                      stroke="${branch.color}" stroke-width="2.5" opacity="0.9"
                      stroke-linecap="round"/>
                
                <!-- Corner indicator at source -->
                <path d="${cornerPath}" 
                      stroke="${branch.color}" stroke-width="2.5" 
                      fill="none" stroke-linecap="round" opacity="0.9"/>
                
                <!-- Arrow head at target pointing LEFT -->
                <polygon points="${arrowPoints}" 
                         fill="${branch.color}" opacity="0.9"/>
            `;
        }).join('\n');
        
        return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        html, body {
            width: 100%;
            height: 100%;
            overflow: hidden;
            background: #1e1e1e;
            font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
        }
        #container {
            width: 100%;
            height: 100%;
            position: relative;
            overflow-y: auto;
            overflow-x: hidden;
        }
        svg {
            display: block;
            min-height: 100%;
        }
    </style>
</head>
<body>
    <div id="container">
        <svg width="${totalWidth}" height="${totalHeight}" xmlns="http://www.w3.org/2000/svg">
            ${svgPaths}
        </svg>
    </div>
</body>
</html>`;
    }
    
    /**
     * Apply branch visualization to the active editor
     */
    function applyBranchVisualization(editor: vscode.TextEditor) {
        // Clear existing decorations - UPDATED
        clearBranchDecorations();
        
        const document = editor.document;
        const branches: Array<{ sourceLine: number, targetLine: number, offset: number, opcode: string }> = [];
        
        // Find all relative branches in the document
        for (let line = 0; line < document.lineCount; line++) {
            const lineText = document.lineAt(line).text;
            const branchInfo = parseRelativeBranch(lineText, line);
            
            if (branchInfo && branchInfo.targetLine >= 0 && branchInfo.targetLine < document.lineCount) {
                branches.push({
                    sourceLine: line,
                    targetLine: branchInfo.targetLine,
                    offset: branchInfo.offset,
                    opcode: branchInfo.opcode
                });
            }
        }
        
        if (branches.length === 0) {
            vscode.window.showInformationMessage('No relative branch instructions found in this file');
            return;
        }
        
        // Build a map of line -> [branch colors] for lines referenced by multiple branches
        const lineToColors = new Map<number, number[]>();
        const branchIndexToTarget = new Map<number, number>(); // Track which branch index points to which target line
        const ghostTextMap = new Map<number, string>();
        
        branches.forEach((branch, branchIndex) => {
            // Add color ONLY to source and target lines (not middle lines)
            if (!lineToColors.has(branch.sourceLine)) {
                lineToColors.set(branch.sourceLine, []);
            }
            lineToColors.get(branch.sourceLine)!.push(branchIndex);
            
            if (!lineToColors.has(branch.targetLine)) {
                lineToColors.set(branch.targetLine, []);
            }
            lineToColors.get(branch.targetLine)!.push(branchIndex);
            branchIndexToTarget.set(branchIndex, branch.targetLine); // Map branch index to its target line
            
            // Store ghost text for source line (inline hint showing target)
            const targetLineText = document.lineAt(branch.targetLine).text.trim();
            const targetPreview = targetLineText.length > 50 
                ? targetLineText.substring(0, 50) + '...' 
                : targetLineText;
            const direction = branch.offset < 0 ? '⇑' : '⇓';
            ghostTextMap.set(branch.sourceLine, ` ${direction} line ${branch.targetLine + 1}: ${targetPreview}`);
        });
        
        // Calculate gutter line positions to avoid overlaps
        // IMPORTANT: Shorter spans get drawn further LEFT (depth 0), longer spans further RIGHT
        const sortedBranches = branches
            .map((branch, index) => ({ ...branch, index, span: Math.abs(branch.targetLine - branch.sourceLine) }))
            .sort((a, b) => a.span - b.span); // Ascending: shortest first (for right-side display)
        
        // Assign depth (horizontal position) to each branch
        const branchDepths = new Map<number, number>(); // branchIndex -> depth
        const occupiedRanges: Array<{ minLine: number, maxLine: number, depth: number, branchIndex: number }> = [];
        
        sortedBranches.forEach(branch => {
            const minLine = Math.min(branch.sourceLine, branch.targetLine);
            const maxLine = Math.max(branch.sourceLine, branch.targetLine);
            
            // Find the first depth level where this branch doesn't conflict
            // Two branches conflict if they share ANY line
            let depth = 0;
            let conflict = true;
            
            while (conflict) {
                // Check if ANY line of this branch overlaps with ANY line of another branch at this depth
                conflict = occupiedRanges.some(range => {
                    if (range.depth !== depth) return false;
                    // Check if ranges overlap (they conflict if they share ANY line)
                    return !(maxLine < range.minLine || minLine > range.maxLine);
                });
                
                if (conflict) {
                    depth++;
                }
            }
            
            branchDepths.set(branch.index, depth);
            occupiedRanges.push({ minLine, maxLine, depth, branchIndex: branch.index });
        });
        
        // Calculate max depth to determine indentation needed
        const maxDepth = Math.max(...Array.from(branchDepths.values()), 0);
        const indentPerDepth = 2; // characters per depth level
        const totalIndent = (maxDepth + 1) * indentPerDepth;
        
        // Debug: Log branch assignments
        console.log(`[Branch Viz] Found ${branches.length} branches, maxDepth=${maxDepth}, totalIndent=${totalIndent}`);
        branches.forEach((branch, idx) => {
            const depth = branchDepths.get(idx) || 0;
            console.log(`  Branch ${idx}: line ${branch.sourceLine+1} → ${branch.targetLine+1} (offset=${branch.offset}, depth=${depth})`);
        });
        
        // Find the range of lines that need spacing (all lines within any branch)
        let minBranchLine = document.lineCount;
        let maxBranchLine = 0;
        branches.forEach(branch => {
            const minLine = Math.min(branch.sourceLine, branch.targetLine);
            const maxLine = Math.max(branch.sourceLine, branch.targetLine);
            minBranchLine = Math.min(minBranchLine, minLine);
            maxBranchLine = Math.max(maxBranchLine, maxLine);
        });
        
        // Track which lines have arrows/dots
        const linesWithIndicators = new Set<number>();
        branches.forEach(branch => {
            linesWithIndicators.add(branch.sourceLine);
            linesWithIndicators.add(branch.targetLine);
        });
        
        // Add spacing to ALL lines within the branch range for consistent alignment
        const spacerWidth = '  '; // 2 characters to match arrow/dot decorations
        for (let lineNum = minBranchLine; lineNum <= maxBranchLine; lineNum++) {
            if (!linesWithIndicators.has(lineNum)) {
                // Add invisible spacing to lines without indicators
                const spacerDecoration = vscode.window.createTextEditorDecorationType({
                    before: {
                        contentText: spacerWidth,
                        color: 'transparent'
                    }
                });
                
                branchDecorations.push(spacerDecoration);
                editor.setDecorations(spacerDecoration, [
                    new vscode.Range(lineNum, 0, lineNum, 0)
                ]);
            }
        }
        
        // Simple approach: arrow at source, dot at target
        // All decorations use same width for consistent alignment
        
        branches.forEach((branch, branchIndex) => {
            // Use branchIndex for color (not depth) - colors match the highlight colors
            const color = branchColors[branchIndex % branchColors.length];
            const isUpward = branch.offset < 0;
            
            // Arrow at source line (in before decoration) - using double-line arrows
            const arrowChar = isUpward ? '⇑' : '⇓';
            const sourceDecoration = vscode.window.createTextEditorDecorationType({
                before: {
                    contentText: arrowChar + ' ',
                    color: color.border.replace('0.5', '1.0'),
                    fontWeight: 'bold'
                }
            });
            
            branchDecorations.push(sourceDecoration);
            editor.setDecorations(sourceDecoration, [
                new vscode.Range(branch.sourceLine, 0, branch.sourceLine, 0)
            ]);
            
            // Dot at target line (in before decoration)
            const dotDecoration = vscode.window.createTextEditorDecorationType({
                before: {
                    contentText: '● ',
                    color: color.border.replace('0.5', '1.0'),
                    fontWeight: 'bold'
                }
            });
            
            branchDecorations.push(dotDecoration);
            editor.setDecorations(dotDecoration, [
                new vscode.Range(branch.targetLine, 0, branch.targetLine, 0)
            ]);
        });
        
        // Add ghost text (inline hints) at the end of branch source lines
        ghostTextMap.forEach((ghostText, lineNum) => {
            const lineLength = document.lineAt(lineNum).text.length;
            
            const ghostDecoration = vscode.window.createTextEditorDecorationType({
                after: {
                    contentText: ghostText,
                    color: 'rgba(128, 128, 128, 0.6)',
                    fontStyle: 'italic'
                }
            });
            
            branchDecorations.push(ghostDecoration);
            editor.setDecorations(ghostDecoration, [
                new vscode.Range(lineNum, lineLength, lineNum, lineLength)
            ]);
        });
        
        // For each line, create segments based on how many colors it has
        // Only highlight the actual instruction code
        lineToColors.forEach((colorIndices, lineNum) => {
            const lineText = document.lineAt(lineNum).text;
            const lineLength = lineText.length;
            
            // Debug: Log highlighting info
            console.log(`  Line ${lineNum+1}: ${colorIndices.length} colors [${colorIndices.join(',')}]`);
            
            // Find where actual code starts (first non-whitespace after indentation)
            const firstNonWhitespace = lineText.search(/\S/);
            if (firstNonWhitespace === -1) return; // Skip empty lines
            
            // Start highlighting from where code begins
            const highlightStart = firstNonWhitespace;
            
            if (colorIndices.length === 1) {
                // Single color - highlight entire instruction
                const colorIndex = colorIndices[0];
                const color = branchColors[colorIndex % branchColors.length];
                
                // Check if THIS specific branch has this line as its target
                const isTargetForThisBranch = branchIndexToTarget.get(colorIndex) === lineNum;
                const bgColor = isTargetForThisBranch 
                    ? color.bg.replace('0.15', '0.45') // Much darker for targets
                    : color.bg;
                
                const decorationType = vscode.window.createTextEditorDecorationType({
                    backgroundColor: bgColor,
                    borderWidth: '1px',
                    borderStyle: 'solid',
                    borderColor: color.border
                });
                
                branchDecorations.push(decorationType);
                const range = new vscode.Range(lineNum, highlightStart, lineNum, lineLength);
                editor.setDecorations(decorationType, [range]);
            } else {
                // Multiple colors - split the code portion into equal segments
                const codeLength = lineLength - highlightStart;
                const segmentWidth = codeLength / colorIndices.length;
                
                colorIndices.forEach((colorIndex, segmentIndex) => {
                    const color = branchColors[colorIndex % branchColors.length];
                    
                    // Check if THIS specific branch has this line as its target
                    const isTargetForThisBranch = branchIndexToTarget.get(colorIndex) === lineNum;
                    const bgColor = isTargetForThisBranch 
                        ? color.bg.replace('0.15', '0.45') // Much darker for targets
                        : color.bg;
                    
                    const startCol = highlightStart + Math.floor(segmentIndex * segmentWidth);
                    const endCol = segmentIndex === colorIndices.length - 1 
                        ? lineLength
                        : highlightStart + Math.floor((segmentIndex + 1) * segmentWidth);
                    
                    const decorationType = vscode.window.createTextEditorDecorationType({
                        backgroundColor: bgColor,
                        borderWidth: '1px',
                        borderStyle: 'solid',
                        borderColor: color.border
                    });
                    
                    branchDecorations.push(decorationType);
                    const range = new vscode.Range(lineNum, startCol, lineNum, endCol);
                    editor.setDecorations(decorationType, [range]);
                });
            }
        });
        
        vscode.window.showInformationMessage(`Branch visualization active (${branches.length} branch${branches.length === 1 ? '' : 'es'} found)`);
    }
    
    /**
     * Clear all branch decorations
     */
    function clearBranchDecorations() {
        branchDecorations.forEach(decoration => decoration.dispose());
        branchDecorations.length = 0;
        
        // Clean up temp SVG files
        if (svgTempDir && fs.existsSync(svgTempDir)) {
            try {
                const files = fs.readdirSync(svgTempDir);
                files.forEach(file => {
                    fs.unlinkSync(path.join(svgTempDir!, file));
                });
                fs.rmdirSync(svgTempDir);
                svgTempDir = undefined;
            } catch (err) {
                console.error('Failed to clean up SVG temp files:', err);
            }
        }
    }
    
    /**
     * Toggle branch visualization on/off
     */
    context.subscriptions.push(vscode.commands.registerCommand('ic10.toggleBranchVisualization', () => {
        const editor = vscode.window.activeTextEditor;
        
        if (!editor || editor.document.languageId !== 'ic10') {
            vscode.window.showInformationMessage('Open an IC10 file to visualize branches');
            return;
        }
        
        branchVisualizationActive = !branchVisualizationActive;
        
        if (branchVisualizationActive) {
            applyBranchVisualization(editor);
        } else {
            clearBranchDecorations();
            vscode.window.showInformationMessage('Branch visualization disabled');
        }
    }));
    
    // Clear decorations when switching files or closing editor
    context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(() => {
        if (branchVisualizationActive) {
            clearBranchDecorations();
            branchVisualizationActive = false;
        }
    }));
    
    // Clear decorations on deactivation
    context.subscriptions.push({
        dispose: () => clearBranchDecorations()
    });

}

// This method is called when your extension is deactivated
export function deactivate() { }
