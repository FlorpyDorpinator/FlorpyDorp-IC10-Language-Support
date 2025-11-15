use serde_json::Value;
use std::collections::HashSet;
use std::{
    env,
    fs::{self, File},
    io::BufWriter,
    io::Write,
    path::Path,
};

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

    let enums_file = Path::new("../../../data/Enums.json");
    let enums_json =
        fs::read_to_string(enums_file).expect("Failed to read Enums.json (expected at data folder)");
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

    println!("cargo:rerun-if-changed=../../../data/Enums.json");
}
