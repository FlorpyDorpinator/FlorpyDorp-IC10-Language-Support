# Control-flow analysis test scripts (read-before-assign)

Manual test IC10 files for the v2.5 **control-flow-aware** read-before-assign
diagnostic. Open each in the **dev Extension Development Host** (F5) and compare
the red error squiggles against the expectations written at the top of each file.

The analysis builds a per-instruction control-flow graph (basic blocks, jump /
branch / loop edges) and runs a forward *may-be-assigned* dataflow. A read is
flagged **only** when the register is provably never assigned on *any* path that
reaches it — so it removes the false positives the old textual line-order model
produced, while still catching (and now catching *more*) genuine bugs.

Toggle the feature with the setting **`ic10.lsp.enableControlFlowAnalysis`**
(default **on**). Turn it off to fall back to the legacy textual heuristic and
see the difference.

| File | What it exercises | Pass = |
|------|-------------------|--------|
| `01_true_positives.ic10` | Reads of registers never assigned on any path | Red error on each `# ERROR` line, nothing else |
| `02_loop_back_edge.ic10` | Counters/accumulators assigned across loop back edges | **No** red errors |
| `03_init_skip_forward_jump.ic10` | Read appears textually above its assignment, but a jump runs init first | **No** red errors (old model false-positived here) |
| `04_unreachable_assignment.ic10` | The only assignment is in an unreachable block / after the read | Red error on each `# ERROR` line |
| `05_realistic_clean_program.ic10` | Real thermostat+airlock: aliases, yield loop, branches, `jal`/`j ra` subroutine | **No** red errors |

Notes
- Yellow *"assigned but never read"* warnings are a **separate** diagnostic and
  may appear in some files (e.g. `04`); they are expected and unrelated.
- `04` also demonstrates improved precision: the old textual model **missed**
  the `r5` read (its only assignment sits in an unreachable block) — the CFG
  catches it.
- Automated coverage lives in `ic10lsp/src/additional_features.rs` under the
  `cfg_*` unit tests (`cargo test cfg_`).
