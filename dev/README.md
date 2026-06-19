# Development Directory

This directory contains development tools and test files for the IC10 Language Server project.

## Directory Structure

```
dev/
├── extractor/                # Python utilities for extracting game data
│   └── StationeersDataExtractor/
│       ├── generate_hashes.py    # Extracts device hashes from game files
│       ├── input/                # Place english.xml here
│       └── output/
│           └── stationpedia.txt  # Generated device hash mappings
├── testing/                      # Test files and test modules
│   ├── *.ic10                    # IC10 test scripts
│   └── *.rs                      # Rust test modules
└── requirements.txt              # Python dependencies for extractor
```

## Data Processing Pipeline

> **RETIRED:** The `generate_hashes.py` → `stationpedia.txt` path described below is **no
> longer used** by the build. Device hashes are now generated directly from
> `data/Stationpedia.json` at compile time. To update game data, just drop the four source
> files into `data/` and run `cargo build` — see [`data/README.md`](../data/README.md).
> The extractor script is kept only as a historical reference.

### How Device Data is Generated (current)

1. **Drop in sources**: place fresh `Enums.json`, `Stationpedia.json`, `ProgrammableChip.cs`,
   and `english.xml` into `data/`.
2. **Build**: `ic10lsp/build.rs` reads those four files and regenerates all device hashes,
   logic types, enums, instruction docs, and descriptions into `OUT_DIR`.
3. **Compile LSP**: the generated maps are `include!`d straight into the language server.

**Data Pipeline (current):** `data/{Enums.json, Stationpedia.json, ProgrammableChip.cs, english.xml}` → `build.rs` → LSP binary

### Legacy extractor (no longer required)

`generate_hashes.py` historically produced `output/stationpedia.txt` from the game's
`english.xml`. The build no longer reads that file. Device names/hashes now come from
`data/Stationpedia.json` (hashes) joined with `data/english.xml` (display names).

## Test Files

The `testing/` directory contains comprehensive test files for various language server features:

### IC10 Test Scripts (*.ic10)
- `test_arrow_display.ic10` - Tests inlay hint arrow display
- `test_battery_devices.ic10` - Tests battery device completion and tooltips
- `test_comprehensive_completion.ic10` - Tests instruction and device completions
- `test_fuzzy_search.ic10` - Tests fuzzy search functionality for HASH() completion
- `test_hash_tooltips.ic10` - Tests device hash hover tooltips
- `test_hover.ic10` - Tests hover information for instructions
- And more...

### Rust Test Modules (*.rs)
- `test_hash_lookup.rs` - Unit tests for device hash lookup functionality
- `test_hash_debug.rs` - Debug utilities for hash value verification

## Running Tests

### Manual Testing
1. Open any `.ic10` file in VS Code with the IC10 extension installed
2. Test features like:
   - Code completion (type "HASH(" and see device suggestions)
   - Hover information (hover over instructions or device hashes)
   - Inlay hints (should show arrow symbols with device names)
   - Diagnostics (syntax errors, length limits)

### Automated Testing (Future)
The test files in this directory are designed to support automated testing:

```bash
# Future CI/CD pipeline commands
cargo test                    # Run Rust unit tests
npm test                     # Run VS Code extension tests (from anex-ic10-language-support/)
```

## Development Workflow

1. **Update game data**: Run `generate_hashes.py` after Stationeers updates
2. **Rebuild LSP**: Changes to `stationpedia.txt` trigger automatic rebuild via `build.rs`
3. **Add test cases**: Create `.ic10` files demonstrating features
4. **Test manually**: Use VS Code to verify language server behavior

## Contributing

When adding new features:

1. Create corresponding test files in `testing/`
2. Document expected behavior in test comments
3. Test both positive and negative cases
4. Update this README if new tools are added

## Notes

- **Automatic code generation**: Device mappings are generated at compile-time by `build.rs`
- **Python required**: Install dependencies with `pip install -r requirements.txt`
- **Game file access**: `generate_hashes.py` can auto-copy from Stationeers install directory
- **Test coverage**: Aim for comprehensive test coverage of language server features

---

**Last Updated**: 2025-11-29
**Maintained By**: IC10 Language Server Development Team