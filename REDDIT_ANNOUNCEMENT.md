# FlorpyDorp IC10 Language Support v2.0.0 - Major Release!

![FlorpyDorp IC10 Extension](https://raw.githubusercontent.com/FlorpyDorpinator/IC10-Code-Extension/main/Stationeers-ic10-main/FlorpyDorp%20IC10/FlorpyDorp%20Language%20Support/images/icon.png)

I'm excited to announce **v2.0.0** of the FlorpyDorp IC10 Language Support extension for VS Code! This is a major release with revolutionary features that fundamentally change how you write IC10 code for Stationeers.

## 🎉 Respawn Update Beta Support

Fully updated for **Stationeers v0.2.6054.26551** (Respawn Update Beta):
- **1709 device hashes** (+509 new devices!)
- ✨ **Full Modular Console Mod support** - All 101 modular console devices recognized
- 🆕 **13 new instructions**: atan2, pow, lerp, ext, ins, ld, sd, bdnvl, bdnvs, clr, clrd, poke, rmap
- 📊 **242 logic types** verified - complete orbital mechanics & celestial navigation support

## ⚡ Revolutionary HASH() Completions

![HASH Completions Demo](https://raw.githubusercontent.com/FlorpyDorpinator/IC10-Code-Extension/main/Stationeers-ic10-main/FlorpyDorp%20IC10/FlorpyDorp%20Language%20Support/images/completion-demo.gif)

This is the game-changer: **Type `HASH("` anywhere in your code and instantly see all 1709 devices with fuzzy search!**

- Works everywhere: defines, instructions, batch operations, you name it
- Example: `define Satellite HASH("StructureSatelliteDish")` - just start typing and pick from the list
- Smart filtering: Type `HASH("Pump")` and see all pump devices
- Auto-closes quotes for new HASH calls
- Detects existing closing quotes to prevent duplicates like `HASH("Device")")`
- `HASH("` automatically suggested at the top of completion lists for define, lbn, and sbn parameters

No more alt-tabbing to look up device names or hash values!

## 🎯 Context-Aware Completions

![Context-Aware Completions](https://raw.githubusercontent.com/FlorpyDorpinator/IC10-Code-Extension/main/Stationeers-ic10-main/FlorpyDorp%20IC10/FlorpyDorp%20Language%20Support/images/hover-demo.gif)

Completion dropdowns now understand what parameter you're typing and show **only** relevant options:

- **LogicType/BatchMode parameters**: Shows ONLY their valid constants (Average, Sum, Maximum, Minimum, etc.) - no more scrolling through irrelevant items
- **Branch instructions**: Shows ONLY labels from your code
- **Batch instructions** (lb, lbn, sb, sbn, etc.): Perfectly detects which parameter expects device hashes
- **Defines prioritized**: Your defined constants appear at the top of numeric parameter lists, not buried under registers
- **Usage-based sorting**: Items you've already used in your script float to the top automatically

## ⚡ Quick Actions & Refactoring

![Code Actions Demo](https://raw.githubusercontent.com/FlorpyDorpinator/IC10-Code-Extension/main/Stationeers-ic10-main/FlorpyDorp%20IC10/FlorpyDorp%20Language%20Support/images/code-actions-demo.gif)

New code actions make refactoring effortless:

- **HASH Conversion**: Right-click on `HASH("StructureVolumePump")` → Refactor → Convert to hash number (-1258351925), and vice versa!
- **Branch Fixes**: Convert relative branches to absolute with one click
- **Smart Warnings**: Using a relative branch to a label? Get an instant warning with a quick-fix (this prevents the common mistake of using `breq/brne/brlt` with labels instead of their absolute counterparts)

## 💡 Enhanced Inlay Hints

![Inlay Hints Demo](https://raw.githubusercontent.com/FlorpyDorpinator/IC10-Code-Extension/main/Stationeers-ic10-main/FlorpyDorp%20IC10/FlorpyDorp%20Language%20Support/images/inlay-hints-demo.gif)

Inlay hints (shadow text) have been completely reworked:

- **Parameter type hints** appear as you type an instruction, then vanish instantly when you start typing parameters
- **No more cursor jumping** - hints disappear before they can interfere with your typing flow
- **Device name hints** show at the end of the line for HASH() calls and numeric hashes
  - Example: `HASH("StructureVolumePump")` displays ` → Volume Pump (-1258351925)` in gray at the end of the line
  - Numeric hashes show friendly names: `-1258351925` displays ` → Volume Pump`
- Only appears for complete, valid code - won't clutter your screen while you're still typing

## 🐛 Major Bug Fixes

![Device Hash Support](https://raw.githubusercontent.com/FlorpyDorpinator/IC10-Code-Extension/main/Stationeers-ic10-main/FlorpyDorp%20IC10/FlorpyDorp%20Language%20Support/images/device-hash-demo.gif)

- **Store batch instructions** (sb, sbn, sbs) now correctly detect device hash parameter at position 0
- **Load batch instructions** (lb, lbn, lbs, lbns) correctly detect device hash parameter at position 1
- **HASH() completions** now work in ALL contexts (defines, inline, everywhere) - not just after instructions
- **Inlay hints** behavior fixed - no interference with typing flow or cursor position
- **Smart quote handling** prevents duplicate closing parentheses when editing existing HASH() calls
- **Usage-based sorting** now works correctly for all parameter positions, not just the first

## 🔗 Download & Installation

**VS Code Marketplace**: [FlorpyDorp IC10 Language Support](https://marketplace.visualstudio.com/items?itemName=FlorpyDorp.florpydorp-ic10-language-support)

**GitHub Repository**: [https://github.com/FlorpyDorpinator/IC10-Code-Extension](https://github.com/FlorpyDorpinator/IC10-Code-Extension)

Install directly in VS Code:
1. Open VS Code
2. Press Ctrl+Shift+X (Extensions)
3. Search for "FlorpyDorp IC10"
4. Click Install

Or install from the command palette:
```
ext install FlorpyDorp.florpydorp-ic10-language-support
```

## 🎨 Features Overview

For those new to the extension, here's what you get:

- **Full IC10 syntax highlighting** with two immersive color themes (IC10 In-Game Colors and Stationeers Full Color Theme)
- **Intelligent code completion** with context awareness and usage-based sorting
- **Hover documentation** with multiple examples for 80+ instructions
- **Device hash support** for 1709+ devices with instant tooltips
- **Real-time diagnostics** with smart error detection
- **Signature help** showing parameter types as you code
- **Go to definition** for labels and variables
- **Code actions** for quick fixes and refactoring
- **Inlay hints** for parameter types and device names
- **Multi-platform support**: Windows, Linux, macOS (Intel & Apple Silicon)

## 🙏 Credits

This extension builds on amazing community work:
- Anexgohan - Earlier IC10 extension foundations
- Xandaros - Original ic10lsp language server
- awilliamson - First IC10 VS Code extension
- The IC10 community for documentation, testing, and feedback...particularly Niv.

## 📝 Full Changelog

Complete changelog available in the [CHANGELOG.md](https://github.com/FlorpyDorpinator/IC10-Code-Extension/blob/main/Stationeers-ic10-main/FlorpyDorp%20IC10/FlorpyDorp%20Language%20Support/CHANGELOG.md) on GitHub.

---

**What's your favorite feature from this update?** I'd love to hear your feedback and suggestions for future improvements!

Happy coding, and see you in the stars! 🚀✨
