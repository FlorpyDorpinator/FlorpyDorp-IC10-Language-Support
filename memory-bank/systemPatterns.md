# System Patterns

## Architecture
- VS Code extension (TypeScript) provides activation, client wiring, completion/hover UX tuning.
- Rust LSP (`ic10lsp`) provides parsing, validation, signatures, hover content, and semantic tokens.
- TextMate grammar + Tree-sitter provide baseline and structural highlighting.

## Key Patterns
- Source of truth for instruction signatures: text files in repo parsed by LSP. We will augment to include operand labels.
- Game data ingestion: parse `Enums.json` (+ optional `Stationpedia.json`) on LSP startup; cache maps for completions/hover.
- Semantic token mapping: enums → constant scopes; labels → entity.name.label; devices/addresses → parameter labels in hovers only.
- Packaging discipline: `prepack` enforces build LSP → build JS → smoke checks → package.

## Data Flow
Text (ic10) → Client sends to LSP → Parse AST → Provide tokens/diagnostics/completions → Client renders with grammar + tokens; hovers show signatures/docs.

## Error Handling
- If JSON not found/invalid: degrade gracefully (skip enums completions) with a single warning in logs.
- Signature parser: ignore unknown labels but keep base signature.

## Testing
- Fixture ic10 scripts in `testing/` for hover and color checks.
- Smoke checks before packaging to confirm presence of key tokens and LSP binary.
