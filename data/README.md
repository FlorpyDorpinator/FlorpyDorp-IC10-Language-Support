# Game Source Files — Auto-Generation

This folder holds the **four** Stationeers game-data files that the language server
auto-generates all of its game knowledge from. Drop in fresh files after a game
update, run `cargo build`, and the extension is back in sync — no hand-editing of
Rust source required.

> **TL;DR update flow:** replace the 4 files below → `cd ic10lsp && cargo build` → done.

---

## The 4 source files (single source of truth)

| File | Where it comes from | Drives |
|------|--------------------|--------|
| `Enums.json` | `rocketstation_Data/StreamingAssets/Data/Enums.json` | Logic types, slot logic types, batch modes, reagent modes, **all enums incl. `GasType`** |
| `Stationpedia.json` | `rocketstation_Data/StreamingAssets/Language/english/Stationpedia.json` | **Device name ↔ hash** maps + **IC10 instruction descriptions** |
| `ProgrammableChip.cs` | Decompiled `Assembly-CSharp.dll`, `Assets/Scripts/Objects/Electrical/ProgrammableChip.cs` | Instruction parameter signatures (reference data) |
| `english.xml` | `rocketstation_Data/StreamingAssets/Language/english.xml` | Human-readable device **display names** + device **descriptions** |

All four live **directly in `data/`**. The build script ([`ic10lsp/build.rs`](../ic10lsp/build.rs))
reads them via `../data/<file>`.

---

## What gets generated, and from what

At compile time `build.rs` writes Rust into `OUT_DIR` and `include!`s it into `src/`.
Nothing below is edited by hand.

### From `Enums.json`
- `LOGIC_TYPES` + `LOGIC_TYPE_DOCS`
- `SLOT_LOGIC_TYPES` + `SLOT_TYPE_DOCS`
- `BATCH_MODES` + `BATCH_MODE_DOCS`
- `REAGENT_MODES` + `REAGENT_MODE_DOCS`
- `ENUM_VALUE_BY_NAME`, `ENUM_DESC_BY_NAME`, `ENUM_DEPRECATED`, `LOGIC_TYPE_NAME_TO_VALUE`
  (covers every enum family, including `GasType`, `Color`, `SortingClass`, etc.)
- Deprecated members are skipped from the type lists automatically.

→ generated files: `enums_generated.rs`, `instructions_generated.rs`

### From `Stationpedia.json`
- `DEVICE_NAME_TO_HASH` — every `PrefabName → PrefabHash` from the `pages` array.
- `INSTRUCTION_DOCS` — every IC10 instruction's hover description, from `scriptCommands`.
- `INSTRUCTIONS` — the **type-checking signature map** (which operands each instruction takes
  and their allowed types), parsed from the color-coded `scriptCommands[].example` strings.

→ generated files: `device_hashes_generated.rs`, `instructions_generated.rs`

### From `english.xml`
- `HASH_TO_DISPLAY_NAME` — `PrefabHash → "Friendly Name"` (joined to Stationpedia by prefab).
- `DEVICE_DESCRIPTIONS` — `PrefabName → (display name, cleaned description)`.

→ generated files: `device_hashes_generated.rs`, `descriptions_generated.rs`

### From `ProgrammableChip.cs`
- `INSTRUCTION_SIGNATURES` — parameter types parsed from the decompiled `GetCommandExample`
  switch. Kept as a secondary **reference / cross-check**; the live signature map (`INSTRUCTIONS`)
  is generated from `Stationpedia.json`.

→ generated file: `instructions_generated.rs`

---

## How instruction signatures are generated (and the override table)

The `INSTRUCTIONS` map is generated in [`build.rs`](../ic10lsp/build.rs) by parsing each
instruction's `scriptCommands[].example`. In those examples the **color** of an operand
encodes its type and `|` marks a union:

| Color | Meaning | DataType |
|-------|---------|----------|
| blue `#0080ffff` | `r?` | Register |
| teal `#20b2aa` | `num`/`int`/`id`/`*Hash`/`slotIndex` | Number *(+ Register — see below)* |
| green | `d?` | Device |
| white | `str` | Name |
| orange | `logicType` / `logicSlotType` / `batchMode` / `reagentMode` | the matching enum type |

Two IC10 semantics are baked into the parser because the examples understate them:
a **numeric operand also accepts a register**, and a **batch/reagent mode operand also
accepts a number** (reagent mode also a register). With those rules, **144 / 154** instructions
parse to exactly the right signature automatically.

The remaining handful live in a small **override table** (`signature_override` in `build.rs`)
— cases where real validation is broader or narrower than the literal example (a *stored*
value may be a literal; device-by-id forms like `clr`/`ld`/`sd`; `sdns`/`sdse` take a device
only; the `label` pseudo-op). Edit that table only if one of those instructions changes.

**When a game update adds a new instruction:** its signature is parsed automatically — usually
no action needed. The test `instructions::test::matching_instructions` still guards that every
instruction has both a signature and a doc. Only add an override if the new instruction needs
the broader/narrower treatment the example doesn't capture.

> The 2024-25 gas update added `clamp`, `rol`, `ror`, `sgn` — all parsed automatically, no
> overrides required.

---

## Update process (when Stationeers patches)

```bash
# 1. Archive the current files (optional but recommended)
#    e.g. move them into  data/game-sources/Old Versions/<patch name>/

# 2. Copy the 4 fresh files into data/
cp "$STEAM/rocketstation_Data/StreamingAssets/Data/Enums.json"                       data/
cp "$STEAM/rocketstation_Data/StreamingAssets/Language/english/Stationpedia.json"    data/
cp "$STEAM/rocketstation_Data/StreamingAssets/Language/english.xml"                  data/
# ...and the freshly decompiled ProgrammableChip.cs
cp /path/to/decompiled/ProgrammableChip.cs                                           data/

# 3. Rebuild — build.rs regenerates everything (incl. instruction signatures)
cd ic10lsp && cargo build

# 4. Run the tests. New instructions are picked up automatically; only touch the
#    signature_override table in build.rs if a special-case instruction changed.
cargo test
```

`build.rs` declares `cargo:rerun-if-changed` on all four files, so editing any of them
re-triggers generation on the next build.

### Verify a build picked up new data
```bash
# device count (should track the game's prefab count)
grep -c '=>' target/debug/build/ic10lsp-*/out/device_hashes_generated.rs   # ÷2 for per-map

# instruction docs count
grep -c '=>' target/debug/build/ic10lsp-*/out/instructions_generated.rs
```

---

## Folder layout

```
data/
├── Enums.json            # ← source of truth (logic types, enums, gases)
├── Stationpedia.json     # ← source of truth (device hashes, instruction docs)
├── ProgrammableChip.cs   # ← source of truth (instruction signatures, reference)
├── english.xml           # ← source of truth (display names, descriptions)
├── README.md             # this file
└── game-sources/
    └── Old Versions/     # archive of previous patches' source files
```

---

## Notes / gotchas

- **Hashes come from `Stationpedia.json`, names from `english.xml`.** A device with no
  english.xml entry falls back to its raw `PrefabName` as the display name.
- **Duplicate keys are tolerated** — `english.xml` occasionally ships duplicate `Key`s; the
  build keeps the first occurrence (phf maps require unique keys).
- **The old `dev/extractor` → `stationpedia.txt` path is retired.** Device data no longer
  depends on running the Python extractor; it's generated straight from `Stationpedia.json`.
- This keeps the LSP synchronized with the game with effectively zero manual maintenance,
  apart from adding brand-new *instructions* to the hand-tuned validation map.
