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

### How Device Data is Generated

1. **Extract from Game**: Run `generate_hashes.py` to extract device hashes from Stationeers' `english.xml`
2. **Generate Code**: The `ic10lsp/build.rs` script automatically reads `stationpedia.txt` and generates Rust code
3. **Compile LSP**: Device mappings are compiled directly into the language server

**Data Pipeline**: `english.xml` → `generate_hashes.py` → `stationpedia.txt` → `build.rs` → LSP binary

### Updating Device Hashes

When Stationeers releases new devices:

```bash
# 1. Navigate to extractor directory
cd dev/extractor/StationeersDataExtractor

# 2. Run the Python script (will prompt to copy game files)
python generate_hashes.py

# 3. Rebuild the language server (automatic via build.rs)
cd ../../../ic10lsp
cargo build
```

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