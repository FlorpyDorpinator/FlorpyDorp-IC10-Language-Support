# Auto-Generated Grammar

The `grammar.js` file is now **auto-generated** from the game source files to ensure it stays in sync with the LSP.

## How It Works

The `generate-grammar.js` script reads:
- **Enums.json** - For LogicTypes, SlotLogicTypes, and BatchModes
- **ProgrammableChip.cs** - For instruction names (from GetCommandExample cases)
- **Stationpedia.json** - For script constants (nan, pi, tau, rgas, etc.)

It generates `grammar.js` with:
- ✅ **150 instructions** (operation tokens)
- ✅ **257 LogicTypes**
- ✅ **31 SlotLogicTypes**  
- ✅ **4 BatchModes**
- ✅ **9 constants** (nan, pinf, ninf, pi, tau, deg2rad, rad2deg, epsilon, rgas)
- ✅ **292 total logictype tokens**

## Usage

To regenerate the grammar after game updates:

```bash
npm run generate
```

This will:
1. Run `generate-grammar.js` to create `grammar.js`
2. Run `npx tree-sitter generate` to build the parser

## What Was Missing

The old hardcoded grammar had:
- **~100 instructions** → Found **50+ missing instructions**
- **7 constants** (nan, pinf, ninf, pi, deg2rad, rad2deg, epsilon) → Missing **tau** and **rgas**!

Auto-generation ensures everything is included!

## Benefits

- 🔄 **Always in sync** - Grammar uses same source as LSP
- 🎯 **Complete** - No more missing keywords
- 🛠️ **Maintainable** - Single source of truth (game files)
- 🚀 **Automated** - One command to update everything
