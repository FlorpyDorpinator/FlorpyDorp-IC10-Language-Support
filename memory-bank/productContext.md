# Product Context

## Why This Extension
Stationeers IC10 scripting can be opaque without rich IDE feedback. The extension bridges raw assembly‑like syntax with contextual guidance comparable to in‑game consoles.

## Problems Solved
- Missing/desynchronized instruction signatures vs game reference.
- Confusing ghost/shadow text causing cursor jumps and friction.
- Enums & special identifiers flagged as unknown (noise diagnostics).
- Label references not visually distinct, reducing code legibility.
- Occasional stale build artifacts in published VSIX.

## Desired Experience
- Immediate mnemonic recognition with labeled operand hints.
- Nonintrusive parameter previews (visual aid only; does not steal cursor).
- Enums auto‑complete; show numeric value and optional description.
- Labels consistent color (purple) anywhere they appear.
- One command to build/package with confidence artifacts are current.

## Users
- Intermediate to advanced Stationeers players scripting automation.
- Maintainers & contributors improving IC10 language tooling.

## Differentiators
- Mirrors in‑game naming conventions precisely.
- Deep integration with actual game data files (`Enums.json`, `Stationpedia.json`).
- Emphasis on smooth typing workflow (no layout jitter).

## Risks & Mitigations
| Risk | Impact | Mitigation |
|------|--------|-----------|
| Overlabeling instructions | Cluttered signatures | Curate label set; follow game canonical terms |
| Parsing JSON at runtime cost | Startup latency | Lazy load, cache map, reuse for completions |
| Grammar changes destabilize | Highlight regressions | Minimal additive node for label operands |
| Build script drift | Stale VSIX | Prepack script + smoke checks |

## Metrics (Qualitative for now)
- User reports of cursor bounce eliminated.
- Reduction in "unknown identifier" false positives.
- Positive feedback on clarity of operand labels in hovers/completions.
