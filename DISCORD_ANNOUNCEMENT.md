# 🚀 FlorpyDorp IC10 Language Support v2.0.0 - Major Release! 🎉

## 🎉 Respawn Update Beta Support
Updated for **Stationeers v0.2.6054.26551** with **1709 devices** (+509 new!)
- ✨ **Full Modular Console Mod support** - All 101 modular devices recognized
- 🆕 **13 new instructions**: atan2, pow, lerp, ext, ins, ld, sd, bdnvl, bdnvs, clr, clrd, poke, rmap
- 📊 **242 logic types** verified - complete orbital mechanics & celestial navigation

## ⚡ Revolutionary HASH() Completions
**Type `HASH("` anywhere and see all 1709 devices instantly!**
- Works in defines: `define Satellite HASH("StructureSatelliteDish")`
- Smart filtering: Type `HASH("Pump")` → see all pumps
- Auto-closes quotes for new HASH calls
- Smart enough not to add duplicate quotes when editing existing HASH
- `HASH("` now suggested at top of list for define/lbn/sbn parameters

## 🎯 Context-Aware Completions
**Dropdowns now show exactly what you need:**
- LogicType/BatchMode: Shows ONLY valid constants (Average, Sum, Maximum, etc.)
- Branch instructions: Shows ONLY labels
- Batch instructions (lb, sb, etc.): Detects device hash parameters perfectly
- **Defines prioritized** - Your device hashes appear first, not buried in register lists
- Usage-based sorting - Items you use appear at the top

## ⚡ Quick Actions & Refactoring
- **HASH Conversion**: Right-click `HASH("Device")` → Convert to hash number (and vice versa!)
- **Branch Fixes**: Convert relative→absolute branches with one click
- **Smart Warnings**: Relative branch to label? Get instant warning + fix

## 💡 Enhanced Inlay Hints
- Parameter hints vanish instantly when you start typing (no more cursor jumping!)
- Device names show at line end: `HASH("StructureVolumePump")` → ` → Volume Pump (-1258351925)`
- Only appears for complete code - doesn't interfere while typing

## 🐛 Major Fixes
- Store batch (sb/sbn/sbs) device hash detection fixed
- HASH() completions work in ALL contexts (not just instructions)
- Inlay hints behave perfectly - no interference with typing flow
- Smart quote handling prevents `HASH("Device")")` duplicates

**Download now and experience the most powerful IC10 editing environment yet!** 🎮✨

*v2.0.0 - Major release celebrating Respawn Update Beta with 500+ new devices, revolutionary completion system, and architectural improvements*
