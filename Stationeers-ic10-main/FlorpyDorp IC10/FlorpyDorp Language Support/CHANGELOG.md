### Changelog Beginning 11-01-2025

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
  - "Stationeers Dark" → "Stationeers Full Color Theme" (complete UI + syntax)
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
- **Theme Toggle Command**: Press **Ctrl+Alt+T** to switch between Stationeers Dark and your previous theme
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
- Eliminated “Cannot call write after a stream was destroyed” during restarts
  - Guarded client restarts and queued config/diagnostic notifications until the server is running
- Restored operand typing for `hash_preproc` so HASH operands are treated as numbers
- Diagnostics toggle more reliable; reduces stale squiggles and avoids mid-shutdown writes

## 🔧 Developer Notes
- Added targeted regression test for HASH defines recognition
- Updated extension client to await `start()` instead of using `onReady()`
