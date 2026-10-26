/**
 * Instructions from mods: finds the `ic10-instructions.json` files (instructionSources.ts says
 * where), keeps them up to date as mods are enabled or files change, and hands them to the language
 * server (as the `extraInstructions` setting) and to the extension's own inline hints and branch
 * arrows.
 */
import * as vscode from 'vscode';
import * as os from 'os';
import * as path from 'path';
import {
    ClientInstruction,
    InstructionFile,
    clientInstructions,
    findGameFolder,
    lintConfigInstructionFiles,
    modInstructionFiles,
    readCatalogue,
    readInstructionFiles,
} from './instructionSources';

export class ExtraInstructions implements vscode.Disposable {
    /** The files, as the language server takes them. */
    files: InstructionFile[] = [];
    /** Their instructions, for the extension's own features. */
    instructions = new Map<string, ClientInstruction>();

    private readonly changed = new vscode.EventEmitter<void>();
    /** Fires when the files (or what's in them) change. */
    readonly onDidChange = this.changed.event;

    private readonly output = vscode.window.createOutputChannel('IC10 Mod Instructions');
    private watchers: vscode.Disposable[] = [];
    private refreshTimer: NodeJS.Timeout | undefined;
    private lastWarnings = '';
    private lastFiles = '';

    constructor(private readonly context: vscode.ExtensionContext) {
        // No recursive watcher for ic10-lint.json: refresh() watches each one it finds, and each
        // workspace folder's top level for a new one, file by file.
        this.context.subscriptions.push(
            vscode.workspace.onDidChangeWorkspaceFolders(() => this.scheduleRefresh()),
            vscode.workspace.onDidChangeConfiguration((e) => {
                if (e.affectsConfiguration('ic10.lsp.instructionFiles') || e.affectsConfiguration('ic10.stationeersFolder')) {
                    this.scheduleRefresh();
                }
            }),
        );
    }

    /** Find and read the files again; fires onDidChange if anything differs. */
    async refresh(): Promise<void> {
        const warnings: string[] = [];
        const paths: string[] = [];
        const watched = new Set<string>();

        // 1. The game's enabled mods.
        const override = vscode.workspace.getConfiguration('ic10').get<string>('stationeersFolder')?.trim();
        const game = await findGameFolder({
            platform: process.platform,
            home: os.homedir(),
            override: override ? this.resolve(override) : undefined,
        });
        if (game) {
            watched.add(path.join(game.folder, 'modconfig.xml'));
            paths.push(...modInstructionFiles(game, readCatalogue(this.context.asAbsolutePath('mods'))));
        }

        // 2. The workspace's ic10-lint.json files. Each found is watched, and so is each workspace
        // folder's top level, where a new one usually goes; a new one deeper down is found on the
        // next refresh.
        for (const folder of vscode.workspace.workspaceFolders ?? []) {
            watched.add(path.join(folder.uri.fsPath, 'ic10-lint.json'));
        }
        for (const config of await vscode.workspace.findFiles('**/ic10-lint.json', '**/node_modules/**')) {
            watched.add(config.fsPath);
            const found = lintConfigInstructionFiles(config.fsPath);
            paths.push(...found.files);
            warnings.push(...found.warnings);
        }

        // 3. The setting.
        const fromSetting = vscode.workspace.getConfiguration('ic10.lsp').get<string[]>('instructionFiles') ?? [];
        paths.push(...fromSetting.filter((p) => typeof p === 'string' && p.trim()).map((p) => this.resolve(p.trim())));

        const read = readInstructionFiles(paths);
        warnings.push(...read.warnings);
        for (const file of read.files) watched.add(file.source);
        this.watch(watched);

        const filesKey = JSON.stringify(read.files);
        if (filesKey !== this.lastFiles) {
            this.lastFiles = filesKey;
            this.files = read.files;
            this.instructions = clientInstructions(read.files);
            this.log(game?.folder);
            this.changed.fire();
        }
        this.warn(warnings);
    }

    dispose(): void {
        this.watchers.forEach((w) => w.dispose());
        this.changed.dispose();
        this.output.dispose();
        if (this.refreshTimer) clearTimeout(this.refreshTimer);
    }

    /** Refresh soon: the game rewrites modconfig.xml, and editors save, in several steps. */
    private scheduleRefresh(): void {
        if (this.refreshTimer) clearTimeout(this.refreshTimer);
        this.refreshTimer = setTimeout(() => void this.refresh(), 300);
    }

    /**
     * Watch each file (modconfig.xml, ic10-lint.json files and the instruction files), wherever it
     * is, with a non-recursive watcher on its folder, so it's seen when it's created, too.
     */
    private watch(files: Set<string>): void {
        this.watchers.forEach((w) => w.dispose());
        this.watchers = [];
        for (const file of files) {
            const watcher = vscode.workspace.createFileSystemWatcher(
                new vscode.RelativePattern(vscode.Uri.file(path.dirname(file)), path.basename(file)),
            );
            watcher.onDidCreate(() => this.scheduleRefresh());
            watcher.onDidChange(() => this.scheduleRefresh());
            watcher.onDidDelete(() => this.scheduleRefresh());
            this.watchers.push(watcher);
        }
    }

    private resolve(p: string): string {
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '';
        return p
            .replace(/\$\{workspaceFolder\}/gi, workspaceFolder)
            .replace(/\$\{userHome\}/gi, os.homedir())
            .replace(/^~(?=[\\/]|$)/, os.homedir());
    }

    private log(gameFolder: string | undefined): void {
        this.output.appendLine(`[${new Date().toLocaleTimeString()}] Game folder: ${gameFolder ?? 'not found'}`);
        if (this.instructions.size === 0) {
            this.output.appendLine('  No instructions from mods.');
        }
        for (const instruction of this.instructions.values()) {
            this.output.appendLine(`  ${instruction.name}: ${instruction.source}`);
        }
    }

    /** Warnings go to the output; a new set also shows once as a message. */
    private warn(warnings: string[]): void {
        const key = warnings.join('\n');
        if (key === this.lastWarnings) return;
        this.lastWarnings = key;
        if (warnings.length === 0) return;
        warnings.forEach((w) => this.output.appendLine(`  Warning: ${w}`));
        void vscode.window
            .showWarningMessage(`IC10: ${warnings[0]}${warnings.length > 1 ? ` (and ${warnings.length - 1} more)` : ''}`, 'Show')
            .then((choice) => {
                if (choice) this.output.show(true);
            });
    }
}
