### Changelog Beginning 11-01-2025

## [2.3.0] - 2025-12-24 The "Clean Architecture" Update

### 🐛 Critical Bug Fixes
- **Fixed Semantic Tokens UTF-16 Encoding**: Resolved crash caused by position encoding mismatch
  - Fixed "Invalid Semantic Tokens Data From Extension: end character > model.getLineLength(lineNumber)" error
  - Root cause: Tree-sitter returns byte offsets (UTF-8) but VS Code expects UTF-16 code units
  - Solution: Implemented proper conversion using `encode_utf16().count()` for all token positions
  - Impact: Prevents complete LSP server crashes when files contain multi-byte Unicode characters
  - Added boundary checking and `saturating_sub()` to prevent position underflow
  - Removed obsolete byte-based position calculations

### 🏗️ Major LSP Codebase Refactoring
- **main.rs Modularization**: Reduced from 5,631 lines to 1,028 lines (82% reduction!)
  - Code is now organized into focused, single-responsibility modules
  - Dramatically improved maintainability and readability
  - Zero functionality changes - all features work exactly as before

- **New Module Structure**:
  | Module | Lines | Purpose |
  |--------|-------|---------|
  | `lsp_completion.rs` | 1,308 | All completion/autocomplete handling |
  | `lsp_diagnostics.rs` | 1,362 | Diagnostic generation and type checking |
  | `lsp_hover.rs` | 858 | Hover documentation and inlay hints |
  | `lsp_handlers.rs` | 816 | Semantic tokens, symbols, signature help, code actions, goto definition |
  | `document.rs` | 201 | Document data structures and type tracking |
  | `types.rs` | 75 | Position/Range type conversions |
  | `tree_utils.rs` | 87 | Tree-sitter node utilities |
  | `type_classification.rs` | 123 | Parameter type classification helpers |
  | `diagnostic_helpers.rs` | 36 | Diagnostic utility functions |

- **Cleanup**: Removed debug utilities and build artifacts
  - Deleted `debug_crc32.rs`, `debug_tree_structure.rs`
  - Removed temporary `.txt` files from source directory
  - Clean build with zero compiler warnings

### 🎯 Branch Visualizer Fixes
- **Fixed Absolute vs Relative Branch Display**: 
  - `j 3` now correctly shows "⇓ to line 3" instead of "⇓ +3 lines forward"
  - Absolute branches (j, beq, bne, etc.) display destination only
  - Relative branches (jr, brgt, brle, etc.) continue showing offset info
  
- **Fixed Numeric Line Number Support**:
  - `j 3` now correctly recognized as "jump to line 3"
  - Previously, numeric operands were incorrectly parsed as relative offsets
  - Proper 1-based to 0-based line number conversion

---

## [2.2.0] - 2025-12-16 The "Performance Optimization" Update

### ⚡ Major Performance Improvements
- **Diagnostic Performance**: 86% faster diagnostics (27.77ms → 3.87ms average)
  - Baseline testing: 27.77ms average across 98 runs (18,048 total diagnostics)
  - After optimization: 3.87ms average with 63.6% cache hit rate
  - P95 latency: 14.06ms (well under 20ms target)
  
- **Proper Debouncing with Task Cancellation**: Fixed critical debouncing bug
  - Previously: Throttling (ran diagnostics every 150ms regardless of typing)
  - Now: True debouncing (runs 250ms after LAST keystroke)
  - Each keystroke cancels previous pending diagnostic task with `.abort()`
  - Adaptive delays: 250ms for small files, 400ms for files >500 lines
  - Result: 87% reduction in diagnostic executions (54 calls → 7 actual runs)
  
- **Content-Based Diagnostic Caching**: Intelligent caching with SHA256 hashing
  - Diagnostics cached by content hash - unchanged files return instantly
  - 63.6% cache hit rate on first test (7 hits / 11 calls)
  - Cache hits complete in 0.17ms (99% faster than full analysis)
  - DashMap for lock-free concurrent access (avoids async deadlocks)
  - Auto-cleanup: Maintains 100 most recent entries
  - Performance metrics: `cache_hits` and `cache_misses` counters tracked
  
- **Benefits**:
  - Near-instant feedback when switching between files
  - No re-analysis when hovering over unchanged code
  - Minimal CPU usage during rapid typing
  - Dramatically improved responsiveness in large workspaces

### 🎨 Branch Visualizer Performance Improvements
- **Fixed Critical Performance Issues**: Resolved expensive operations in branch visualization
  - **Proper Debouncing**: 500ms delay after last keystroke (prevents update spam)
    - Previously: Fired on EVERY keystroke with stacking timeouts
    - Now: Cancels pending updates, only runs after typing stops
    - Tracked: `updatesCancelled` counter shows how many redundant updates prevented
  - **Content Change Detection**: Only re-parses when document actually changes
    - `hasRelevantChanges()` compares document version and content
    - Skips updates for identical content (cursor moves, focus changes)
    - Tracked: `updatesSkipped` counter for cache effectiveness
  - **Branch Parse Caching**: LRU cache for parsed branch instructions
    - Cache key: `${lineNumber}:${lineText}`
    - Avoids re-parsing unchanged lines on every update
    - Cleared only when line count changes (lines added/removed)
    - Tracked: `cacheHits` and `cacheMisses` counters
  - **Smart Cache Invalidation**: Preserves cache for edits within existing lines
    - Only clears when `document.lineCount` changes
    - Dramatically improves performance for inline edits
  - **Benchmarking System**: Built-in performance tracking
    - Commands: `ic10.toggleBenchmarking`, `ic10.showBenchmarkStats`
    - Tracks: `parseRelativeBranch`, `applyBranchVisualization`, `hasRelevantChanges`
    - Reports: avg/min/max/total times, cache hit rate, update statistics

- **Benefits**:
  - No lag while typing in files with branch visualization active
  - Instant updates after 500ms pause (smooth user experience)
  - Minimal CPU usage - only processes actual changes
  - Cache hit rates of 80%+ for typical editing workflows

### 📖 Enhanced Hover Tooltips with Device Descriptions
- **English.xml Integration**: Device hovers now show full descriptions from game localization
  - **Build-time XML Parsing**: Automatically extracts ~2000+ device descriptions during compilation
  - **HASH() Function Support**: Hover over `HASH("DeviceName")` shows hash value, display name, and full description
  - **Numeric Hash Support**: Hover over device hash numbers (e.g., `-1258351925`) shows device info with descriptions
  - **Robust Hover Detection**: Fixed to handle hovering on any part of HASH function
    - Works on `HASH` keyword, quoted string `"DeviceName"`, or anywhere in the function
    - Previously only worked when hovering specific positions
  - **Zero Runtime Cost**: Uses PHF (Perfect Hash Functions) for O(1) compile-time lookups
  - **Auto-generation**: `descriptions.rs` module with ~2000 entries regenerated on every build
  - **Clean Formatting**: Strips game markup (`{LINK:}`, `{THING:}`, `{GAS:}`, `{HEADER:}`) from descriptions

- **Implementation Details**:
  - Added `quick-xml = "0.31"` dependency for XML parsing
  - Enhanced `build.rs` with `parse_english_xml()` and `clean_description()` functions
  - Created `descriptions.rs` module with API: `get_device_description()`, `get_description_text()`, `get_display_name()`
  - Generates `descriptions_generated.rs` with PHF map at compile time
  - Data source: `../data/game-sources/english.xml` (29,530 lines)
  - Hover handler now checks for `hash_function`, `hash_string`, and `hash_keyword` nodes
  - Parent node navigation for child elements ensures consistent hover behavior

### 🧹 Code Cleanup
- **Dead Code Removal**: Removed 9 unused functions, constants, and variables
  - Removed: `sort_completions_by_usage`, `should_ignore_register_warnings`
  - Removed: `get_description_text`, `get_display_name`, `is_str_function_call`
  - Removed: `ABLIST`, `BATCH_MODE`, unused imports
  - Suppressed warnings for auto-generated `INSTRUCTION_SIGNATURES`
  - Cleaner, more maintainable codebase

### 📊 Benchmarking System
- **Comprehensive Performance Tracking**: Full-stack benchmarking infrastructure
  - Client-side metrics: TypeScript PerformanceTracker with P50/P95/P99 statistics
  - Server-side metrics: Rust PerformanceTracker with RAII timing guards
  - Tracks: diagnostics, hover, completion, parsing operations
  - Commands: `ic10.toggleGlobalBenchmarking`, `ic10.showGlobalBenchmarkStats`
  - Server commands: `ic10.server.enableBenchmarking`, `ic10.server.getBenchmarkReport`
  - Maintains last 1000 measurements for accurate percentile calculations

### 🔧 Technical Details (LSP v0.10.0)
- **Dependencies Added**:
  - `sha2 = "0.10"` - Content hashing for cache keys
  - `parking_lot = "0.12"` - Initially attempted, replaced with DashMap
  - Uses existing `dashmap = "5.5.3"` for lock-free cache storage
  
- **Implementation Details**:
  - Backend struct: Added `diagnostic_cache: Arc<DashMap<String, Vec<Diagnostic>>>`
  - Backend struct: Added `pending_diagnostics: Arc<Mutex<HashMap<Url, JoinHandle<()>>>>`
  - Debounce constants: `DIAGNOSTIC_DEBOUNCE_MS = 250`, `DIAGNOSTIC_DEBOUNCE_LARGE_FILE_MS = 400`
  - Cache check happens before diagnostic analysis (early return on hit)
  - Task cancellation uses `tokio::spawn` + `JoinHandle::abort()`
  - SHA256 hash computed from file content for cache keys
  
- **Performance Metrics**:
  - Hover: 1.87ms server / 3.65ms client (stable, excellent)
  - Parsing: 0.54ms average (very fast)
  - Diagnostics: 3.87ms average (86% improvement from baseline)
  - Cache hit: 0.17ms min (instant feedback)
  - Cache miss: 14.06ms max (full analysis when content changes)

### 🎯 Results Summary
**Before Optimization:**
- Diagnostics: 27.77ms avg (98 runs)
- No caching (repeated analysis on every change)
- Throttling-based debouncing (ran every 150ms)

**After Optimization:**
- Diagnostics: 3.87ms avg (11 runs, 7 cache hits)
- 86% faster overall
- 87% fewer diagnostic executions
- 63.6% cache hit rate
- True debouncing (runs after last keystroke)

**User Experience:**
- Near-instant feedback when typing
- No lag when switching between files
- Minimal CPU usage during editing
- Smooth performance even in large workspaces

## [2.1.11] - 2025-12-02
### 🐛 Bug Fixes
- **Fixed Inlay Hints Auto-Refresh**: Numeric device hash inlay hints now update automatically
  - Inlay hints refresh immediately when typing or editing documents
  - Inlay hints appear instantly when opening files from explorer
  - No longer requires window reload to see device name hints for numeric hashes
  - Improves real-time feedback for device hash recognition


## [2.1.10] - 2025-12-02
### 🐛 Bug Fixes
- **Fixed README Image Display**: All images now use absolute GitHub raw URLs for proper VS Code Marketplace display
  - Converted 9 image references from relative paths to `https://raw.githubusercontent.com/.../images/filename`
  - Fixed device-hash-hints.png reference to use device-hash-demo.gif (original file didn't exist)
  - Images now display correctly in VS Code Marketplace listings


## [2.1.9] - 2025-12-01
### 🐛 Bug Fixes
- **Fixed ReadMe image display issue


## [2.1.8] - 2025-11-30 The "Branch Color Fix" Update

### 🐛 Bug Fixes
- **Fixed Branch Visualization Colors**: Arrows (⇑/⇓) and dots (●) now correctly match their corresponding branch highlight colors
  - Previously used depth-based colors instead of branch-specific colors
  - Each branch's arrow, dot, and highlight now use the same color from the palette
  - Improves visual clarity when identifying which branches connect to which targets

## [2.1.7] - 2025-11-30 The "BatchMode Validation" Update

### 🐛 Bug Fixes
- Added strict validation for BatchMode functionality, allowing only numeric values (e.g., 0, 1, 2, 3, etc.) while removing support for named strings (e.g., "Average", "Sum"). This ensures consistency with the game's actual behavior and prevents runtime errors.

## [2.1.6] - 2025-11-30 The "Auto-Generation" Update

### 🎯 Major Infrastructure Improvements
- **Fully Auto-Generated Grammar**: tree-sitter grammar now auto-generated from game source files
  - Extracts **150 instructions** from ProgrammableChip.cs (50+ more than hardcoded grammar)
  - Extracts **257 LogicTypes** + **31 SlotLogicTypes** + **4 BatchModes** from Enums.json
  - Extracts **9 script constants** from Stationpedia.json (nan, pinf, ninf, pi, tau, deg2rad, rad2deg, epsilon, rgas)
  - Grammar.js now regenerates automatically on build
  - Zero maintenance burden - always in sync with game data

### 🐛 Bug Fixes
- **Fixed Missing Constants**: Added `tau` and `rgas` constants (were missing from hardcoded grammar)
- **Fixed Missing Instructions**: Auto-generation discovered 50+ missing instructions including:
  - Various branch instructions (bdnsal, bdseal, brdns, brdse, etc.)
  - Label and define directives
  - Many conditional branches and operations
- **Fixed Missing Slot Types**: Completed SlotLogicTypes including FilterType, Quantity, Efficiency, Health, Growth, etc.

### 🔧 Build System
- Added automated grammar regeneration to build task
- Single command now regenerates grammar + rebuilds LSP + copies binary
- Comprehensive documentation in AUTO-GENERATION.md

## [2.1.5] - 2025-11-29 The "Branch Visualizer" Update

### ✨ New Features
- **Branch Visualization**: Visual indicators for relative branch instructions (jr, breq*, bne*, blt*, bgt*, etc.)
  - Color-coded arrows (⇑/⇓) at source lines showing branch direction
  - Dots (●) at target lines marking jump destinations
  - Ghost text descriptions showing target line and preview: ` ⇑ line 17: l r0 d0 On`
  - Multi-color highlights with intelligent depth assignment (shortest branches leftmost)
  - Per-segment opacity: Source lines lighter (0.15), target lines darker (0.45)
  - Split highlights when multiple branches overlap - each segment independently shows correct opacity
  - Toggle on/off with **Ctrl+Alt+B**
  - Helps visualize control flow and understand complex branching logic
  - Consistent spacing within branch ranges for clean alignment

### 🔧 Instruction Improvements
- **Updated Instruction Signatures**: Added missing parameters to instructions
  - `sdse`: Now properly shows 3 parameters (device, slotIndex, value)
  - `sdns`: Now properly shows 3 parameters (device, nameHash, value)
  - `alias`: Now properly shows 2 parameters (aliasName, registerOrDevice)
  - Hover documentation and signature help updated to match
  - InlayHints (shadow text) now display correct parameter counts

### 🐛 Bug Fixes
- **Fixed Register Alias Collision**: Instruction `r` (remainder/modulo) no longer triggers register completions
  - Previously typing `r` would incorrectly suggest r0-r17 registers
  - Now correctly recognized as the modulo instruction
  - Prevents accidental register references when using remainder operation
- Fixed instruction parameter validation for `sdse`, `sdns`, and `alias` instructions
- Branch visualization correctly handles lines that are both source and target for different branches
- Improved ghost text positioning to avoid cursor interference

### ⌨️ New Keybindings
- **Ctrl+Alt+B** - Toggle branch visualization on/off

## [2.1.0] - 2025-11-29 The "Completions" Update
### Overview
 - Major improvements to many of the underlying systems related to auto completions. There were some deep errors in how that was working and now you should be able to auto-complete your instructions seamlessly. 
 - The extension now pulls from the game's source code which I've decompiled for the purpose of pulling these values. It also takes the enums & stationpedia .jsons from a mod that rips those directly. This should be absolutely the most complete version now. Every item, structure, etc. should be in the game. This means that I can easily add mods to my game and pull their names whenever I want. So if you have a mod you want added let me know.
 - Some other issues with hovering and various instruction types are now fixed too. This should all be quite stable. Still testing on a large library of ic10 scripts to see if it fails. 
 - Cleaned up the folder heirarchy and got rid of some duplicate files and organized it better. More to do here but a good start.


### 🚀 Major Feature: Auto-Generation System
- **Automatic Type Definitions**: LSP now auto-generates all type definitions from Stationeers game source files for easy maintenance
  - Replaces 187 manually-maintained logic types with 257 types extracted from game data
  - All types include descriptions from game (93 missing types added, including `VolumeOfLiquid`)
  - Zero manual maintenance required - just replace 3 source files and rebuild
  - Uses phf crate for compile-time perfect hashing (O(1) lookups, zero runtime cost)
  
- **Build-Time Code Generation**: New `build.rs` system parses game sources during compilation
  - Parses `Enums.json` (LogicType, SlotLogicType, BatchMode, ReagentMode)
  - Parses `Stationpedia.json` (device documentation)
  - Parses `ProgrammableChip.cs` (instruction signatures)
  - Generates `instructions_generated.rs` included directly into LSP
  
- **Update Process**: Simple 3-step workflow for game updates
  1. Replace source files in `ic10lsp/data/game-sources/`
  2. Run `cargo build --release`
  3. Copy new binary to extension
  - See `ic10lsp/AUTO-GENERATION.md` for complete documentation (~800 lines)
  - See `ic10lsp/data/game-sources/README.md` for extraction guide (~400 lines)
  
- **Benefits**:
  - ✅ Always up-to-date with game patches
  - ✅ 100% coverage of all logic types (257 vs 187 previously)
  - ✅ Authoritative source - data comes directly from game
  - ✅ Type safety - compile-time generation ensures correctness
  - ✅ Future-proof for new Stationeers features

### 🐛 Critical Bug Fixes
- **Fixed SlotLogicType Completions**: Now shows all 31 slot types (was showing 0)
  - Root cause: Enum name mismatch in build.rs (SlotLogicType vs LogicSlotType)
  - Fixed enum names: `LogicSlotType`, `LogicBatchMethod`, `LogicReagentMode`
  - All 31 types now available: Occupant, OccupantHash, Charge, ChargeRatio, Class, etc.
  
- **Fixed BatchMode Strict Validation**: Only 4 modes allowed (Average, Sum, Minimum, Maximum)
  - Batch instructions (lb, lbn, lbs, lbns, sb, sbn, sbs) now enforce strict mode list
  - Registers no longer suggested for BatchMode parameters
  - Prevents invalid code like `lbn r0 HASH("Device") HASH("Device") Acceleration r5`
  
- **Fixed Label Completions**: Branch/jump instructions now show ONLY labels
  - Instructions starting with 'b' (except 'br*') and 'j'/'jal' get label-only completions
  - Removed defines, aliases, and registers from branch target suggestions
  - Cleaner completion list for jump targets
  
- **Fixed HASH(" Suggestions**: Now appears for all batch instruction hash parameters
  - Shows at top of completions for deviceHash and nameHash parameters
  - Instructions: define (param 1), lbn/lbns (params 1, 2), sbn (params 0, 1), lb/lbs (param 1), sb/sbs (param 0)
  - Automatically triggers device completions when typing inside HASH("...")
  
- **Fixed Signature Hover Parameter Highlighting**: Blue highlight now works on all lines
  - Root cause: Converting LSP position column to document byte offset incorrectly
  - Now uses `line_start_byte + column` for accurate byte position
  - Parameter highlighting tracks correctly as you type on lines 0, 1, 2, ... N
  - Each parameter highlights blue in the signature popup as you reach it

### ✨ Completion System Improvements
- **Fixed Global HASH Detection**: HASH() completions now work correctly everywhere
  - Typing `HASH("` triggers device completions in any context (defines, instructions, parameters)
  - Fixed `already_complete` logic to skip device loop when HASH is complete
  - Allows fallthrough to parameter completions when appropriate
  - Example: `sbn HASH("Fertilizer") [space]` now shows HASH() for nameHash parameter
  
- **Windows Line Ending Support**: Fixed byte offset calculations for CRLF line endings
  - Completions now work correctly on Windows after line 3
  - Added `original_position` variable to preserve LSP position before adjustments
  - Rewrote cursor byte calculation using `char_indices()` for accurate CRLF handling
  - Fixed two separate cursor_byte calculations (global HASH and instruction bounds)
  - Should fix mismatch with in game editor byte counts.
  
- **Complete Fallback Completions**: Added comprehensive fallback when cursor outside instruction bounds
  - Registers (r0-r17, ra, sp) for all parameter types
  - LogicType/SlotLogicType/BatchMode/ReagentMode for static parameters
  - Labels for branch/jump instructions (starts with 'b' or 'j')
  - `ra` register specifically for "*al" suffix instructions (jal, beqal, etc.)
  - Number completions for numeric parameters
  - Example: `s d0 [space]` shows registers even if cursor is past instruction end
  
- **Label Completions**: Branch and jump instructions now show labels on jump target parameter only
  - Jump instructions (`j`, `jal`, `jr`): Labels show for parameter 0 (the jump target)
  - Branch instructions (`beq`, `bne`, `blt`, `bgt`, etc.): Labels show for last parameter (the jump target)
  - Example: `beq r0 r1 [here]` - labels only appear for 3rd parameter, not for r0/r1 comparison values
  - Prevents labels from incorrectly appearing on comparison operands
  - Shows labels from `file_data.type_data.labels` with kind=CONSTANT
  - Detail text: " label"
  
- **Improved Prefix Extraction**: Fixed completion prefix detection
  - Changed from `split_whitespace().last()` to `rfind(' ')`
  - Correctly extracts text after last space for filtering
  - Prevents wrong token from being used as filter prefix

### 🐛 Bug Fixes (LSP)
- **Completion Position Handling**: Fixed cursor position calculations throughout completion()
  - Added `original_position` to preserve LSP position before `saturating_sub(1)`
  - All byte offset calculations now use `original_position` instead of adjusted `position`
  - Fixed text fallback `cursor_col` calculation
  - Prevents off-by-one errors in position tracking
  
- **Parameter Completion Logic**: Added both builtin and static completions
  - Now calls `param_completions_builtin()` for registers/numbers/devices
  - Also calls `param_completions_static()` for LogicType/BatchMode/etc.
  - Previously only called static, missing register completions
  - Both are needed for complete parameter coverage

### 🔧 Technical Improvements (LSP v0.9.0)
- **Build System**: Added comprehensive `build.rs` script
  - Regex parsing for ProgrammableChip.cs instruction signatures
  - JSON parsing for Enums.json type definitions
  - Automatic recompilation when source files change
  - Generated code uses phf maps/sets for optimal performance
  
- **Code Organization**: Better separation of manual vs generated code
  - Generated types in `instructions_generated.rs`
  - Manual instruction definitions remain in `instructions.rs`
  - Old manual definitions commented out with explanation
  - Clear include statement: `include!(concat!(env!("OUT_DIR"), "/instructions_generated.rs"));`
  
- **Dependencies**: Added to Cargo.toml
  - `regex` in build-dependencies for ProgrammableChip.cs parsing
  - `serde_json` for Enums.json parsing
  - `phf` and `phf_codegen` for compile-time hash maps

### 📝 Documentation
- **AUTO-GENERATION.md**: Comprehensive 800-line guide
  - Architecture overview and build flow
  - What gets generated and why
  - Complete update process for new game versions
  - Verification steps and troubleshooting
  - Benefits and history comparison
  
- **data/game-sources/README.md**: Source file documentation
  - File format details for Enums.json, Stationpedia.json, ProgrammableChip.cs
  - Extraction instructions for each file type
  - Verification commands and expected outputs
  - Version history table
  - Backup procedures
  
- **DOCUMENTATION_INDEX.md**: Updated with auto-generation references
  - Links to new documentation files
  - Quick start guides
  - Update procedures

### 🧹 Known Issues
- Debug `eprintln!` statements still present in main.rs completion() function (~50+ lines)
- Will be removed in next patch release for cleaner logs

### 📊 Statistics
- **Logic Types**: 187 manual → 257 auto-generated (+37% coverage)
- **Missing Types Added**: 93 including VolumeOfLiquid and other new types
- **Documentation**: +1200 lines of new auto-generation documentation
- **Build Script**: 428 lines of parsing and code generation logic
- **Future-Proof**: No manual updates needed for game patches

## [2.0.2] - 2025-11-28

### Bug Fixes
- Fixed LSP binary packaging issue from v2.0.1

## [2.0.1] - 2025-11-28

### Documentation
- Updated all references from "Stationeers Dark" to "Stationeers Full Color Theme" for consistency

## [2.0.0] - 2025-11-28

### 🎉 Major Update: Respawn Update Beta Support
- **Game Version**: Updated to Stationeers v0.2.6054.26551 (Respawn Update Beta)
- **Device Hashes**: Expanded from ~1200 to 1709 devices (+509 new devices)
  - ✨ **Modular Console Mod**: Full support for all 101 modular console devices
  - Includes: ModularDeviceConsole, buttons, switches, dials, sliders, throttles
  - LED displays, gauges, meters, label plates, and all console variants
  - HASH() tooltips now recognize modular device names

### 🚀 New Instructions (13 Added)
- **Math**: `atan2` (arc tangent of y/x in radians), `pow` (power function), `lerp` (linear interpolation)
- **Bit Operations**: `ext` (extract bit field), `ins` (insert bit field)
- **Direct Device Access**: `ld` (load by ID), `sd` (store by ID)
- **Device Validation**: `bdnvl` (branch if device not valid for load), `bdnvs` (branch if not valid for store)
- **Stack Operations**: `clr` (clear device stack), `clrd` (clear by device ID), `poke` (store at stack address)
- **Recipe Mapping**: `rmap` (map reagent hash to prefab hash for autolathes/fabricators)
- All instructions include hover documentation and signature help

### 📊 Logic Types Expansion
- Verified all 242 logic types from latest Stationpedia
- Comprehensive coverage of all device properties:
  - Orbital mechanics (Eccentricity, SemiMajorAxis, TrueAnomaly, etc.)
  - Celestial navigation (CelestialHash, CelestialParentHash, DistanceAu, DistanceKm)
  - Advanced gas ratios (all Input/Output/Input2/Output2 variants)
  - Liquid ratios (all variants)
  - Solar positioning and efficiency metrics
- All logic types properly recognized in autocomplete and diagnostics

### ✨ Intelligent Completions System
- **Context-Aware Filtering**: Completions now filter based on instruction parameter types
  - LogicType/BatchMode parameters show ONLY their predefined constants (e.g., `Average`, `Sum`, `Maximum`, `Minimum`)
  - Register parameters (e.g., `add r0 _`) show registers and register aliases
  - Device parameters show device references and aliases
  - Number parameters show defines and numeric literals
  - Branch instructions (j, jal, b*) show ONLY labels (no defines/aliases/constants)
  - Batch instructions (lb, lbn, lbs, lbns, sb, sbn, sbs) recognize device hash parameter and suggest HASH()
  
- **Device Completions in HASH()**: Revolutionary dropdown experience
  - Typing `HASH("")` shows all 1709 device names with fuzzy filtering
  - Example: `HASH("Struct")` shows StructureVolumePump, StructureBatterySmall, etc.
  - Display format: `DeviceName` with detail showing `DisplayName → HashValue`
  - Case-insensitive matching for ease of use
  - Smart detection: Only triggers when cursor is inside HASH(""), not for completed HASH() calls
  - Smart quote handling: Auto-adds closing `")` for new HASH calls, but not when editing existing complete HASH
  - Prevents logic types/registers from appearing in HASH() context
  
- **Usage-Based Sorting**: Frequently-used items appear first
  - Registers (r0-r17, ra, sp) that appear earlier in your script are prioritized
  - Devices (d0-d5, db) used in your code float to the top
  - Aliases and defines you've created appear before unused items
  - Makes completions more relevant to your specific script
  
- **Automatic Dropdown Triggers**:
  - Space after instruction: Shows appropriate parameter completions
  - Opening quote in HASH(): Immediately shows device name completions
  - Empty LogicType/BatchMode parameters: Dropdown appears automatically
  
- **HASH() Number Validation**: New error diagnostic
  - `HASH("123")` shows ERROR diagnostic with message
  - "Content inside HASH() argument cannot be a number. Use the hash value directly: 123"
  - Prevents common mistake of putting hash values inside HASH()

- **Relative Branch to Label Warning**: New diagnostic for critical mistakes
  - Relative branches (e.g., `breq r0 0 labelName`) to labels now show warning
  - Message: "Relative branch to label - do you REALLY want to use a relative branch here? Relative branches use the numeric value at the label, not the label's line number."
  - Quick-fix action converts to absolute branch (e.g., `breq` → `beq`)
  - Prevents script-breaking mistake: relative branches read the value stored at the label position, not the label's line number
  
- **STR() Completion Suppression**: No completions inside STR()
  - `STR("")` strings are freeform text and don't trigger completions
  - Cleaner editing experience without irrelevant suggestions

### ⚡ Code Actions & Refactoring
- **HASH Conversion Refactoring**: Bidirectional conversion between HASH strings and numeric values
  - Right-click on `HASH("StructureVolumePump")` → Refactor → "Convert to hash number: -1258351925"
  - Right-click on numeric hash (e.g., `-1258351925`) → Refactor → "Convert to HASH(\"StructureVolumePump\")"
  - Works for all 1709 recognized device hashes
  - Appears in "Refactor..." submenu (CodeActionKind::REFACTOR)
  - Uses device_hashes.rs mapping for accurate conversions
  
- **Branch Conversion Quick-Fixes**: Convert between relative and absolute branches
  - Relative to Absolute: `breq r0 0 label` → Quick-fix → "Change to absolute branch (beq)"
  - Absolute to Relative: `beq r0 0 123` → Quick-fix → "Change to relative branch (breq)"
  - Tied to diagnostics: LINT_RELATIVE_BRANCH_TO_LABEL and LINT_ABSOLUTE_JUMP
  - Prevents common mistake of using relative branches with labels
  
- **Register Diagnostic Suppression**: Quick-fix to ignore false-positive register warnings
  - Click lightbulb on register diagnostic → "Ignore diagnostics for rX"
  - Automatically adds `# ignore rX` comment to suppress specific register warnings
  - Useful for complex control flow (loops, jumps) that static analysis can't follow
  - Hotkey alternative: Ctrl+Alt+I suppresses all register diagnostics

### 🎨 Visual Improvements
- **HASH()/STR() Syntax Highlighting**: Fixed content color
  - Content inside HASH("Device") and STR("text") now displays white instead of green
  - Changed TextMate scope from `string.quoted.double` to `variable.other.ic10`
  - Maintains consistent color with other device/variable references

- **Inlay Hint Behavior**: Improved shadow text (parameter hints) user experience
  - Parameter hints now disappear immediately when you start typing after an instruction
  - Prevents cursor jumping when pressing space after instruction names
  - Hints only appear when instruction has no operands and nothing typed after it
  - HASH() inlay hints (device name/hash value display) only show for complete HASH() calls
  - Incomplete HASH() calls don't show hints, preventing interference while typing

### 🐛 Bug Fixes
- **Batch Instruction Completions**: Fixed device hash parameter detection
  - Store batch instructions (sb, sbn, sbs): Device hash parameter correctly identified at position 0
  - Load batch instructions (lb, lbn, lbs, lbns): Device hash parameter at position 1
  - HASH() completions now trigger properly for all batch instruction variants
  
- **HASH() Completion Smart Quotes**: Fixed quote handling when editing existing HASH calls
  - Auto-adds closing `")` when typing new HASH() calls
  - Does NOT add closing quotes when editing inside existing complete HASH("device")
  - Detects if closing `")` already exists on the line after HASH("
  - Prevents duplicate quotes: `HASH("DeviceName")")` no longer occurs

- **Global HASH() Detection**: HASH() device completions now work anywhere
  - Typing `HASH("` triggers device completions in any context (defines, instructions, etc.)
  - Example: `define Satellite HASH("` shows device dropdown
  - No longer limited to instruction parameters
  - Fixed cursor position calculation for accurate detection

- **HASH(" Suggestions**: Smart suggestions for instructions that commonly use device hashes
  - `define` instruction: `HASH("` appears at top of completion list for value parameter
  - `lbn`/`sbn` instructions: `HASH("` suggested for nameHash parameter (3rd parameter)
  - Helps discover HASH function for device name lookup
  - Sort priority ensures HASH(" appears first in list

- **Define Prioritization**: Defines now appear at top of completion lists
  - For numeric/value parameters, defines are prioritized over registers
  - Sorting: Used defines → Unused defines → Used registers → Unused registers → Enums
  - Recognizes that defines often contain device hashes and important constants
  - Makes script-specific values more discoverable

### 🔧 Technical Improvements
- **Grammar Restructure**: HASH/STR now parsed as proper functions
  - Changed from single tokens (`hash_preproc`/`str_preproc`) to function nodes
  - New structure: `hash_function(hash_keyword, '(', hash_string, ')')`
  - Enables querying argument content for intelligent completions
  - All existing features (inlay hints, validation) updated for new structure
  
- **Tree-sitter Grammar**: Rebuilt parser with new function nodes and instructions
- **LSP Server**: Recompiled with context-aware completions and validation (v0.8.0)
- **Extension**: Rebuilt with latest game data and features (794.2kb bundle)

### 📝 Documentation
- Updated README with feature overview and completion screenshots
- Comprehensive changelog entry with all changes
- Created `CONTEXT_AWARE_COMPLETIONS_SUMMARY.md` technical documentation
- Version bump reflects major content and feature update

## [1.2.18] - 2025-11-27

### 🐛 Bug Fixes (Extension)
- **LSP Startup**: Fixed server initialization race condition
  - Added 100ms delay before sending initial configuration to allow server to fully initialize
  - Configuration change listener now properly disposed on extension deactivation
  - Prevents "Connection to server got closed" errors and unexpected restarts
  - Resolves startup issues in VS Code Insiders

### 🎨 Inlay Hints
- **Label Detection**: Fixed shadow text appearing after labels
  - Labels ending with `:` no longer trigger parameter hints
  - Improves code readability by not showing hints on non-instruction lines

## [1.2.17] - 2025-11-26

### 🎨 Theme Fixes
- **Register Colors**: Fixed registers (r0-r15, sp, ra) displaying as teal instead of blue
  - Registers now correctly display in blue (#0080FF) in both themes
- **Device Colors**: Fixed devices (d0-d5, db) displaying as teal instead of green
  - Devices now correctly display in bright green (#00FF00) in both themes
- Added proper semantic token color mappings for `macro` (registers) and `function` (devices)

### 🐛 Bug Fixes (LSP)
- **Semantic Token Validation**: Fixed "Invalid Semantic Tokens Data" errors that caused syntax highlighting to not update until Enter was pressed
  - Added bounds checking to ensure token lengths don't exceed line lengths
  - Prevents "end character > model.getLineLength" errors in VS Code
  - Syntax highlighting now updates immediately as you type
  - Fixes issue where colors wouldn't refresh until creating a new line

## [1.2.16] - 2025-11-26

### 🚀 Performance Improvements (LSP)
- **Large Workspace Optimization**: Significantly improved performance when working with many IC10 files
  - Added diagnostic debouncing (500ms delay) to prevent spam on rapid file changes
  - Smart batching: config changes only refresh 50 most recently-edited files instead of all files
  - File count warning when >50 files open with suggestion to use Ctrl+Alt+D to disable diagnostics
  - Timestamp tracking for intelligent diagnostic prioritization
  - Prevents LSP timeout errors in workspaces with 100+ IC10 files

### 🐛 Bug Fixes
- **Inlay Hints**: Fixed shadow text appearing on completed instructions
  - Properly recognizes `beqzal` and other `*zal` branch variants as 2-operand instructions
  - Fixed parameter counting logic to hide hints when all parameters are provided
- **Code Actions**: Fixed panic in code action handler (replaced unsafe unwrap() calls)

### 🔧 Technical Changes
- Added dashmap dependency for better concurrent performance
- Optimized `did_change_configuration` to avoid diagnostic cascades
- Improved memory efficiency with selective diagnostic runs

## [1.2.15] - 2025-11-26

### 🐛 Bug Fixes
- **Inlay Hints**: Fixed shadow text appearing on completed instructions
  - Properly recognizes `beqzal` and other `*zal` branch variants as 2-operand instructions
  - Fixed parameter counting logic to hide hints when all parameters are provided
  - Shadow text now correctly disappears for fully-formed instructions

## [1.2.14] - 2025-11-26

### 🐛 Bug Fixes
- **Critical Fix**: Fixed LSP crash in code action handler
  - Replaced unsafe `unwrap()` calls with proper Option handling
  - Prevents panic when diagnostic data is None
  - Fixes "called `Option::unwrap()` on a `None` value" crash at line 1714

## [1.2.13] - 2025-11-26

### 🚀 Performance Improvements
- **Large Workspace Optimization**: Significantly improved performance when working with many IC10 files
  - Added diagnostic debouncing (500ms delay) to prevent spam on rapid file changes
  - Smart batching: config changes only refresh 50 most recently-edited files instead of all files
  - File count warning when >50 files open with suggestion to use Ctrl+Alt+D to disable diagnostics
  - Timestamp tracking for intelligent diagnostic prioritization
  - Prevents LSP timeout errors in workspaces with 100+ IC10 files

### 🔧 Technical Changes
- Added dashmap dependency for better concurrent performance
- Optimized `did_change_configuration` to avoid diagnostic cascades
- Improved memory efficiency with selective diagnostic runs

## [1.2.12] - 2025-11-25

### ✨ New Features
- **#IgnoreLimits Directive**: Add `#IgnoreLimits` comment to scripts to suppress line and byte limit diagnostics
  - Suppresses "Instruction past line 128" errors for long scripts
  - Suppresses byte limit warnings for scripts exceeding 52KB
  - Case-insensitive (works with `#ignorelimits`, `#IgnoreLimits`, etc.)
  - Useful for development/testing of large scripts before optimization

### 🔧 Improvements
- Enhanced diagnostic control for better workflow flexibility

## [1.2.11] - 2025-11-24

### ✨ New Features
- **One-Time Theme Prompt**: Extension now prompts once on first install to choose between IC10 themes
  - "Syntax Colors Only" - Dark+ UI with Stationeers in-game syntax colors
  - "Full Custom Theme" - Complete custom theme with Stationeers aesthetics
  - "No Thanks" - Keep current theme
- Prompt only appears once and respects user's choice

### 📚 Documentation
- Added theme showcase section to README with screenshots
- Clarified differences between "Syntax Only" and "Full Theme" options
- Added theme selection guide for new users

### 🔧 Improvements
- Removed dependency on `ic10.colors.forceGamePalette` setting for cleaner configuration
- Added "IC10: Reset Theme Prompt" command for testing (developer feature)
- Improved theme prompt messaging to clearly explain options

## [1.2.10] - 2025-11-17

### ✨ New Features
- **Hash Diagnostics Toggle**: Added Ctrl+Alt+H to suppress/restore HASH() and device hash diagnostics
- New command: "IC10: Suppress Hash Diagnostics" for toggling hash-related warnings
- Added `ic10.lsp.suppressHashDiagnostics` setting to persist preference across sessions

### 🐛 Bug Fixes
- Fixed "Client does not support UTF-8" warning appearing on startup
- Removed debug messages that showed "Server received command" notifications
- Cleaned up verbose logging for better user experience
- Extension now properly handles UTF-16 encoding (default for LSP clients)

## [1.2.8] - 2025-11-17

### 🎨 Theme Improvements
- **Renamed Themes for Clarity**:
  - "Stationeers Full Color Theme" → "Stationeers Full Color Theme" (complete UI + syntax)
  - "IC10 In-Game Colors" → "Stationeers IC10 Syntax Only" (syntax highlighting only)
- Updated theme toggle command (Ctrl+Alt+T) to use new names
- Improved theme descriptions for better discoverability

## [1.2.7] - 2025-11-15

### 🐛 Bug Fixes
- **Auto-fix Execute Permissions**: Extension now automatically sets execute permissions for LSP binaries on Linux/macOS
- Eliminates "permission denied" errors on Unix-like systems
- No manual `chmod +x` required after installation

## [1.2.6] - 2025-11-15

### 🌍 Multi-Platform Support
- **Cross-Platform Binaries**: Extension now supports all major platforms
  - ✅ Windows (x64)
  - ✅ Linux (x64)
  - ✅ macOS Intel (x64)
  - ✅ macOS Apple Silicon (ARM64)
- **Automatic Platform Detection**: Extension automatically selects correct LSP binary for your OS
- **GitHub Actions Workflow**: Automated builds for all platforms on every push
- Fixed "binary not found" error for Linux and macOS users

## [1.2.5] - 2025-11-15

### ✨ New Features
- **Stationeers IC10 Editor Theme**: Added complete UI color theme matching in-game editor aesthetics
  - Deep blue-teal editor background (#0a2838) matching game console
  - Orange accents (#FFA500) for tabs, status bar, and highlights
  - Dark blue sidebar (#062030) and activity bar (#041820)
  - Orange window border when active
  - Complete coverage: editor, tabs, sidebar, terminal, menus, notifications, and more
- **Theme Toggle Command**: Press **Ctrl+Alt+T** to switch between Stationeers Full Color Theme and your previous theme
  - Remembers your previous theme across sessions
  - Works globally from any file type
- **Register Diagnostic Suppression**: Added `# ignore` directive to suppress false-positive register warnings
  - Manual: Add `# ignore r1, r2` anywhere in your code
  - Code Action: Click lightbulb on register diagnostic → "Ignore diagnostics for rX"
  - Hotkey: Press **Ctrl+Alt+I** to suppress all register diagnostics at once
- **LogicType Value Tracking**: Extension now tracks when registers hold LogicType values
  - `move LogicType.Power r0` marks r0 as holding a LogicType
  - Registers with LogicType or Number values accepted where LogicType parameters expected
  - Arithmetic operations on LogicType constants correctly produce Number values
- **Complete Device Hash Database**: Updated to include all 1248 devices from Stationpedia
  - HASH() inlay hints now show friendly device names instead of numeric hashes
  - Added previously missing devices like StructureTankSmallInsulated
- **Added LogicType**: `TargetSlotIndex` now recognized in grammar and LSP
- Added support for `get`, `getd`, `put`, `putd` operations in tree-sitter grammar
- Improved type system with Unknown value kind for runtime-determined values (get/pop/peek)
- Register references (rr0-rr15) now treated as implicitly initialized like `sp`

### 🐛 Bug Fixes
- Fixed LogicType semantic highlighting to use orange color in both themes
- Fixed "register read before assign" errors for rr0-rr15 indirect addressing registers
- Fixed device parameter type checking to accept Unknown value kind from get operations
- Fixed db:0-7 network channel type mismatches (channels can store any data type)
- Fixed LogicType parameter validation to accept registers with numeric values
- Fixed label colors to use darker purple (#800080) matching original theme

### 🔧 Improvements
- Optional colon in ignore directive (`# ignore` or `# ignore:` both work)
- Code actions now properly identify register diagnostics for individual suppression
- Better static analysis handling for complex control flow with jumps and loops
- Keybinding changed from Ctrl+Alt+R to Ctrl+Alt+I to avoid conflicts
- Enhanced semantic token colors for consistent LogicType display across themes

## [1.2.1] - 2025-11-15

### 🐛 Bug Fixes
- Fixed VS Code variable resolution for extension path (now works in both VS Code and VS Code Insiders)
- Added proper error handling and helpful error messages when LSP binary is not found
- Enhanced extension startup validation with file existence checks

### 🔧 Improvements
- Improved documentation with comprehensive guides (README, DEVELOPER_GUIDE, CONFIGURATION, QUICK_REFERENCE)
- Enhanced code comments throughout Rust LSP and TypeScript extension
- Better troubleshooting information for common issues

## [1.1.0] - 2025-11-15
- Added new grey shadow text that follows cursor as you type
- Fixed instruction descriptions so they match in game
- Variables & enums now properly display
- Added lots of missing strucutre prefabs and hashs
- HASH("") should properly read as a number now
- ra & sp should no longer incorrectly get marked as having no value
- Labels should be recognized no matter where they are
- Variables should show as a teal color 
- Added hundreds missing variabls & enums
- Got some other fixes in I can't even remember.

## [1.1.0]
- I skipped this by accident so we jumped right to 1.2....

### FlorpyDorp Era 1.0 Changes

## ✨ Features
- HASH() in defines now behaves like a number everywhere
  - `define StartButton HASH("StructureButton")` resolves to a numeric constant
  - Hover/inlay hints show friendly names and values consistently

## 🐛 Bug Fixes
- Fixed LSP crash on startup caused by querying a non-existent `function_call` node
  - Uses `hash_preproc` (as defined by the grammar) for HASH detection/inlays
- Eliminated "Cannot call write after a stream was destroyed" during restarts
  - Guarded client restarts and queued config/diagnostic notifications until the server is running
- Restored operand typing for `hash_preproc` so HASH operands are treated as numbers
- Diagnostics toggle more reliable; reduces stale squiggles and avoids mid-shutdown writes

## 🔧 Developer Notes
- Added targeted regression test for HASH defines recognition
- Updated extension client to await `start()` instead of using `onReady()`