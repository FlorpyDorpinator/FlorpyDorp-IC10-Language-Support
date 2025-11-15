# Active Context

## Current Focus (Nov 2025)
Live testing pipeline is now priority: launch configurations + tasks enable rapid rebuild of Rust LSP and extension. Operand labels, enum integration, label reference coloring, and case‑insensitive opcode handling have been implemented. Remaining work: comprehensive in‑editor verification (TASK007), packaging reliability (TASK006), and polishing any residual hover/diagnostic edge cases.

## Recent Work
- Added `.vscode/launch.json` (multi‑config: local/remote LSP, attach, listening server).
- Replaced malformed `.vscode/tasks.json` with sequential build tasks (`build: ic10lsp (debug)` -> copy binary -> `esbuild`).
- Patched Rust LSP for persistent parameter inlay hints, labeled syntax in completion/hover/signature help, and case‑insensitive instruction lookup.
- Semantic tokens classify branch labels (TYPE) and dotted enums (ENUM).

## Decisions (Confirmed)
- Centralize parameter label generation via `tooltip_documentation::get_instruction_syntax`.
- Completions insert mnemonic + space only; signature shown in item detail & hover.
- Keep build warnings for now (non‑blocking) until verification finishes.
- Use tasks dependency chain instead of ad‑hoc scripts for consistency across dev machines.

## Open Items
- TASK006: Prepack reliability (release build + smoke checks) not started.
- TASK007: Systematic verification sweep across test scripts pending.
- Potential cleanup of unreachable pattern warnings after functional verification.

## Immediate Next Steps
1. Run "Run Extension (Local LSP)" and validate test `.ic10` scripts (hover, completion, inlay hints).
2. Capture any missing operand labels or incorrect colorization; log in TASK007.
3. Draft prepack script + smoke checks (binary presence, instruction maps) for TASK006.

## Risk / Watch List
- Malformed tasks.json fixed; verify no leftover broken labels in other developer clones.
- Ensure remote LSP config works once `--listen` server launched (port 9257).
- Watch for performance impact of heuristic label generation on very large files.

## Hand‑Off Summary
Environment now supports rapid iterative testing. Proceed with verification before refactoring warnings.
