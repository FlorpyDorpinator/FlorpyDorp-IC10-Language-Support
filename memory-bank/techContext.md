# Tech Context

## Languages & Tools
- Rust (LSP): cargo build; produces `ic10lsp.exe`.
- TypeScript (VS Code extension): built via esbuild; outputs `out/main.js`.
- PowerShell build environment (execution policy: must use `npm.cmd` / `npx.cmd`).
- TextMate grammar (`ic10.tmLanguage.json`), Tree-sitter grammar for structural parsing.

## Dependencies
- Node.js (npm scripts, vsce packaging).
- `@vscode/vsce` for VSIX packaging.
- Rust crates (parse logic; specifics TBD on inspection).

## Build Order Requirements
1. `cargo build --release` for LSP.
2. Copy `target/release/ic10lsp.exe` → extension `bin/`.
3. `npm.cmd install` (ensure dependencies).
4. `npm.cmd run esbuild` (produce latest `out/main.js`).
5. Smoke test: search for key tokens (e.g., `ReferenceId`).
6. `npx.cmd vsce package`.

## Constraints
- Windows path handling; prefer absolute paths.
- Avoid stale binaries (must rebuild if signatures or parsing code change).

## Planned Additions
- `prepack` script to enforce proper ordering.
- Enum ingestion logic (Rust) sourcing `Enums.json`.
- Signature augmentation logic for operand labels.
