use regex::Regex;
use serde_json::Value;
use std::collections::HashSet;
use std::{
    env,
    fs::{self, File},
    io::BufWriter,
    io::Write,
    path::Path,
};

// BUILD SCRIPT - Auto-generates code from game source files
//
// This build.rs script runs during compilation and generates Rust code from
// Stationeers game source files. This ensures the LSP stays up-to-date with
// game updates without manual maintenance.
//
// GENERATED FILES:
// 1. stationpedia.rs - Device names and hashes from stationpedia.txt
// 2. enums_generated.rs - Game enums from Enums.json
// 3. instructions_generated.rs - Logic types, instruction signatures from game sources
//
// SOURCE FILES (data/game-sources/):
// - Enums.json - All game enum definitions (LogicType, SlotLogicType, etc.)
// - Stationpedia.json - Game documentation and descriptions
// - ProgrammableChip.cs - Decompiled game code with instruction signatures
//
// For details on updating game sources, see: AUTO-GENERATION.md

fn main() {
    let out_dir = env::var_os("OUT_DIR").unwrap();
    let dest_path = Path::new(&out_dir).join("stationpedia.rs");

    let mut map_builder = ::phf_codegen::Map::new();
    let mut set_builder = ::phf_codegen::Set::new();
    let mut prefab_to_hash_builder = ::phf_codegen::Map::new();
    let mut hash_to_display_builder = ::phf_codegen::Map::new();
    let mut check_set = std::collections::HashSet::new();

    // Store entries to avoid borrowing issues
    let mut prefab_entries: Vec<(String, String)> = Vec::new();
    let mut hash_entries: Vec<(String, String)> = Vec::new();

    let infile = Path::new("../dev/extractor/StationeersDataExtractor/output/stationpedia.txt");
    let contents = fs::read_to_string(infile).unwrap();

    for line in contents.lines() {
        let line = line.trim();
        if line.is_empty() {
            continue;
        }

        // Parse format: "prefab_name" signed_hash hex_hash "display_name"
        // Find the positions of all quotes
        let quote_positions: Vec<usize> = line.match_indices('"').map(|(i, _)| i).collect();

        if quote_positions.len() >= 4 {
            // Extract the parts between quotes
            let prefab_name = &line[quote_positions[0] + 1..quote_positions[1]];
            let display_name = &line[quote_positions[2] + 1..quote_positions[3]];

            // Find the space-separated parts between the quoted sections
            let middle_part = &line[quote_positions[1] + 1..quote_positions[2]].trim();
            let middle_parts: Vec<&str> = middle_part.split_whitespace().collect();

            if middle_parts.len() >= 2 {
                let hash = middle_parts[0]; // signed_hash

                // Original mapping (hash -> display name)
                map_builder.entry(hash, &format!("\"{}\"", display_name));

                // Store entries for later processing
                let hash_int: i32 = hash.parse().unwrap_or(0);
                prefab_entries.push((prefab_name.to_string(), hash_int.to_string()));
                hash_entries.push((hash_int.to_string(), format!("\"{}\"", display_name)));

                if !check_set.contains(display_name) {
                    set_builder.entry(display_name);
                    check_set.insert(display_name);
                }
            }
        }
    }

    // Build the additional maps
    for (prefab, hash) in &prefab_entries {
        prefab_to_hash_builder.entry(prefab, hash);
    }

    for (hash, display) in &hash_entries {
        hash_to_display_builder.entry(hash, display);
    }

    let output_file = File::create(dest_path).unwrap();
    let mut writer = BufWriter::new(&output_file);

    write!(
        &mut writer,
        "pub(crate) const HASH_NAME_LOOKUP: phf::Map<&'static str, &'static str> = {};\n",
        map_builder.build()
    )
    .unwrap();

    write!(
        &mut writer,
        "pub(crate) const HASH_NAMES: phf::Set<&'static str> = {};\n",
        set_builder.build()
    )
    .unwrap();

    write!(
        &mut writer,
        "pub(crate) const PREFAB_TO_HASH: phf::Map<&'static str, i32> = {};\n",
        prefab_to_hash_builder.build()
    )
    .unwrap();

    write!(
        &mut writer,
        "pub(crate) const HASH_TO_DISPLAY: phf::Map<&'static str, &'static str> = {};\n",
        hash_to_display_builder.build()
    )
    .unwrap();

    println!(
        "cargo:rerun-if-changed=../dev/extractor/StationeersDataExtractor/output/stationpedia.txt"
    );

    // =========================
    // Generate enums from Enums.json
    // =========================
    let enums_out_path = Path::new(&out_dir).join("enums_generated.rs");

    // Helper to escape strings for Rust source
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

    let enums_file = Path::new("../data/game-sources/Enums.json");
    let enums_json =
        fs::read_to_string(enums_file).expect("Failed to read game-sources/Enums.json");
    let v: Value = serde_json::from_str(&enums_json).expect("Failed to parse Enums.json");

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
    // reserved for future validation of duplicate LogicType values
    // let _seen_logic_vals: HashSet<i32> = HashSet::new();

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
                    // Reverse mapping will be derived at runtime by scanning name_to_value map.
                }
            }
        }
    };

    if let Some(script_enums) = v.get("scriptEnums").and_then(|x| x.as_object()) {
        for (family_name, family_obj) in script_enums.iter() {
            process_family(family_name, family_obj);
        }
    }
    if let Some(basic_enums) = v.get("basicEnums").and_then(|x| x.as_object()) {
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
    // (No direct value->name PHF map emitted; use runtime scan helper.)

    println!("cargo:rerun-if-changed=../data/game-sources/Enums.json");

    // =========================
    // Generate instruction signatures and logic types from game sources
    // =========================
    let instructions_out_path = Path::new(&out_dir).join("instructions_generated.rs");
    
    // Read Enums.json for logic types
    let enums_game_file = Path::new("../data/game-sources/Enums.json");
    let enums_game_json = fs::read_to_string(enums_game_file)
        .expect("Failed to read game-sources/Enums.json");
    let enums_game: Value = serde_json::from_str(&enums_game_json)
        .expect("Failed to parse game-sources/Enums.json");
    
    // Read ProgrammableChip.cs for instruction signatures
    let chip_file = Path::new("../data/game-sources/ProgrammableChip.cs");
    let chip_cs = fs::read_to_string(chip_file)
        .expect("Failed to read game-sources/ProgrammableChip.cs");
    
    // Parse instruction signatures from GetCommandExample method
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
    
    // Extract LogicType
    if let Some(script_enums) = enums_game.get("scriptEnums").and_then(|x| x.as_object()) {
        if let Some(logic_type) = script_enums.get("LogicType") {
            if let Some(values) = logic_type.get("values").and_then(|x| x.as_object()) {
                for (name, data) in values.iter() {
                    let deprecated = data.get("deprecated").and_then(|x| x.as_bool()).unwrap_or(false);
                    if !deprecated {
                        logic_types_builder.entry(name);
                        let desc = data.get("description").and_then(|x| x.as_str()).unwrap_or("");
                        logic_type_docs_builder.entry(name, &format!("\"{}\"", escape_str(desc)));
                    }
                }
            }
        }
        
        // Extract LogicSlotType
        if let Some(slot_logic_type) = script_enums.get("LogicSlotType") {
            if let Some(values) = slot_logic_type.get("values").and_then(|x| x.as_object()) {
                for (name, data) in values.iter() {
                    let deprecated = data.get("deprecated").and_then(|x| x.as_bool()).unwrap_or(false);
                    if !deprecated {
                        slot_logic_types_builder.entry(name);
                        let desc = data.get("description").and_then(|x| x.as_str()).unwrap_or("");
                        slot_type_docs_builder.entry(name, &format!("\"{}\"", escape_str(desc)));
                    }
                }
            }
        }
        
        // Extract LogicBatchMethod
        if let Some(batch_mode) = script_enums.get("LogicBatchMethod") {
            if let Some(values) = batch_mode.get("values").and_then(|x| x.as_object()) {
                for (name, data) in values.iter() {
                    let deprecated = data.get("deprecated").and_then(|x| x.as_bool()).unwrap_or(false);
                    if !deprecated {
                        batch_modes_builder.entry(name);
                        let desc = data.get("description").and_then(|x| x.as_str()).unwrap_or("");
                        batch_mode_docs_builder.entry(name, &format!("\"{}\"", escape_str(desc)));
                    }
                }
            }
        }
        
        // Extract LogicReagentMode
        if let Some(reagent_mode) = script_enums.get("LogicReagentMode") {
            if let Some(values) = reagent_mode.get("values").and_then(|x| x.as_object()) {
                for (name, data) in values.iter() {
                    let deprecated = data.get("deprecated").and_then(|x| x.as_bool()).unwrap_or(false);
                    if !deprecated {
                        reagent_modes_builder.entry(name);
                        let desc = data.get("description").and_then(|x| x.as_str()).unwrap_or("");
                        reagent_mode_docs_builder.entry(name, &format!("\"{}\"", escape_str(desc)));
                    }
                }
            }
        }
    }
    
    // Write instructions_generated.rs
    let mut inst_writer = BufWriter::new(
        File::create(instructions_out_path).expect("Failed to create instructions_generated.rs")
    );
    
    writeln!(&mut inst_writer, "// Auto-generated from game sources - DO NOT EDIT").unwrap();
    writeln!(&mut inst_writer, "// This file is included directly into instructions.rs").unwrap();
    writeln!(&mut inst_writer, "// Do not add 'use' statements here - they belong in instructions.rs").unwrap();
    writeln!(&mut inst_writer, "").unwrap();
    
    // Write LOGIC_TYPES
    writeln!(
        &mut inst_writer,
        "pub const LOGIC_TYPES: phf::Set<&'static str> = {};",
        logic_types_builder.build()
    ).unwrap();
    writeln!(&mut inst_writer, "").unwrap();
    
    // Write LOGIC_TYPE_DOCS
    writeln!(
        &mut inst_writer,
        "pub const LOGIC_TYPE_DOCS: phf::Map<&'static str, &'static str> = {};",
        logic_type_docs_builder.build()
    ).unwrap();
    writeln!(&mut inst_writer, "").unwrap();
    
    // Write SLOT_LOGIC_TYPES
    writeln!(
        &mut inst_writer,
        "pub const SLOT_LOGIC_TYPES: phf::Set<&'static str> = {};",
        slot_logic_types_builder.build()
    ).unwrap();
    writeln!(&mut inst_writer, "").unwrap();
    
    // Write SLOT_TYPE_DOCS
    writeln!(
        &mut inst_writer,
        "pub const SLOT_TYPE_DOCS: phf::Map<&'static str, &'static str> = {};",
        slot_type_docs_builder.build()
    ).unwrap();
    writeln!(&mut inst_writer, "").unwrap();
    
    // Write BATCH_MODES
    writeln!(
        &mut inst_writer,
        "pub const BATCH_MODES: phf::Set<&'static str> = {};",
        batch_modes_builder.build()
    ).unwrap();
    writeln!(&mut inst_writer, "").unwrap();
    
    // Write BATCH_MODE_DOCS
    writeln!(
        &mut inst_writer,
        "pub const BATCH_MODE_DOCS: phf::Map<&'static str, &'static str> = {};",
        batch_mode_docs_builder.build()
    ).unwrap();
    writeln!(&mut inst_writer, "").unwrap();
    
    // Write REAGENT_MODES
    writeln!(
        &mut inst_writer,
        "pub const REAGENT_MODES: phf::Set<&'static str> = {};",
        reagent_modes_builder.build()
    ).unwrap();
    writeln!(&mut inst_writer, "").unwrap();
    
    // Write REAGENT_MODE_DOCS
    writeln!(
        &mut inst_writer,
        "pub const REAGENT_MODE_DOCS: phf::Map<&'static str, &'static str> = {};",
        reagent_mode_docs_builder.build()
    ).unwrap();
    writeln!(&mut inst_writer, "").unwrap();
    
    // Write instruction signatures
    writeln!(&mut inst_writer, "// Instruction signatures").unwrap();
    writeln!(&mut inst_writer, "pub(crate) const INSTRUCTION_SIGNATURES: &[(&str, &[&[&str]])] = &[").unwrap();
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
    
    println!("cargo:rerun-if-changed=../data/game-sources/ProgrammableChip.cs");
    println!("cargo:rerun-if-changed=../data/game-sources/Stationpedia.json");
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
    let switch_end = method_code[switch_start..].find("default:").unwrap_or(method_code.len() - switch_start);
    let switch_end = switch_end + method_code[switch_start + switch_end..].find('}').unwrap_or(0) + 1;
    
    let switch_body = &method_code[switch_start..std::cmp::min(switch_start + switch_end, method_code.len())];
    
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
