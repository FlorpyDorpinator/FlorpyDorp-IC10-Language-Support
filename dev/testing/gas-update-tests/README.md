# Gas-update test scripts

Manual test IC10 files for the auto-generated data pipeline + the gas update.
Open each in the **dev Extension Development Host** and check the expectations noted
at the top of each file.

| File | What it exercises | Pass = |
|------|-------------------|--------|
| `test_all_instructions_autogen.ic10` | Every instruction (154) with a valid signature, generated from the auto-built `INSTRUCTIONS` map | **No** error squiggles on any line |
| `test_new_gases.ic10` | New `Ratio*` gas logic types (Helium/Silanol/HydrochloricAcid/Ozone, liquids, in/out) | No errors; hover + autocomplete work |
| `test_new_gas_devices.ic10` | New gas devices via `HASH()` (filters, pure ice) | Autocomplete + hover show friendly names |
| `test_alias_deviceid_fix.ic10` | Regression: device `ReferenceId` propagated through an aliased `move` | No false "expected a device" errors |

All data comes from `data/{Enums.json, Stationpedia.json, ProgrammableChip.cs, english.xml}`
via `ic10lsp/build.rs`. See `data/README.md`.
