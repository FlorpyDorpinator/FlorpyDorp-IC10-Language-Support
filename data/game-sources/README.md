# Game Source Files for Auto-Generation

This folder contains source files extracted from Stationeers game data that are used to auto-generate LSP definitions.

## Files

### Enums.json
**Source:** Extracted from game data  
**Contains:**
- `scriptEnums.LogicType` - All logic types (257 entries) with descriptions
- `scriptEnums.SlotLogicType` - All slot logic types with descriptions
- `scriptEnums.BatchMode` - Batch operation modes (Average, Sum, Minimum, Maximum)
- `scriptEnums.ReagentMode` - Reagent modes (Contents, Required, Recipe)

**Used for:** Auto-generating LOGIC_TYPES, LOGIC_TYPE_DOCS, SLOT_LOGIC_TYPES, SLOT_TYPE_DOCS, BATCH_MODES, BATCH_MODE_DOCS, REAGENT_MODES

### Stationpedia.json
**Source:** Extracted from game data  
**Contains:**
- Device information with logic type support
- `scriptCommands` section with instruction descriptions and examples

**Used for:** Auto-generating instruction descriptions and potentially device-specific logic type mappings

### ProgrammableChip.cs
**Source:** Decompiled from Assembly-CSharp.dll  
**Path:** `Assets/Scripts/Objects/Electrical/ProgrammableChip.cs`  
**Contains:**
- `GetCommandExample(ScriptCommand command)` - Complete instruction parameter signatures
- Parameter type definitions (REGISTER, DEVICE_INDEX, LOGIC_TYPE, NUMBER, STRING, etc.)

**Used for:** Auto-generating INSTRUCTIONS map with accurate parameter type signatures

## Update Process

When Stationeers updates:

1. Extract new `Enums.json` from game data
2. Extract new `Stationpedia.json` from game data
3. Decompile Assembly-CSharp.dll and copy `ProgrammableChip.cs`
4. Replace these three files
5. Run the build process - all LSP definitions will auto-update!

## Auto-Generated Files

The build process (build.rs) will read these files and generate:

- **From Enums.json:**
  - Logic types and descriptions
  - Slot logic types and descriptions
  - Batch modes and descriptions
  - Reagent modes and descriptions

- **From ProgrammableChip.cs:**
  - Instruction parameter signatures
  - Parameter type definitions

- **From Stationpedia.json:**
  - Instruction descriptions
  - Device capabilities

This ensures the LSP stays synchronized with the game without manual maintenance!
