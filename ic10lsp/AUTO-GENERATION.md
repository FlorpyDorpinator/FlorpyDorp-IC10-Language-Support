# Auto-Generation System (developer notes)

The IC10 LSP generates **all** of its game knowledge — logic types, enums, instruction
signatures + docs, device hashes/names, and device descriptions — from four Stationeers game
files at build time. Replace the files, `cargo build`, and the extension is up to date with no
hand-maintained tables.

> This is the implementation-level companion to **[`/data/README.md`](../data/README.md)**,
> which is the canonical overview + update procedure. If the two ever disagree, `data/README.md`
> wins.

## The four source files

All live in the repo-root **`data/`** folder (not `data/game-sources/`, which now only holds
an `Old Versions/` archive):

| File | Origin | Drives |
|------|--------|--------|
| `Enums.json` | `StreamingAssets/Data/Enums.json` | logic/slot/batch/reagent types + docs; every enum (incl. `GasType`) |
| `Stationpedia.json` | `StreamingAssets/Language/english/Stationpedia.json` | device name↔hash; instruction docs **and signatures** |
| `english.xml` | `StreamingAssets/Language/english.xml` | device display names + descriptions |
| `ProgrammableChip.cs` | decompiled `Assembly-CSharp.dll` | `INSTRUCTION_SIGNATURES` reference (cross-check only) |

## Two build-time generators read these files

1. **`ic10lsp/build.rs`** — emits Rust into `OUT_DIR`, `include!`d by `src/`:

   ```
   data/Enums.json          -> enums_generated.rs, instructions_generated.rs
   data/Stationpedia.json   -> device_hashes_generated.rs, instructions_generated.rs
   data/english.xml         -> device_hashes_generated.rs, descriptions_generated.rs
   data/ProgrammableChip.cs -> instructions_generated.rs (reference signatures)
   ```

2. **`tree-sitter-ic10/generate-grammar.js`** — reads `Enums.json`, `Stationpedia.json`, and
   `ProgrammableChip.cs` to bake the instruction + logic-type/constant tokens into `grammar.js`,
   **and** syncs the TextMate grammar's instruction + logic-type keyword lists in
   `FlorpyDorp Language Support/syntaxes/ic10.tmLanguage.json` (so static syntax highlighting
   stays in sync too). Run via `npm run generate` in `tree-sitter-ic10/` — the `generate: grammar`
   build task.

## What `build.rs` generates

| Constant | From | File |
|----------|------|------|
| `LOGIC_TYPES` / `LOGIC_TYPE_DOCS` | Enums.json `LogicType` | instructions_generated.rs |
| `SLOT_LOGIC_TYPES` / `SLOT_TYPE_DOCS` | Enums.json `LogicSlotType` | instructions_generated.rs |
| `BATCH_MODES` / `BATCH_MODE_DOCS` | Enums.json `LogicBatchMethod` | instructions_generated.rs |
| `REAGENT_MODES` / `REAGENT_MODE_DOCS` | Enums.json `LogicReagentMode` | instructions_generated.rs |
| `ENUM_VALUE_BY_NAME`, `ENUM_DESC_BY_NAME`, `ENUM_DEPRECATED`, `LOGIC_TYPE_NAME_TO_VALUE` | every Enums.json family | enums_generated.rs |
| `INSTRUCTION_DOCS` | Stationpedia.json `scriptCommands[].desc` | instructions_generated.rs |
| `INSTRUCTIONS` (type-checking signatures) | Stationpedia.json `scriptCommands[].example` + override table | instructions_generated.rs |
| `INSTRUCTION_SIGNATURES` (reference) | ProgrammableChip.cs `GetCommandExample` | instructions_generated.rs |
| `DEVICE_NAME_TO_HASH` / `HASH_TO_DISPLAY_NAME` | Stationpedia.json `pages` + english.xml names | device_hashes_generated.rs |
| `DEVICE_DESCRIPTIONS` | english.xml | descriptions_generated.rs |

Deprecated enum members are skipped automatically. Counts track the game data — as of the
current files: ~330 logic types, 33 slot types, 5 batch modes, 154 instructions, ~1,788 devices.

### How instruction signatures are generated

`INSTRUCTIONS` is parsed from each instruction's color-coded `scriptCommands[].example`
(blue = Register, teal = Number, green = Device, white = Name, orange = the enum named in the
label). IC10 semantics are applied (numeric operands also accept a register; mode operands also
accept a number), which makes **144/154** parse exactly right. A small `signature_override`
table in `build.rs` covers the ~10 cases where real validation is broader/narrower than the
literal example. **New instructions added by a game update get a signature automatically** —
the test `instructions::test::matching_instructions` enforces every instruction has both a
signature and a doc. `ProgrammableChip.cs` is retained only as a secondary cross-reference.

## Updating for a new game version

See [`/data/README.md`](../data/README.md) for the full procedure. Short version:

```bash
# 1. (optional) archive the current data/ files under data/game-sources/Old Versions/<patch>/
# 2. drop the 4 fresh files into data/
# 3. rebuild — both generators run from the VS Code task "build: ic10 lsp + ext", or manually:
cd tree-sitter-ic10 && npm run generate          # regenerate grammar tokens
cd ../ic10lsp && cargo build                     # regenerate Rust + compile LSP
# 4. copy the binary into the extension (the build task does this for you):
#    target/debug/ic10lsp.exe -> "../FlorpyDorp Language Support/bin/ic10lsp-win32.exe"
# 5. cargo test   (only touch signature_override if a special-case instruction changed)
```

`build.rs` declares `cargo:rerun-if-changed` on all four files, so editing any of them
re-triggers generation on the next build.

## Verification

```bash
# generated files (path has a build hash)
ls target/debug/build/ic10lsp-*/out/
#   enums_generated.rs  instructions_generated.rs  device_hashes_generated.rs  descriptions_generated.rs

# device count (÷2 for per-map: DEVICE_NAME_TO_HASH + HASH_TO_DISPLAY_NAME)
grep -c '=>' target/debug/build/ic10lsp-*/out/device_hashes_generated.rs

# invariants (signatures<->docs cover the same instruction set, etc.)
cargo test matching
```

## Troubleshooting

- **`ENOENT ... data/game-sources/...`** — a generator is still pointing at the old folder.
  Both readers (`build.rs`, `tree-sitter-ic10/generate-grammar.js`) must read from `data/`.
- **Build fails reading a source file** — confirm all four exist in `data/`:
  `ls data/` should show `Enums.json Stationpedia.json english.xml ProgrammableChip.cs`.
- **`duplicate key` panic from phf** — a source file has duplicate keys (english.xml sometimes
  ships duplicate `Key`s); the build keeps the first. If it still panics, a new duplicate slipped
  past a dedup guard in `build.rs`.
- **New types/devices not appearing** — the old LSP binary is still loaded; rebuild and re-copy
  the binary into `FlorpyDorp Language Support/bin/`, then reload the extension host.
- **`matching_instructions` test fails** — an instruction has a doc but no signature (or vice
  versa). Normally auto-resolved; only needs an override entry for a genuine special case.

## Related

- `build.rs` — Rust code generator
- `tree-sitter-ic10/generate-grammar.js` — grammar token generator
- `src/instructions.rs` — includes the generated maps; the old hand-tuned tables are disabled here
- `src/device_hashes.rs` — thin `include!` of the generated device maps
- `/data/README.md` — canonical source-files + update documentation
