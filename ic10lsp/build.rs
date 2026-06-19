use regex::Regex;
use serde_json::Value;
use std::collections::{HashMap, HashSet};
use std::{
    env,
    fs::{self, File},
    io::BufWriter,
    io::Write,
    path::Path,
};
use quick_xml::events::Event;
use quick_xml::Reader;

// BUILD SCRIPT - Auto-generates code from game source files
//
// This build.rs runs during compilation and regenerates all game-derived data
// for the language server from FOUR source files in `data/`. Drop in fresh files
// after a Stationeers update, run `cargo build`, and the LSP is up to date.
//
// SOURCE FILES (all in ../data/):
//   - Enums.json          Logic types, slot types, batch/reagent modes, all enums (incl. GasType)
//   - Stationpedia.json   Device PrefabName -> PrefabHash, and IC10 instruction descriptions
//   - ProgrammableChip.cs Decompiled chip code -> instruction parameter signatures (reference)
//   - english.xml         Human-readable device display names + descriptions
//
// GENERATED FILES (written to OUT_DIR, included via include! in src/):
//   - enums_generated.rs        <- Enums.json                 (included by instructions.rs)
//   - instructions_generated.rs <- Enums.json + ProgrammableChip.cs + Stationpedia.json
//   - device_hashes_generated.rs<- Stationpedia.json + english.xml (included by device_hashes.rs)
//   - descriptions_generated.rs <- english.xml                (included by descriptions.rs)
//
// For details on updating game sources, see: ../data/README.md

// Escape a string so it can be embedded inside a Rust "..." literal.
fn escape_str(s: &str) -> String {
    let mut out = String::with_capacity(s.len() + 8);
    for ch in s.chars() {
        match ch {
            '\\' => out.push_str("\\\\"),
            '"' => out.push_str("\\\""),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            _ => out.push(ch),
        }
    }
    out
}

fn main() {
    let out_dir = env::var_os("OUT_DIR").unwrap();

    // ---- The four source files (single source of truth) ----
    let enums_file = Path::new("../data/Enums.json");
    let stationpedia_file = Path::new("../data/Stationpedia.json");
    let chip_file = Path::new("../data/ProgrammableChip.cs");
    let english_file = Path::new("../data/english.xml");

    // Rebuild whenever any source file changes.
    println!("cargo:rerun-if-changed=../data/Enums.json");
    println!("cargo:rerun-if-changed=../data/Stationpedia.json");
    println!("cargo:rerun-if-changed=../data/ProgrammableChip.cs");
    println!("cargo:rerun-if-changed=../data/english.xml");

    // Parse the JSON sources once and reuse.
    let enums_json = fs::read_to_string(enums_file).expect("Failed to read data/Enums.json");
    let enums: Value = serde_json::from_str(&enums_json).expect("Failed to parse data/Enums.json");

    let pedia_json =
        fs::read_to_string(stationpedia_file).expect("Failed to read data/Stationpedia.json");
    let pedia: Value =
        serde_json::from_str(&pedia_json).expect("Failed to parse data/Stationpedia.json");

    // =========================
    // Generate enums from Enums.json  ->  enums_generated.rs
    // =========================
    let enums_out_path = Path::new(&out_dir).join("enums_generated.rs");

    // Builders
    let mut enum_value_by_name = ::phf_codegen::Map::new();
    let mut enum_desc_by_name = ::phf_codegen::Map::new();
    let mut enum_deprecated = ::phf_codegen::Set::new();
    let mut logic_name_to_value = ::phf_codegen::Map::new();

    // Stage entries first to avoid borrow/lifetime issues
    let mut enum_value_by_name_entries: Vec<(String, String)> = Vec::new();
    let mut enum_desc_by_name_entries: Vec<(String, String)> = Vec::new();
    let mut enum_deprecated_entries: Vec<String> = Vec::new();
    let mut logic_name_to_value_entries: Vec<(String, String)> = Vec::new();

    let mut seen_qnames: HashSet<String> = HashSet::new();
    let mut seen_logic_names: HashSet<String> = HashSet::new();

    let mut process_family = |family_name: &str, family_obj: &Value| {
        if let Some(values) = family_obj.get("values").and_then(|x| x.as_object()) {
            for (member_name, member) in values.iter() {
                let val = member.get("value").and_then(|x| x.as_i64()).unwrap_or(0) as i32;
                let desc = member
                    .get("description")
                    .and_then(|x| x.as_str())
                    .unwrap_or("");
                let deprecated = member
                    .get("deprecated")
                    .and_then(|x| x.as_bool())
                    .unwrap_or(false);

                let qname = format!("{}.{}", family_name, member_name);
                if seen_qnames.insert(qname.clone()) {
                    // Keys must not be additionally quoted; phf_codegen will add quotes itself.
                    enum_value_by_name_entries.push((qname.clone(), format!("{}i32", val)));
                    enum_desc_by_name_entries
                        .push((qname.clone(), format!("\"{}\"", escape_str(desc))));
                    if deprecated {
                        enum_deprecated_entries.push(qname);
                    }
                }

                if family_name == "LogicType" {
                    if seen_logic_names.insert(member_name.clone()) {
                        logic_name_to_value_entries
                            .push((member_name.clone(), format!("{}i32", val)));
                    }
                }
            }
        }
    };

    if let Some(script_enums) = enums.get("scriptEnums").and_then(|x| x.as_object()) {
        for (family_name, family_obj) in script_enums.iter() {
            process_family(family_name, family_obj);
        }
    }
    if let Some(basic_enums) = enums.get("basicEnums").and_then(|x| x.as_object()) {
        for (family_name, family_obj) in basic_enums.iter() {
            process_family(family_name, family_obj);
        }
    }

    // Populate builders
    for (k, v) in enum_value_by_name_entries.iter() {
        enum_value_by_name.entry(k, v);
    }
    for (k, v) in enum_desc_by_name_entries.iter() {
        enum_desc_by_name.entry(k, v);
    }
    for k in enum_deprecated_entries.iter() {
        enum_deprecated.entry(k);
    }
    for (k, v) in logic_name_to_value_entries.iter() {
        logic_name_to_value.entry(k, v);
    }

    let mut w =
        BufWriter::new(File::create(enums_out_path).expect("Failed to create enums_generated.rs"));
    writeln!(
        &mut w,
        "pub(crate) const ENUM_VALUE_BY_NAME: phf::Map<&'static str, i32> = {};",
        enum_value_by_name.build()
    )
    .unwrap();
    writeln!(
        &mut w,
        "pub(crate) const ENUM_DESC_BY_NAME: phf::Map<&'static str, &'static str> = {};",
        enum_desc_by_name.build()
    )
    .unwrap();
    writeln!(
        &mut w,
        "pub(crate) const ENUM_DEPRECATED: phf::Set<&'static str> = {};",
        enum_deprecated.build()
    )
    .unwrap();
    writeln!(
        &mut w,
        "pub(crate) const LOGIC_TYPE_NAME_TO_VALUE: phf::Map<&'static str, i32> = {};",
        logic_name_to_value.build()
    )
    .unwrap();

    // =========================
    // Generate logic types + instruction data  ->  instructions_generated.rs
    //   Logic/slot/batch/reagent sets+docs : from Enums.json
    //   INSTRUCTION_SIGNATURES             : from ProgrammableChip.cs (reference signatures)
    //   INSTRUCTION_DOCS                   : from Stationpedia.json scriptCommands
    // =========================
    let instructions_out_path = Path::new(&out_dir).join("instructions_generated.rs");

    // Read ProgrammableChip.cs for instruction signatures
    let chip_cs = fs::read_to_string(chip_file).expect("Failed to read data/ProgrammableChip.cs");
    let instruction_sigs = parse_instruction_signatures(&chip_cs);

    // Build logic types from Enums.json
    let mut logic_types_builder = ::phf_codegen::Set::new();
    let mut logic_type_docs_builder = ::phf_codegen::Map::new();
    let mut slot_logic_types_builder = ::phf_codegen::Set::new();
    let mut slot_type_docs_builder = ::phf_codegen::Map::new();
    let mut batch_modes_builder = ::phf_codegen::Set::new();
    let mut batch_mode_docs_builder = ::phf_codegen::Map::new();
    let mut reagent_modes_builder = ::phf_codegen::Set::new();
    let mut reagent_mode_docs_builder = ::phf_codegen::Map::new();

    if let Some(script_enums) = enums.get("scriptEnums").and_then(|x| x.as_object()) {
        // LogicType
        if let Some(logic_type) = script_enums.get("LogicType") {
            if let Some(values) = logic_type.get("values").and_then(|x| x.as_object()) {
                for (name, data) in values.iter() {
                    let deprecated =
                        data.get("deprecated").and_then(|x| x.as_bool()).unwrap_or(false);
                    if !deprecated {
                        logic_types_builder.entry(name);
                        let desc = data.get("description").and_then(|x| x.as_str()).unwrap_or("");
                        logic_type_docs_builder.entry(name, &format!("\"{}\"", escape_str(desc)));
                    }
                }
            }
        }

        // LogicSlotType
        if let Some(slot_logic_type) = script_enums.get("LogicSlotType") {
            if let Some(values) = slot_logic_type.get("values").and_then(|x| x.as_object()) {
                for (name, data) in values.iter() {
                    let deprecated =
                        data.get("deprecated").and_then(|x| x.as_bool()).unwrap_or(false);
                    if !deprecated {
                        slot_logic_types_builder.entry(name);
                        let desc = data.get("description").and_then(|x| x.as_str()).unwrap_or("");
                        slot_type_docs_builder.entry(name, &format!("\"{}\"", escape_str(desc)));
                    }
                }
            }
        }

        // LogicBatchMethod
        if let Some(batch_mode) = script_enums.get("LogicBatchMethod") {
            if let Some(values) = batch_mode.get("values").and_then(|x| x.as_object()) {
                for (name, data) in values.iter() {
                    let deprecated =
                        data.get("deprecated").and_then(|x| x.as_bool()).unwrap_or(false);
                    if !deprecated {
                        batch_modes_builder.entry(name);
                        let desc = data.get("description").and_then(|x| x.as_str()).unwrap_or("");
                        batch_mode_docs_builder.entry(name, &format!("\"{}\"", escape_str(desc)));
                    }
                }
            }
        }

        // LogicReagentMode
        if let Some(reagent_mode) = script_enums.get("LogicReagentMode") {
            if let Some(values) = reagent_mode.get("values").and_then(|x| x.as_object()) {
                for (name, data) in values.iter() {
                    let deprecated =
                        data.get("deprecated").and_then(|x| x.as_bool()).unwrap_or(false);
                    if !deprecated {
                        reagent_modes_builder.entry(name);
                        let desc = data.get("description").and_then(|x| x.as_str()).unwrap_or("");
                        reagent_mode_docs_builder.entry(name, &format!("\"{}\"", escape_str(desc)));
                    }
                }
            }
        }
    }

    // Build INSTRUCTION_DOCS from Stationpedia.json scriptCommands (name -> desc)
    let mut instruction_docs_entries: Vec<(String, String)> = Vec::new();
    if let Some(cmds) = pedia.get("scriptCommands").and_then(|x| x.as_object()) {
        for (name, data) in cmds.iter() {
            let desc = data.get("desc").and_then(|x| x.as_str()).unwrap_or("");
            instruction_docs_entries.push((name.clone(), format!("\"{}\"", escape_str(desc))));
        }
    }
    instruction_docs_entries.sort_by(|a, b| a.0.cmp(&b.0));
    let mut instruction_docs_builder = ::phf_codegen::Map::new();
    for (k, v) in instruction_docs_entries.iter() {
        instruction_docs_builder.entry(k, v);
    }

    // Write instructions_generated.rs
    let mut inst_writer = BufWriter::new(
        File::create(instructions_out_path).expect("Failed to create instructions_generated.rs"),
    );

    writeln!(&mut inst_writer, "// Auto-generated from game sources - DO NOT EDIT").unwrap();
    writeln!(&mut inst_writer, "// This file is included directly into instructions.rs").unwrap();
    writeln!(
        &mut inst_writer,
        "// Do not add 'use' statements here - they belong in instructions.rs"
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();

    writeln!(
        &mut inst_writer,
        "pub const LOGIC_TYPES: phf::Set<&'static str> = {};",
        logic_types_builder.build()
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();
    writeln!(
        &mut inst_writer,
        "pub const LOGIC_TYPE_DOCS: phf::Map<&'static str, &'static str> = {};",
        logic_type_docs_builder.build()
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();
    writeln!(
        &mut inst_writer,
        "pub const SLOT_LOGIC_TYPES: phf::Set<&'static str> = {};",
        slot_logic_types_builder.build()
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();
    writeln!(
        &mut inst_writer,
        "pub const SLOT_TYPE_DOCS: phf::Map<&'static str, &'static str> = {};",
        slot_type_docs_builder.build()
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();
    writeln!(
        &mut inst_writer,
        "pub const BATCH_MODES: phf::Set<&'static str> = {};",
        batch_modes_builder.build()
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();
    writeln!(
        &mut inst_writer,
        "pub const BATCH_MODE_DOCS: phf::Map<&'static str, &'static str> = {};",
        batch_mode_docs_builder.build()
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();
    writeln!(
        &mut inst_writer,
        "pub const REAGENT_MODES: phf::Set<&'static str> = {};",
        reagent_modes_builder.build()
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();
    writeln!(
        &mut inst_writer,
        "pub const REAGENT_MODE_DOCS: phf::Map<&'static str, &'static str> = {};",
        reagent_mode_docs_builder.build()
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();

    // INSTRUCTION_DOCS (hover descriptions) from Stationpedia.json
    writeln!(
        &mut inst_writer,
        "pub const INSTRUCTION_DOCS: phf::Map<&'static str, &'static str> = {};",
        instruction_docs_builder.build()
    )
    .unwrap();
    writeln!(&mut inst_writer).unwrap();

    // INSTRUCTIONS — the type-checking signature map — generated from Stationpedia.json
    // examples. Each operand's color encodes its type and "|" marks a union; IC10 semantics
    // are applied (a numeric operand also accepts a register; a mode operand also accepts a
    // number). A small override table (signature_override) reproduces the handful of cases
    // where real behavior is broader/narrower than the example. New instructions added by a
    // game update get a signature automatically.
    let span_re = Regex::new(r"<color=([^>]+)>([^<]*)</color>").unwrap();
    let mut instr_sig_entries: Vec<(String, Vec<Vec<&'static str>>)> = Vec::new();
    if let Some(cmds) = pedia.get("scriptCommands").and_then(|x| x.as_object()) {
        for (name, data) in cmds.iter() {
            let sig = match signature_override(name) {
                Some(ov) => ov,
                None => {
                    let example = data.get("example").and_then(|x| x.as_str()).unwrap_or("");
                    parse_example_signature(example, &span_re)
                }
            };
            instr_sig_entries.push((name.clone(), sig));
        }
    }
    instr_sig_entries.sort_by(|a, b| a.0.cmp(&b.0));

    writeln!(
        &mut inst_writer,
        "pub const INSTRUCTIONS: phf::Map<&'static str, InstructionSignature> = phf_map! {{"
    )
    .unwrap();
    for (name, params) in instr_sig_entries.iter() {
        write!(&mut inst_writer, "    \"{}\" => InstructionSignature(&[", name).unwrap();
        for (i, union) in params.iter().enumerate() {
            if i > 0 {
                write!(&mut inst_writer, ", ").unwrap();
            }
            write!(&mut inst_writer, "Union(&[").unwrap();
            for (j, ty) in union.iter().enumerate() {
                if j > 0 {
                    write!(&mut inst_writer, ", ").unwrap();
                }
                write!(&mut inst_writer, "DataType::{}", ty).unwrap();
            }
            write!(&mut inst_writer, "])").unwrap();
        }
        writeln!(&mut inst_writer, "]),").unwrap();
    }
    writeln!(&mut inst_writer, "}};").unwrap();
    writeln!(&mut inst_writer).unwrap();

    // Instruction parameter signatures parsed from ProgrammableChip.cs.
    // Reference data: the authoritative type-checking map lives in instructions.rs
    // (hand-tuned for register/number unions). Use this when adding new instructions.
    writeln!(&mut inst_writer, "// Instruction signatures (reference)").unwrap();
    writeln!(&mut inst_writer, "#[allow(dead_code)]").unwrap();
    writeln!(
        &mut inst_writer,
        "pub(crate) const INSTRUCTION_SIGNATURES: &[(&str, &[&[&str]])] = &["
    )
    .unwrap();
    for (cmd, params) in instruction_sigs.iter() {
        write!(&mut inst_writer, "    (\"{}\", &[", cmd).unwrap();
        for (i, param_types) in params.iter().enumerate() {
            if i > 0 {
                write!(&mut inst_writer, ", ").unwrap();
            }
            write!(&mut inst_writer, "&[").unwrap();
            for (j, ptype) in param_types.iter().enumerate() {
                if j > 0 {
                    write!(&mut inst_writer, ", ").unwrap();
                }
                write!(&mut inst_writer, "\"{}\"", ptype).unwrap();
            }
            write!(&mut inst_writer, "]").unwrap();
        }
        writeln!(&mut inst_writer, "]),").unwrap();
    }
    writeln!(&mut inst_writer, "];").unwrap();

    // =========================
    // Generate device hashes  ->  device_hashes_generated.rs
    //   DEVICE_NAME_TO_HASH  : PrefabName -> PrefabHash      (from Stationpedia.json)
    //   HASH_TO_DISPLAY_NAME : PrefabHash -> display name    (english.xml, falls back to prefab)
    // =========================
    let device_hashes_out_path = Path::new(&out_dir).join("device_hashes_generated.rs");
    let english_names = parse_english_names(english_file);

    let mut name_to_hash: Vec<(String, i32)> = Vec::new();
    let mut hash_to_display: Vec<(i32, String)> = Vec::new();
    let mut seen_names: HashSet<String> = HashSet::new();
    let mut seen_hashes: HashSet<i32> = HashSet::new();

    if let Some(pages) = pedia.get("pages").and_then(|x| x.as_array()) {
        for page in pages.iter() {
            let prefab = page.get("PrefabName").and_then(|x| x.as_str()).unwrap_or("");
            if prefab.is_empty() {
                continue;
            }
            let hash = match page.get("PrefabHash").and_then(|x| x.as_i64()) {
                Some(h) if h != 0 => h as i32,
                _ => continue,
            };

            // Prefer the human-readable english.xml name; fall back to a literal
            // Stationpedia Title (skip "<N:EN:...>" localization tokens); else prefab.
            let display = english_names
                .get(prefab)
                .filter(|s| !s.is_empty())
                .cloned()
                .or_else(|| {
                    page.get("Title")
                        .and_then(|x| x.as_str())
                        .filter(|t| !t.is_empty() && !t.starts_with('<'))
                        .map(|t| t.to_string())
                })
                .unwrap_or_else(|| prefab.to_string());

            if seen_names.insert(prefab.to_string()) {
                name_to_hash.push((prefab.to_string(), hash));
            }
            if seen_hashes.insert(hash) {
                hash_to_display.push((hash, display));
            }
        }
    }

    name_to_hash.sort_by(|a, b| a.0.cmp(&b.0));
    hash_to_display.sort_by(|a, b| a.0.cmp(&b.0));

    let mut dh_writer = BufWriter::new(
        File::create(device_hashes_out_path).expect("Failed to create device_hashes_generated.rs"),
    );
    writeln!(
        &mut dh_writer,
        "// Auto-generated from data/Stationpedia.json (hashes) + data/english.xml (names) - DO NOT EDIT"
    )
    .unwrap();
    writeln!(
        &mut dh_writer,
        "pub static DEVICE_NAME_TO_HASH: phf::Map<&'static str, i32> = phf_map! {{"
    )
    .unwrap();
    for (name, hash) in name_to_hash.iter() {
        writeln!(&mut dh_writer, "    \"{}\" => {}i32,", escape_str(name), hash).unwrap();
    }
    writeln!(&mut dh_writer, "}};").unwrap();
    writeln!(&mut dh_writer).unwrap();
    writeln!(
        &mut dh_writer,
        "pub static HASH_TO_DISPLAY_NAME: phf::Map<i32, &'static str> = phf_map! {{"
    )
    .unwrap();
    for (hash, display) in hash_to_display.iter() {
        writeln!(&mut dh_writer, "    {}i32 => \"{}\",", hash, escape_str(display)).unwrap();
    }
    writeln!(&mut dh_writer, "}};").unwrap();

    // =========================
    // Generate device descriptions  ->  descriptions_generated.rs  (from english.xml)
    // =========================
    let descriptions_out_path = Path::new(&out_dir).join("descriptions_generated.rs");
    let descriptions = parse_english_xml(english_file);

    let mut device_descriptions_builder = ::phf_codegen::Map::new();
    let mut seen_desc_keys: HashSet<&str> = HashSet::new();
    for (prefab_name, (display_name, description)) in descriptions.iter() {
        // english.xml can contain duplicate Keys; keep the first (phf panics on dupes).
        if !seen_desc_keys.insert(prefab_name.as_str()) {
            continue;
        }
        let escaped_display = escape_str(display_name);
        let escaped_desc = escape_str(description);
        device_descriptions_builder.entry(
            prefab_name,
            &format!("(\"{}\", \"{}\")", escaped_display, escaped_desc),
        );
    }

    let mut desc_writer = BufWriter::new(
        File::create(descriptions_out_path).expect("Failed to create descriptions_generated.rs"),
    );
    writeln!(&mut desc_writer, "// Auto-generated from english.xml - DO NOT EDIT").unwrap();
    writeln!(&mut desc_writer, "// Device descriptions from game localization files").unwrap();
    writeln!(&mut desc_writer).unwrap();
    writeln!(
        &mut desc_writer,
        "pub(crate) const DEVICE_DESCRIPTIONS: phf::Map<&'static str, (&'static str, &'static str)> = {};",
        device_descriptions_builder.build()
    )
    .unwrap();
}

// Parse instruction signatures from ProgrammableChip.cs GetCommandExample method
fn parse_instruction_signatures(cs_code: &str) -> Vec<(String, Vec<Vec<String>>)> {
    let mut instructions = Vec::new();

    // Find the GetCommandExample method
    let method_start = match cs_code.find("public static string GetCommandExample") {
        Some(pos) => pos,
        None => return instructions,
    };
    let method_code = &cs_code[method_start..];

    // Find the switch statement
    let switch_start = match method_code.find("switch (command)") {
        Some(pos) => pos,
        None => return instructions,
    };

    // Find the end of the method more carefully
    let switch_end = method_code[switch_start..]
        .find("default:")
        .unwrap_or(method_code.len() - switch_start);
    let switch_end = switch_end + method_code[switch_start + switch_end..].find('}').unwrap_or(0) + 1;

    let switch_body =
        &method_code[switch_start..std::cmp::min(switch_start + switch_end, method_code.len())];

    // Parse each case
    let case_regex = Regex::new(r"case ScriptCommand\.([a-z0-9]+):").unwrap();
    let helpstring_regex = Regex::new(
        r"ProgrammableChip\.(REGISTER|DEVICE_INDEX|LOGIC_TYPE|LOGIC_SLOT_TYPE|BATCH_MODE|REAGENT_MODE|NUMBER|INTEGER|STRING|DEVICE_HASH|NAME_HASH|SLOT_INDEX|REF_ID)"
    ).unwrap();

    let lines: Vec<&str> = switch_body.lines().collect();
    let mut i = 0;

    while i < lines.len() {
        let line = lines[i].trim();

        if let Some(caps) = case_regex.captures(line) {
            let cmd = caps.get(1).unwrap().as_str().to_string();

            // Look ahead to find the HelpString array
            let mut params: Vec<Vec<String>> = Vec::new();
            let mut j = i + 1;
            let mut in_array = false;

            while j < lines.len() && j < i + 30 {
                let array_line = lines[j];

                if array_line.contains("ProgrammableChip.HelpString[]") {
                    in_array = true;
                }

                if in_array {
                    // Extract parameter types from this line
                    for cap in helpstring_regex.captures_iter(array_line) {
                        let ptype = cap.get(1).unwrap().as_str();
                        let mapped_type = map_helpstring_to_datatype(ptype);

                        // Check if this is part of a union (+ operator on same line)
                        if array_line.contains(" + ") {
                            // This is a union type - collect all types on this logical line
                            let mut union_types = vec![mapped_type.to_string()];

                            // Scan for all types in this union expression
                            for cap2 in helpstring_regex.captures_iter(array_line) {
                                let ptype2 = cap2.get(1).unwrap().as_str();
                                let mapped2 = map_helpstring_to_datatype(ptype2);
                                if !union_types.contains(&mapped2.to_string()) {
                                    union_types.push(mapped2.to_string());
                                }
                            }
                            params.push(union_types);
                            break; // Move to next parameter
                        } else {
                            params.push(vec![mapped_type.to_string()]);
                        }
                    }

                    if array_line.contains("});") {
                        break;
                    }
                }

                j += 1;
            }

            // Handle multiple case labels for same signature (fallthrough)
            let mut commands = vec![cmd];
            let mut k = i + 1;
            while k < lines.len() && lines[k].trim().starts_with("case ScriptCommand.") {
                if let Some(caps2) = case_regex.captures(lines[k]) {
                    commands.push(caps2.get(1).unwrap().as_str().to_string());
                }
                k += 1;
            }

            // Add all commands with this signature
            for command in commands {
                instructions.push((command, params.clone()));
            }
        }

        i += 1;
    }

    instructions
}

// Parse a Stationpedia `scriptCommands[].example` string into a list of operand unions.
// The example is color-coded: each operand is one or more `<color=..>token</color>` spans,
// with a gray `|` span separating union members. Returns DataType variant names per operand.
fn parse_example_signature(example: &str, span_re: &Regex) -> Vec<Vec<&'static str>> {
    let mut params: Vec<Vec<&'static str>> = Vec::new();
    for (idx, chunk) in example.split_whitespace().enumerate() {
        if idx == 0 {
            continue; // first token is the mnemonic itself (yellow)
        }
        let mut types: Vec<&'static str> = Vec::new();
        for cap in span_re.captures_iter(chunk) {
            let color = cap[1].to_lowercase();
            let text = cap[2].trim().to_lowercase();
            for &ty in color_to_datatypes(&color, &text) {
                if !types.contains(&ty) {
                    types.push(ty);
                }
            }
        }
        if !types.is_empty() {
            params.push(types);
        }
    }
    params
}

// Map an example operand's color (and text, for the orange enum family) to DataType variants.
// IC10 semantics are baked in: numeric operands also accept a register; batch/reagent mode
// operands also accept a number (and reagent mode a register).
fn color_to_datatypes(color: &str, text: &str) -> &'static [&'static str] {
    match color {
        "#0080ffff" => &["Register"],            // r?
        "#20b2aa" => &["Number", "Register"],    // num / int / id / *Hash / slotIndex
        "green" => &["Device"],                   // d?
        "white" => &["Name"],                     // str
        "orange" => match text {
            "logictype" => &["LogicType"],
            "logicslottype" => &["SlotLogicType"],
            "batchmode" => &["BatchMode", "Number"],
            "reagentmode" => &["ReagentMode", "Number", "Register"],
            _ => &[],
        },
        _ => &[], // gray "|" separator, mnemonic, or anything unexpected
    }
}

// The handful of instructions whose real validation differs from the literal example
// (broader: a stored value can be a literal; device-by-id forms; narrower: sdns/sdse take a
// device only; the `label` pseudo-op). Reproduces the long-standing hand-tuned signatures.
fn signature_override(name: &str) -> Option<Vec<Vec<&'static str>>> {
    let sig: Vec<Vec<&'static str>> = match name {
        "clr" => vec![vec!["Device", "Number", "Register"]],
        "label" => vec![vec!["Name"], vec!["Register", "Device"]],
        "ld" => vec![
            vec!["Register"],
            vec!["Device", "Number", "Register"],
            vec!["LogicType"],
        ],
        "s" => vec![
            vec!["Device", "Number", "Register"],
            vec!["LogicType"],
            vec!["Number", "Register"],
        ],
        "sb" => vec![
            vec!["Number", "Register"],
            vec!["LogicType"],
            vec!["Number", "Register"],
        ],
        "sbn" => vec![
            vec!["Number", "Register"],
            vec!["Number", "Register"],
            vec!["LogicType"],
            vec!["Number", "Register"],
        ],
        "sd" => vec![
            vec!["Device", "Number", "Register"],
            vec!["LogicType"],
            vec!["Number", "Register"],
        ],
        "sdns" => vec![vec!["Register"], vec!["Device"]],
        "sdse" => vec![vec!["Register"], vec!["Device"]],
        "ss" => vec![
            vec!["Device", "Number", "Register"],
            vec!["Number", "Register"],
            vec!["SlotLogicType"],
            vec!["Number", "Register"],
        ],
        _ => return None,
    };
    Some(sig)
}

fn map_helpstring_to_datatype(helpstring: &str) -> &str {
    match helpstring {
        "REGISTER" => "Register",
        "DEVICE_INDEX" => "Device",
        "LOGIC_TYPE" => "LogicType",
        "LOGIC_SLOT_TYPE" => "SlotLogicType",
        "BATCH_MODE" => "BatchMode",
        "REAGENT_MODE" => "ReagentMode",
        "NUMBER" => "Number",
        "INTEGER" => "Number",
        "STRING" => "Name",
        "DEVICE_HASH" => "Number",
        "NAME_HASH" => "Number",
        "SLOT_INDEX" => "Number",
        "REF_ID" => "Number",
        _ => "Number",
    }
}

// Parse english.xml to extract device names and descriptions (only entries with a description).
fn parse_english_xml(xml_path: &Path) -> Vec<(String, (String, String))> {
    let xml_content = fs::read_to_string(xml_path).expect("Failed to read english.xml");

    let mut reader = Reader::from_str(&xml_content);
    reader.trim_text(true);

    let mut results = Vec::new();
    let mut buf = Vec::new();

    let mut current_key = String::new();
    let mut current_value = String::new();
    let mut current_description = String::new();
    let mut in_things_section = false;
    let mut in_record_thing = false;
    let mut in_key = false;
    let mut in_value = false;
    let mut in_description = false;

    loop {
        match reader.read_event_into(&mut buf) {
            Ok(Event::Start(ref e)) => match e.name().as_ref() {
                b"Things" => in_things_section = true,
                b"RecordThing" => {
                    if in_things_section {
                        in_record_thing = true;
                        current_key.clear();
                        current_value.clear();
                        current_description.clear();
                    }
                }
                b"Key" => {
                    if in_record_thing {
                        in_key = true;
                    }
                }
                b"Value" => {
                    if in_record_thing {
                        in_value = true;
                    }
                }
                b"Description" => {
                    if in_record_thing {
                        in_description = true;
                    }
                }
                _ => {}
            },
            Ok(Event::Text(e)) => {
                let text = e.unescape().unwrap_or_default().to_string();
                if in_key {
                    current_key.push_str(&text);
                } else if in_value {
                    current_value.push_str(&text);
                } else if in_description {
                    current_description.push_str(&text);
                }
            }
            Ok(Event::End(ref e)) => match e.name().as_ref() {
                b"Things" => in_things_section = false,
                b"RecordThing" => {
                    if in_record_thing && !current_key.is_empty() {
                        // Only store if we have a description
                        if !current_description.is_empty() {
                            let cleaned_desc = clean_description(&current_description);
                            results.push((
                                current_key.clone(),
                                (current_value.clone(), cleaned_desc),
                            ));
                        }
                    }
                    in_record_thing = false;
                }
                b"Key" => in_key = false,
                b"Value" => in_value = false,
                b"Description" => in_description = false,
                _ => {}
            },
            Ok(Event::Eof) => break,
            Err(e) => {
                eprintln!(
                    "Error parsing english.xml at position {}: {:?}",
                    reader.buffer_position(),
                    e
                );
                break;
            }
            _ => {}
        }
        buf.clear();
    }

    results
}

// Parse english.xml into a PrefabName -> display-name map (ALL entries, even without a description).
fn parse_english_names(xml_path: &Path) -> HashMap<String, String> {
    let xml_content = fs::read_to_string(xml_path).expect("Failed to read english.xml");

    let mut reader = Reader::from_str(&xml_content);
    reader.trim_text(true);

    let mut map = HashMap::new();
    let mut buf = Vec::new();

    let mut current_key = String::new();
    let mut current_value = String::new();
    let mut in_things_section = false;
    let mut in_record_thing = false;
    let mut in_key = false;
    let mut in_value = false;

    loop {
        match reader.read_event_into(&mut buf) {
            Ok(Event::Start(ref e)) => match e.name().as_ref() {
                b"Things" => in_things_section = true,
                b"RecordThing" => {
                    if in_things_section {
                        in_record_thing = true;
                        current_key.clear();
                        current_value.clear();
                    }
                }
                b"Key" => {
                    if in_record_thing {
                        in_key = true;
                    }
                }
                b"Value" => {
                    if in_record_thing {
                        in_value = true;
                    }
                }
                _ => {}
            },
            Ok(Event::Text(e)) => {
                let text = e.unescape().unwrap_or_default().to_string();
                if in_key {
                    current_key.push_str(&text);
                } else if in_value {
                    current_value.push_str(&text);
                }
            }
            Ok(Event::End(ref e)) => match e.name().as_ref() {
                b"Things" => in_things_section = false,
                b"RecordThing" => {
                    if in_record_thing && !current_key.is_empty() {
                        map.insert(current_key.clone(), current_value.clone());
                    }
                    in_record_thing = false;
                }
                b"Key" => in_key = false,
                b"Value" => in_value = false,
                _ => {}
            },
            Ok(Event::Eof) => break,
            Err(_) => break,
            _ => {}
        }
        buf.clear();
    }

    map
}

// Clean up description text by removing game-specific formatting tags
fn clean_description(desc: &str) -> String {
    let mut result = desc.to_string();

    // Remove {LINK:text;display} tags - keep the display part
    let link_regex = Regex::new(r"\{LINK:([^;]+);([^}]+)\}").unwrap();
    result = link_regex.replace_all(&result, "$2").to_string();

    // Remove {THING:prefab} tags - keep the prefab name
    let thing_regex = Regex::new(r"\{THING:([^}]+)\}").unwrap();
    result = thing_regex.replace_all(&result, "$1").to_string();

    // Remove {GAS:name} tags - keep the gas name
    let gas_regex = Regex::new(r"\{GAS:([^}]+)\}").unwrap();
    result = gas_regex.replace_all(&result, "$1").to_string();

    // Remove {HEADER:text} tags - keep the text in uppercase
    let header_regex = Regex::new(r"\{HEADER:([^}]+)\}").unwrap();
    result = header_regex
        .replace_all(&result, |caps: &regex::Captures| caps[1].to_uppercase())
        .to_string();

    // Remove any remaining curly brace tags
    let generic_regex = Regex::new(r"\{[^}]+\}").unwrap();
    result = generic_regex.replace_all(&result, "").to_string();

    // Clean up excessive whitespace
    let whitespace_regex = Regex::new(r"\s+").unwrap();
    result = whitespace_regex.replace_all(result.trim(), " ").to_string();

    result
}
