/**
 * Where instructions that aren't the game's own come from: `ic10-instructions.json` files.
 *
 * - The game's enabled mods: modconfig.xml (in Documents\My Games\Stationeers) lists every mod's
 *   folder and whether it's enabled. A mod ships `ic10-instructions.json` in its folder (or its
 *   About folder); for mods that don't, this extension's `mods/` catalogue has files matched by the
 *   `<ModID>` in the mod's About/About.xml.
 * - `ic10-lint.json` files in the workspace: each of their `"extensions"` names an npm package (or a
 *   path) whose package.json gives the file in `"ic10Instructions"`.
 * - The `ic10.lsp.instructionFiles` setting.
 *
 * No VS Code API here, so it can be tested on its own; extraInstructions.ts does the rest.
 */
import * as fs from 'fs';
import * as path from 'path';
import { execFile } from 'child_process';

/** One file, as the language server takes it. */
export interface InstructionFile {
    source: string;
    text: string;
}

export const INSTRUCTION_FILE = 'ic10-instructions.json';
const STATIONEERS_APP_ID = '544550';

// ----------------------------------------------------------------------------
// The game's folder and its mods
// ----------------------------------------------------------------------------

/** The `My Games\Stationeers` folder and how to read the Windows paths in its modconfig.xml. */
export interface GameFolder {
    folder: string;
    /** Turns a path from modconfig.xml into one on this machine (Proton's prefix on Linux). */
    localPath: (windowsPath: string) => string;
}

export interface FindGameFolderOptions {
    platform: NodeJS.Platform;
    home: string;
    /** The `ic10.stationeersFolder` setting, if set. */
    override?: string;
    /** Windows: the Documents folder from the registry (injectable for tests). */
    documentsFromRegistry?: () => Promise<string | undefined>;
}

/** Where the game keeps modconfig.xml, if it's anywhere we know of. */
export async function findGameFolder(options: FindGameFolderOptions): Promise<GameFolder | undefined> {
    const { platform, home, override } = options;
    if (override) {
        return { folder: override, localPath: (p) => p };
    }
    if (platform === 'win32') {
        const fromRegistry = await (options.documentsFromRegistry ?? documentsFromRegistry)();
        for (const documents of [fromRegistry, path.join(home, 'Documents')]) {
            if (!documents) continue;
            const folder = path.join(documents, 'My Games', 'Stationeers');
            if (fs.existsSync(path.join(folder, 'modconfig.xml'))) {
                return { folder, localPath: (p) => p };
            }
        }
        return undefined;
    }
    if (platform === 'linux') {
        // The game runs under Proton: its Documents folder is inside the prefix.
        for (const steam of [path.join(home, '.steam', 'steam'), path.join(home, '.local', 'share', 'Steam')]) {
            const driveC = path.join(steam, 'steamapps', 'compatdata', STATIONEERS_APP_ID, 'pfx', 'drive_c');
            const folder = path.join(driveC, 'users', 'steamuser', 'Documents', 'My Games', 'Stationeers');
            if (fs.existsSync(path.join(folder, 'modconfig.xml'))) {
                return { folder, localPath: (p) => protonPath(p, driveC) };
            }
        }
    }
    return undefined;
}

/** A Windows path inside Proton's prefix as a Linux one: `C:\` is the prefix's drive_c, `Z:\` is `/`. */
export function protonPath(windowsPath: string, driveC: string): string {
    const m = windowsPath.match(/^([A-Za-z]):[\\/](.*)$/);
    if (!m) return windowsPath;
    const rest = m[2].split(/[\\/]+/).filter(Boolean);
    const drive = m[1].toUpperCase();
    if (drive === 'Z') return '/' + rest.join('/');
    if (drive === 'C') return path.posix.join(driveC, ...rest);
    return windowsPath;
}

/** The Documents folder Windows uses (it can be moved, e.g. into OneDrive), from the registry. */
function documentsFromRegistry(): Promise<string | undefined> {
    return new Promise((resolve) => {
        execFile(
            'reg',
            ['query', 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\User Shell Folders', '/v', 'Personal'],
            { windowsHide: true, timeout: 5000 },
            (err, stdout) => resolve(err ? undefined : parseRegistryValue(stdout)),
        );
    });
}

/** The value in `reg query` output, with `%VARIABLES%` expanded. */
export function parseRegistryValue(output: string, env: NodeJS.ProcessEnv = process.env): string | undefined {
    const m = output.match(/REG_(?:EXPAND_)?SZ\s+(.+?)\s*$/m);
    if (!m) return undefined;
    return m[1].replace(/%([^%]+)%/g, (whole, name: string) => env[name] ?? env[name.toUpperCase()] ?? whole);
}

/** The folders of the mods modconfig.xml has enabled. */
export function enabledModFolders(modconfigXml: string): string[] {
    const folders: string[] = [];
    const entry = /<(?:Workshop|Local)\s+Enabled="(true|false)"\s*>([\s\S]*?)<\/(?:Workshop|Local)>/g;
    for (const m of modconfigXml.matchAll(entry)) {
        if (m[1] !== 'true') continue;
        const pathValue = m[2].match(/<Path\s+Value="([^"]*)"/);
        if (pathValue && pathValue[1]) folders.push(unescapeXml(pathValue[1]));
    }
    return folders;
}

function unescapeXml(text: string): string {
    return text
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&lt;/g, '<')
        .replace(/&gt;/g, '>')
        .replace(/&amp;/g, '&');
}

/** A mod's `<ModID>`, from its About/About.xml. */
export function modId(modFolder: string): string | undefined {
    try {
        const about = fs.readFileSync(path.join(modFolder, 'About', 'About.xml'), 'utf8');
        return about.match(/<ModID>\s*([^<]*?)\s*<\/ModID>/)?.[1];
    } catch {
        return undefined;
    }
}

/** The catalogue's files by the `modId` each names. */
export function readCatalogue(folder: string): Map<string, string> {
    const byModId = new Map<string, string>();
    let names: string[] = [];
    try {
        names = fs.readdirSync(folder).filter((n) => n.endsWith('.json'));
    } catch {
        return byModId;
    }
    for (const name of names) {
        const file = path.join(folder, name);
        try {
            const id = JSON.parse(fs.readFileSync(file, 'utf8')).modId;
            if (typeof id === 'string' && id) byModId.set(id, file);
        } catch {
            // A broken catalogue file is reported when it's used, like any other.
        }
    }
    return byModId;
}

/** The instruction files of the game's enabled mods: each mod's own, else the catalogue's. */
export function modInstructionFiles(game: GameFolder, catalogue: Map<string, string>): string[] {
    let xml: string;
    try {
        xml = fs.readFileSync(path.join(game.folder, 'modconfig.xml'), 'utf8');
    } catch {
        return [];
    }
    const files: string[] = [];
    for (const windowsPath of enabledModFolders(xml)) {
        const folder = game.localPath(windowsPath);
        const own = [path.join(folder, INSTRUCTION_FILE), path.join(folder, 'About', INSTRUCTION_FILE)].find((f) =>
            fs.existsSync(f),
        );
        if (own) {
            files.push(own);
            continue;
        }
        const id = modId(folder);
        const fromCatalogue = id ? catalogue.get(id) : undefined;
        if (fromCatalogue) files.push(fromCatalogue);
    }
    return files;
}

// ----------------------------------------------------------------------------
// ic10-lint.json
// ----------------------------------------------------------------------------

/**
 * The instruction files an `ic10-lint.json` names through its `"extensions"`, found as Node would
 * find the packages from the config's folder. Extensions without one (a package that only adds
 * devices, say) add nothing.
 */
export function lintConfigInstructionFiles(configFile: string): { files: string[]; warnings: string[] } {
    const files: string[] = [];
    const warnings: string[] = [];
    let extensions: unknown;
    try {
        extensions = JSON.parse(fs.readFileSync(configFile, 'utf8')).extensions;
    } catch (e) {
        return { files, warnings: [`${configFile}: ${e instanceof Error ? e.message : String(e)}`] };
    }
    if (!Array.isArray(extensions)) return { files, warnings };
    const root = path.dirname(configFile);
    for (const spec of extensions) {
        if (typeof spec !== 'string') continue;
        const isPath = path.isAbsolute(spec) || spec.startsWith('./') || spec.startsWith('../');
        const file = isPath ? fromPath(path.resolve(root, spec)) : fromPackage(root, spec);
        if (file === null) {
            warnings.push(`${configFile}: extension "${spec}" not found (is it installed?)`);
        } else if (file) {
            files.push(file);
        }
    }
    return { files, warnings };
}

/** A package's instruction file: undefined if it has none, null if the package isn't there. */
function fromPackage(root: string, name: string): string | undefined | null {
    for (let dir = root; ; dir = path.dirname(dir)) {
        const pkg = path.join(dir, 'node_modules', name, 'package.json');
        if (fs.existsSync(pkg)) return instructionsOfPackage(pkg);
        if (path.dirname(dir) === dir) return null;
    }
}

/** A path's instruction file: itself if it's one, else its package's (the package.json at or above it). */
function fromPath(target: string): string | undefined | null {
    if (!fs.existsSync(target)) return null;
    if (target.endsWith('.json')) return target;
    let dir = fs.statSync(target).isDirectory() ? target : path.dirname(target);
    for (;;) {
        const pkg = path.join(dir, 'package.json');
        if (fs.existsSync(pkg)) return instructionsOfPackage(pkg);
        if (path.dirname(dir) === dir) return undefined;
        dir = path.dirname(dir);
    }
}

function instructionsOfPackage(packageJson: string): string | undefined {
    try {
        const field = JSON.parse(fs.readFileSync(packageJson, 'utf8')).ic10Instructions;
        return typeof field === 'string' ? path.resolve(path.dirname(packageJson), field) : undefined;
    } catch {
        return undefined;
    }
}

// ----------------------------------------------------------------------------
// Reading the files
// ----------------------------------------------------------------------------

/** Each file's text, in order, once each; a file that can't be read is a warning. */
export function readInstructionFiles(paths: string[]): { files: InstructionFile[]; warnings: string[] } {
    const files: InstructionFile[] = [];
    const warnings: string[] = [];
    const seen = new Set<string>();
    for (const p of paths) {
        const key = path.resolve(p).toLowerCase();
        if (seen.has(key)) continue;
        seen.add(key);
        try {
            files.push({ source: p, text: fs.readFileSync(p, 'utf8') });
        } catch (e) {
            warnings.push(`${p}: ${e instanceof Error ? e.message : String(e)}`);
        }
    }
    return { files, warnings };
}

/** What the extension's own features (inline hints, branch arrows) need about an instruction. */
export interface ClientInstruction {
    name: string;
    operands: { name: string; type: string; role?: string; optional?: boolean; into?: string }[];
    flow?: 'jump' | 'branch' | 'call' | 'return' | 'halt';
    source: string;
}

/**
 * The files' instructions, first file wins. A light reading: the language server checks the files
 * properly and reports what's wrong with them.
 */
export function clientInstructions(files: InstructionFile[]): Map<string, ClientInstruction> {
    const byName = new Map<string, ClientInstruction>();
    for (const file of files) {
        let parsed: any;
        try {
            parsed = JSON.parse(file.text);
        } catch {
            continue;
        }
        const instructions = parsed?.instructions;
        if (!instructions || typeof instructions !== 'object') continue;
        for (const [name, entry] of Object.entries<any>(instructions)) {
            if (byName.has(name) || !entry || typeof entry !== 'object') continue;
            const operands = Array.isArray(entry.operands)
                ? entry.operands.filter((o: any) => o && typeof o.name === 'string' && typeof o.type === 'string')
                : [];
            byName.set(name, { name, operands, flow: entry.flow, source: file.source });
        }
    }
    return byName;
}

/** An instruction's operands in the game's hint style: `dest(r?) target(r?|int) a(r?|num)`. */
export function hintTokens(instruction: ClientInstruction): string[] {
    return instruction.operands.map((o) => (o.name === o.type ? o.type : `${o.name}(${o.type})`));
}

/** The operand an instruction jumps to, if it jumps, branches or calls. */
export function jumpOperand(instruction: ClientInstruction): number | undefined {
    if (instruction.flow !== 'jump' && instruction.flow !== 'branch' && instruction.flow !== 'call') return undefined;
    const i = instruction.operands.findIndex((o) => o.role === 'jump');
    return i >= 0 ? i : undefined;
}
