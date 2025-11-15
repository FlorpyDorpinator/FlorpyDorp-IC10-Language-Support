# IC10 Language Support for Stationeers

## Downloads

Download the latest VS Code IC10 Extension:
- **[Windows](https://github.com/Anexgohan/Stationeers-ic10/releases/latest/download/ic10-language-support-Windows.vsix)**
- **[Linux](https://github.com/Anexgohan/Stationeers-ic10/releases/latest/download/ic10-language-support-Linux.vsix)**
- **[macOS](https://github.com/Anexgohan/Stationeers-ic10/releases/latest/download/ic10-language-support-macOS.vsix)**

---

This Visual Studio Code extension provides syntax highlighting, IntelliSense, and other language support features for the IC10 MIPS-like language used in the game Stationeers. Uses the Language Server [ic10lsp](https://github.com/Anexgohan/Stationeers-ic10/tree/main/ic10lsp)

## Features

- **Syntax highlighting** for IC10 language files (`.ic10`)
- **Code autocompletion** with intelligent suggestions for all IC10 instructions
- **🆕 Enhanced Hover Documentation** - Comprehensive instruction guides with examples and history
  - **Complete Instruction Coverage** - Documentation for all 80+ IC10 instructions including batch operations
  - **Multi-Example Learning** - 3+ examples per instruction (simple → intermediate → advanced)
  - **Register Operation History** - See chronological history of all operations performed on registers
  - **Syntax-Highlighted Examples** - Proper IC10 code rendering in hover tooltips
  - **Categorized Discovery** - 8 instruction categories (Arithmetic, Device I/O, Batch Operations, etc.)
  - **Related Instructions** - Quick discovery of similar commands through enhanced tooltips
- **Signature help** for function parameters and instruction usage
- **Go to definition** for labels and variables
- **Diagnostics** with comprehensive syntax error detection and code length limits
- **HASH() Function Support** - Advanced device hash calculations with inline hints
  - `define Pump HASH("StructureVolumePump")` → Shows "Volume Pump" inline hint
  - `define Sensor -1252983604` → Shows "Gas Sensor" inline hint  
  - **100+ devices supported** including all common IC10 automation devices
  - **Smart typo handling** (e.g., "StructurePipeAnalysizer" works correctly)
  - **Hover tooltips** show device names for hash values in your code
- **Enhanced Language Server** with improved performance and stability
- **Code length validation** - Warns when approaching Stationeers' 4096-byte limit
- **Comprehensive instruction database** with latest Stationeers updates

## FlorpyDorp additions

This repository contains a rebranded and enhanced fork with additional UX and language-server improvements contributed under the "FlorpyDorp" updates. Key additions include:

- Runtime diagnostics toggle and immediate UX improvements
  - A runtime diagnostics toggle (configurable in the extension) clears squiggles immediately and restarts the language server so diagnostics stay in sync with the editor.

- Completion and identifier handling
  - Static parameter completions (LogicType, SlotLogicType, BatchMode) are case-insensitive and tolerant of leading spaces.
  - Static completions use a token-like icon for better visual parity with the in-game tokens.

- New logic / batch tokens
  - Expanded instruction database with additional logic tokens such as ReferenceId, BestContactFilter, CelestialHash, EntityState, Apex, VelocityX/Y/Z, Orientation, Forward, Density, TotalQuantity, MinedQuantity, Channel0–Channel7 and more.

- Type inference and ReferenceId acceptance
  - Loads (l, lb, lbn and friends) now accept `ReferenceId` and certain aggregator batch modes and will mark registers that hold ReferenceId values as DeviceId where appropriate — reducing false-positive diagnostics.

- Hover and signature improvements
  - Hover tooltips better preserve and display IC10-style code blocks and examples. Special-case handling ensures logic-type names (e.g., `ReferenceId`) show helpful inline docs even when parsed as identifiers.

- Inlay hints and UI polish
  - Inlay hints were repositioned to appear to the right of typed operands so they don't obstruct typing and to mirror in-game signature hints.

- Tests, build and packaging niceties
  - Added tests and example IC10 files that exercise ReferenceId/lb/lbn patterns. The extension build process and a VS Code build task were also added to simplify packaging.


## Installation

1. Open Visual Studio Code.
2. Press `Ctrl+P` to open the Quick Open dialog.
3. Type `ext install vsix`
4. Select the latest version of the extension and press `Enter`.
5. Once the extension is installed, you can start using it by opening `.ic10` files.

![how-to-install-in-vscode](images/how-to-install-in-vscode.png)
![how-to-install-in-vscode](how-to-install-in-vscode.gif)

## Usage

After installing the extension, you can use it by opening any `.ic10` file in Visual Studio Code. The extension will automatically activate and provide syntax highlighting and language features for your IC10 MIPS-like code.

### Key Features in Action:
- ### **Enhanced Hover Documentation**: Comprehensive instruction guides with examples and operation history
  - Complete coverage of all IC10 instructions including batch operations (lbn, sbn, lbs, etc.)
  - Multiple examples per instruction from simple to advanced patterns
  - Register operation history showing chronological timeline of register usage
  - Syntax-highlighted code examples in hover tooltips
- ### **Smart Completions**: Type instruction names to see all available options with documentation
  ![fuzzy search](images/fuzzy-search.gif)
- ### **Inline Hints**: inline hints for device hashes in your code
  ![inline hints](images/inline-hints.png)
- ### **HASH() Tooltips**: Hover over device hashes to see friendly device names
  ![tooltips](images/tooltips.png)
- ### **Error Detection**: Real-time syntax checking and 4096-byte limit warnings
  Error-Detection-line-length
  ![Error-Detection-line-length](images/Error-Detection-line-length.png)
  Error-Detection-4098-size-128-line-size
  ![Error-Detection-4098-size-128-line-limit](images/Error-Detection-4098-size-128-line-size.png)

- ### **Quick Smart Suggestions**: Smart suggestions for all IC10 instructions
  ![suggestions for ic10 and hover text](/images/smart_suggestions.png)

### Configuration

The extension supports several VS Code settings:
- `ic10.lsp.max_lines`: Maximum lines allowed (default: 128)
- `ic10.lsp.max_columns`: Maximum columns per line (default: 90)  
- `ic10.lsp.max_bytes`: Maximum total bytes (default: 4096)
- `ic10.useRemoteLanguageServer`: Use remote LSP server for development

## Contributing

If you find any issues or have suggestions for improvements, please [open an issue on GitHub](https://github.com/Anexgohan/Stationeers-ic10/issues).

## License

This extension is released under the [MIT License](https://opensource.org/licenses/MIT). See the [LICENSE](https://github.com/Anexgohan/Stationeers-ic10/blob/master/LICENSE) file for more information.

## Special Thanks

This project is a fork and modification of the great work done by the following individuals:

- **[Xandaros](https://github.com/Xandaros)** for the original `ic10lsp` language server: [https://github.com/Xandaros/ic10lsp](https://github.com/Xandaros/ic10lsp)
- **[awilliamson](https://github.com/awilliamson)** for the original `ic10-language-support` VSCode extension: [https://github.com/awilliamson/ic10-language-support](https://github.com/awilliamson/ic10-language-support)