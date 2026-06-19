// Device name <-> hash maps for HASH() support (completion + hover tooltips).
//
// AUTO-GENERATED at build time - DO NOT EDIT BY HAND.
//   DEVICE_NAME_TO_HASH  : PrefabName  -> PrefabHash   (from data/Stationpedia.json)
//   HASH_TO_DISPLAY_NAME : PrefabHash  -> display name (from data/english.xml, falls back to prefab)
//
// To update after a Stationeers patch: drop new files into data/ and `cargo build`.
// See ic10lsp/build.rs and data/README.md.

use phf::phf_map;

include!(concat!(env!("OUT_DIR"), "/device_hashes_generated.rs"));
