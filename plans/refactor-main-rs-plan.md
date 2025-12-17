## Plan: Refactor main.rs into Modular Components

Your main.rs is too long at 5,399 lines! This plan breaks it into logical, maintainable modules organized by LSP feature areas.

### Summary
Split main.rs into 6 new modules: completion, diagnostics, hover/inlay-hints, other handlers, tree utilities, and a refactored main. This will reduce main.rs from 5,399 lines to ~250 lines while improving code organization and maintainability.

**Phases 6**

1. **Phase 1: Extract Completion Provider Module**
    - **Objective:** Create `src/lsp_completion.rs` containing all completion logic (~1,700 lines)
    - **Files/Functions to Modify/Create:**
        - Create [src/lsp_completion.rs](src/lsp_completion.rs)
        - Extract helper functions: `instruction_completions`, `param_completions_static`, `param_completions_builtin`, `param_completions_dynamic`, `enum_completions`
        - Extract main `completion` handler method
        - Update [main.rs](main.rs) to import and delegate to new module
    - **Tests to Write:**
        - Test helper compilation succeeds
        - Test LSP completion still works through extension
        - Test HASH() device name completions work
    - **Steps:**
        1. Write tests (build succeeds, extension loads)
        2. Run tests to see them fail (module doesn't exist yet)
        3. Create `src/lsp_completion.rs` with helper functions and `handle_completion` function
        4. Update Backend in main.rs to call `lsp_completion::handle_completion`
        5. Add proper imports and make Backend fields accessible
        6. Run tests to confirm they pass

2. **Phase 2: Extract Diagnostics Module**
    - **Objective:** Create `src/lsp_diagnostics.rs` containing all diagnostic generation (~1,100 lines)
    - **Files/Functions to Modify/Create:**
        - Create [src/lsp_diagnostics.rs](src/lsp_diagnostics.rs)
        - Extract `run_diagnostics`, `check_types` functions
        - Extract diagnostic constants (LINT_ABSOLUTE_JUMP, LINT_RELATIVE_BRANCH_TO_LABEL)
        - Consolidate `compute_diagnostics_for_text` (remove duplication with CLI mode)
        - Update [main.rs](main.rs) to import and use new module
    - **Tests to Write:**
        - Test diagnostics module compiles
        - Test diagnostics still fire on syntax errors
        - Test hash diagnostics can be toggled
    - **Steps:**
        1. Write tests (build, diagnostics work)
        2. Run tests to see them fail
        3. Create `src/lsp_diagnostics.rs` with `run_diagnostics` and `check_types`
        4. Update Backend to call `lsp_diagnostics::run_diagnostics`
        5. Replace CLI duplicate logic with calls to new module
        6. Run tests to confirm pass

3. **Phase 3: Extract Hover and Inlay Hints Module**
    - **Objective:** Create `src/lsp_hover_inlay.rs` for hover and inlay hint providers (~600 lines)
    - **Files/Functions to Modify/Create:**
        - Create [src/lsp_hover_inlay.rs](src/lsp_hover_inlay.rs)
        - Extract `hover` handler method
        - Extract `inlay_hint` handler method
        - Update [main.rs](main.rs) to import new module
    - **Tests to Write:**
        - Test module compiles
        - Test hover tooltips work
        - Test inlay hints appear for HASH() calls
    - **Steps:**
        1. Write tests (build, hover/inlay work)
        2. Run tests to see failure
        3. Create `src/lsp_hover_inlay.rs` with both handler functions
        4. Update Backend to delegate hover and inlay_hint to new module
        5. Run tests to confirm pass

4. **Phase 4: Extract Other LSP Handlers Module**
    - **Objective:** Create `src/lsp_handlers.rs` for remaining handlers (~700 lines)
    - **Files/Functions to Modify/Create:**
        - Create [src/lsp_handlers.rs](src/lsp_handlers.rs)
        - Extract: `initialize`, `initialized`, `shutdown`, `did_open`, `did_change`, `did_change_configuration`, `execute_command`
        - Extract: `semantic_tokens_full`, `document_symbol`, `signature_help`, `code_action`, `goto_definition`
        - Update [main.rs](main.rs) to import module
    - **Tests to Write:**
        - Test handlers module compiles
        - Test document lifecycle works (open/change files)
        - Test semantic tokens appear
    - **Steps:**
        1. Write tests (build, lifecycle)
        2. Run tests to see failure
        3. Create `src/lsp_handlers.rs` with all remaining handler methods
        4. Update Backend impl LanguageServer to delegate to new module
        5. Run tests to confirm pass

5. **Phase 5: Extract Tree Utilities Module**
    - **Objective:** Create `src/tree_utils.rs` for tree-sitter helpers (~200 lines)
    - **Files/Functions to Modify/Create:**
        - Create [src/tree_utils.rs](src/tree_utils.rs)
        - Extract `node_at_position`, `node_at_range` functions
        - Extract `NodeEx` trait and implementation
        - Update [main.rs](main.rs) and other modules to import
    - **Tests to Write:**
        - Test tree utils module compiles
        - Test NodeEx trait methods work
        - Test node_at_position finds correct nodes
    - **Steps:**
        1. Write tests (build, trait methods)
        2. Run tests to see failure
        3. Create `src/tree_utils.rs` with NodeEx trait and helper functions
        4. Update all modules using tree utilities to import from new module
        5. Run tests to confirm pass

6. **Phase 6: Final Main.rs Cleanup**
    - **Objective:** Reduce main.rs to ~250 lines with just Backend struct and module coordination
    - **Files/Functions to Modify/Create:**
        - Refactor [main.rs](main.rs) to minimal Backend struct + module imports
        - Update Backend impl to delegate all methods to respective modules
        - Keep constants at top level or move to appropriate modules
        - Ensure all pub/pub(crate) visibility correct
    - **Tests to Write:**
        - Full integration test: build + run extension
        - Test all LSP features work (completion, hover, diagnostics, inlay hints)
        - Run benchmarking to verify no performance regression
    - **Steps:**
        1. Write integration tests
        2. Run to establish baseline
        3. Clean up main.rs - remove extracted code, keep Backend struct
        4. Verify all module imports correct
        5. Run full test suite
        6. Run benchmarks to compare performance

**Open Questions**
1. Should we make Backend fields public or create accessor methods? (Recommend: pub(crate) fields for simplicity)
2. Should tree_utils.rs be in a `utils/` subdirectory? (Recommend: flat for now, can refine later)
3. Should we extract the data type union constants to a separate file? (Recommend: yes, create `src/type_unions.rs`)
4. Do you want the CLI duplicate diagnostic logic consolidated in this refactor? (Recommend: yes, DRY principle)
5. Should we create unit tests as part of this refactor or just integration tests? (Recommend: integration tests only to minimize scope)
