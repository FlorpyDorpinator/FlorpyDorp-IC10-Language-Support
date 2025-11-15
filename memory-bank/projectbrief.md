# Project Brief

**Project:** FlorpyDorp IC10 VS Code Extension (Language + LSP)
**Goal:** Provide rich authoring support (syntax, hover, completion, semantic tokens, validation) for Stationeers IC10 scripts aligning closely with in‑game presentation and semantics.

## Core Objectives
- Accurate instruction signatures mirroring Stationeers in‑game help (parameter labels like `device`, `address`, `label`, `value`).
- Stable authoring UX (no cursor jump on ghost text/instruction preview overlays).
- Full recognition of enums, variables, and game data (printerInstruction.*, logic types, device constants) for hover + completion + colorization.
- Consistent label semantics: declaration and operand references colored uniformly and navigable.
- Reliable deterministic build/publish pipeline ensuring latest Rust LSP + JS bundle packaged.

## Success Criteria
- Typing `get ` shows suggestion/hover: `get r? device(d?|r?|id) address(r?|num)` (not losing labels).
- Cursor never force‑moves beyond helpful shadow/inlay text while typing parameters.
- All enum identifiers recognized (no "unknown identifier" diagnostics) and show value on hover.
- Labels inside instructions color purple and support definition/rename.
- Local VSIX build reproducibly contains up‑to‑date `out/main.js` and `bin/ic10lsp.exe`.

## Non‑Goals (Current Phase)
- Multi‑platform packaging beyond Windows (can be future).
- Advanced performance optimizations inside parser beyond current needs.
- Refactoring entire grammar unless required for label operand recognition.

## Constraints
- Must parse existing `Enums.json` & `Stationpedia.json` for authoritative enum/value data.
- Maintain backward compatibility for existing instruction parsing (avoid breaking user scripts).
- Windows PowerShell build environment; script execution policy quirks.

## Stakeholders
- IC10 script authors seeking improved ergonomics.
- Maintainers ensuring publish stability.

## Timeline (Current Session)
Implement four feature fixes + prepack reliability then verify in-editor.
