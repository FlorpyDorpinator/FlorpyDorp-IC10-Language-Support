/**
 * Finding mods' instruction files: the game's modconfig.xml, the catalogue, ic10-lint.json and
 * reading them. Plain mocha, no VS Code: `npm run test-unit`.
 */
import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import {
    clientInstructions,
    enabledModFolders,
    findGameFolder,
    hintTokens,
    jumpOperand,
    lintConfigInstructionFiles,
    modInstructionFiles,
    parseRegistryValue,
    protonPath,
    readCatalogue,
    readInstructionFiles,
} from '../../instructionSources';

const instructionsJson = (names: string[], extra: object = {}) =>
    JSON.stringify({ ...extra, instructions: Object.fromEntries(names.map((n) => [n, { operands: [] }])) });

/** Write `files` (path relative to a new temporary folder → contents) and return the folder. */
function folderWith(files: Record<string, string>): string {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ic10-sources-'));
    for (const [rel, text] of Object.entries(files)) {
        const file = path.join(root, rel);
        fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, text);
    }
    return root;
}

const modconfig = (mods: { kind: 'Workshop' | 'Local'; enabled: boolean; path: string }[]) =>
    `<?xml version="1.0" encoding="utf-8"?>
<ModConfig>
  <Core Enabled="true">
    <Path />
  </Core>
${mods
    .map((m) => `  <${m.kind} Enabled="${m.enabled}">\n    <Path Value="${m.path}" />\n  </${m.kind}>`)
    .join('\n')}
</ModConfig>`;

describe('the game\'s mods', () => {
    it('reads the enabled mods\' folders from modconfig.xml', () => {
        const xml = modconfig([
            { kind: 'Workshop', enabled: true, path: 'C:\\Steam\\workshop\\1' },
            { kind: 'Local', enabled: false, path: 'C:\\mods\\Off' },
            { kind: 'Local', enabled: true, path: 'C:\\mods\\A &amp; B' },
        ]);
        assert.deepStrictEqual(enabledModFolders(xml), ['C:\\Steam\\workshop\\1', 'C:\\mods\\A & B']);
    });

    it('uses a mod\'s own file, else the catalogue\'s for its ModID, and skips disabled mods', () => {
        const root = folderWith({
            'mods/Own/ic10-instructions.json': instructionsJson(['own']),
            'mods/InAbout/About/ic10-instructions.json': instructionsJson(['about']),
            'mods/Catalogued/About/About.xml': '<ModMetadata><ModID> author.catalogued </ModID></ModMetadata>',
            'mods/Plain/About/About.xml': '<ModMetadata><ModID>author.plain</ModID></ModMetadata>',
            'mods/Off/ic10-instructions.json': instructionsJson(['off']),
            'catalogue/catalogued.json': instructionsJson(['cat'], { modId: 'author.catalogued' }),
            'catalogue/README.md': 'not json',
        });
        const mod = (name: string, enabled = true) => ({ kind: 'Local' as const, enabled, path: path.join(root, 'mods', name) });
        fs.writeFileSync(
            path.join(root, 'modconfig.xml'),
            modconfig([mod('Own'), mod('InAbout'), mod('Catalogued'), mod('Plain'), mod('Off', false)]),
        );
        const catalogue = readCatalogue(path.join(root, 'catalogue'));
        assert.deepStrictEqual([...catalogue.keys()], ['author.catalogued']);
        const files = modInstructionFiles({ folder: root, localPath: (p) => p }, catalogue);
        assert.deepStrictEqual(
            files.map((f) => path.relative(root, f)),
            [
                path.join('mods', 'Own', 'ic10-instructions.json'),
                path.join('mods', 'InAbout', 'About', 'ic10-instructions.json'),
                path.join('catalogue', 'catalogued.json'),
            ],
        );
    });

    it('finds the game\'s folder from the registry\'s Documents on Windows, falling back to the home folder', async () => {
        const docs = folderWith({ 'My Games/Stationeers/modconfig.xml': modconfig([]) });
        const home = folderWith({ 'Documents/My Games/Stationeers/modconfig.xml': modconfig([]) });
        const fromRegistry = await findGameFolder({ platform: 'win32', home, documentsFromRegistry: async () => docs });
        assert.strictEqual(fromRegistry?.folder, path.join(docs, 'My Games', 'Stationeers'));
        const fallback = await findGameFolder({ platform: 'win32', home, documentsFromRegistry: async () => undefined });
        assert.strictEqual(fallback?.folder, path.join(home, 'Documents', 'My Games', 'Stationeers'));
        const setting = await findGameFolder({ platform: 'win32', home, override: '/somewhere' });
        assert.strictEqual(setting?.folder, '/somewhere');
        assert.strictEqual(await findGameFolder({ platform: 'darwin', home }), undefined);
    });

    it('reads the registry\'s Documents value, expanding variables', () => {
        const output = '\r\nHKEY_CURRENT_USER\\...\\User Shell Folders\r\n    Personal    REG_EXPAND_SZ    %USERPROFILE%\\Documents\r\n\r\n';
        assert.strictEqual(parseRegistryValue(output, { USERPROFILE: 'C:\\Users\\me' }), 'C:\\Users\\me\\Documents');
        assert.strictEqual(parseRegistryValue('nothing here'), undefined);
    });

    it('maps Proton\'s Windows paths to Linux ones', () => {
        const driveC = '/home/me/.steam/steam/steamapps/compatdata/544550/pfx/drive_c';
        assert.strictEqual(protonPath('Z:\\home\\me\\mods\\X', driveC), '/home/me/mods/X');
        assert.strictEqual(protonPath('C:\\users\\steamuser\\Documents\\X', driveC), `${driveC}/users/steamuser/Documents/X`);
    });
});

describe('ic10-lint.json', () => {
    it('finds a package\'s file through "ic10Instructions", from the config\'s folder up', () => {
        const root = folderWith({
            'node_modules/@x/ops/package.json': JSON.stringify({ name: '@x/ops', ic10Instructions: './data/ops.json' }),
            'node_modules/@x/ops/data/ops.json': instructionsJson(['xop']),
            'node_modules/@x/devices/package.json': JSON.stringify({ name: '@x/devices' }),
            'project/local/package.json': JSON.stringify({ ic10Instructions: 'local.json' }),
            'project/local/index.ts': '',
            'project/local/local.json': instructionsJson(['lop']),
            'project/direct.json': instructionsJson(['dop']),
            'project/ic10-lint.json': JSON.stringify({
                extensions: ['@x/ops', '@x/devices', './local/index.ts', './direct.json', '@x/missing'],
            }),
        });
        const found = lintConfigInstructionFiles(path.join(root, 'project', 'ic10-lint.json'));
        assert.deepStrictEqual(
            found.files.map((f) => path.relative(root, f)),
            [
                path.join('node_modules', '@x', 'ops', 'data', 'ops.json'),
                path.join('project', 'local', 'local.json'),
                path.join('project', 'direct.json'),
            ],
        );
        assert.strictEqual(found.warnings.length, 1);
        assert.match(found.warnings[0], /@x\/missing/);
    });

    it('is quiet about a config without extensions', () => {
        const root = folderWith({ 'ic10-lint.json': JSON.stringify({ rules: {} }) });
        assert.deepStrictEqual(lintConfigInstructionFiles(path.join(root, 'ic10-lint.json')), { files: [], warnings: [] });
    });
});

describe('reading the files', () => {
    it('reads each file once, warns about missing ones, and the first file to name an instruction wins', () => {
        const root = folderWith({
            'a.json': JSON.stringify({
                instructions: {
                    fcall: {
                        operands: [
                            { name: 'dest', type: 'r?', role: 'write' },
                            { name: 'target', type: 'r?|int', role: 'jump' },
                            { name: 'a', type: 'r?|num', optional: true },
                        ],
                        flow: 'call',
                    },
                    fret: { operands: [{ name: 'value', type: 'r?|num', optional: true }], flow: 'return' },
                },
            }),
            'b.json': instructionsJson(['fcall', 'other']),
        });
        const a = path.join(root, 'a.json');
        const b = path.join(root, 'b.json');
        const read = readInstructionFiles([a, b, a, path.join(root, 'missing.json')]);
        assert.deepStrictEqual(read.files.map((f) => f.source), [a, b]);
        assert.strictEqual(read.warnings.length, 1);

        const instructions = clientInstructions(read.files);
        assert.deepStrictEqual([...instructions.keys()].sort(), ['fcall', 'fret', 'other']);
        const fcall = instructions.get('fcall')!;
        assert.strictEqual(fcall.source, a);
        assert.deepStrictEqual(hintTokens(fcall), ['dest(r?)', 'target(r?|int)', 'a(r?|num)']);
        assert.strictEqual(jumpOperand(fcall), 1);
        assert.strictEqual(jumpOperand(instructions.get('fret')!), undefined);
    });
});
