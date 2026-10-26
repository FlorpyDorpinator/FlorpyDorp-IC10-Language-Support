//! # Extra instructions
//!
//! Instructions that have been added by a mod.
//!
//! ```json
//! {
//!   "name": "Example Math",
//!   "instructions": {
//!     "avg": {
//!       "description": "Puts the average of a, b and (if given) c into r?.",
//!       "operands": [
//!         { "name": "dest", "type": "r?", "role": "write" },
//!         { "name": "a", "type": "r?|num" },
//!         { "name": "b", "type": "r?|num" },
//!         { "name": "c", "type": "r?|num", "optional": true }
//!       ]
//!     }
//!   }
//! }
//! ```

use std::collections::HashMap;
use std::sync::{LazyLock, RwLock};

use serde::Deserialize;

use crate::instructions::{DataType, InstructionSignature, Union, INSTRUCTIONS};

/// How control leaves an extra instruction.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Flow {
    /// Always goes to the jump operand, as `j` does.
    Jump,
    /// Goes to the jump operand or on to the next line, as `beq` does.
    Branch,
    /// Goes to the jump operand, and comes back to the next line, as `jal` does.
    Call,
    /// Goes back to the line after the call, as `j ra` does.
    Return,
    /// Stops the chip, as `hcf` does.
    Halt,
}

/// What the analysis needs to know about an extra instruction.
#[derive(Clone, Copy, Debug)]
pub struct Shape {
    /// Whether it writes its first operand, as `move` does; every other operand is read.
    pub writes_first: bool,
    /// The operand that's a jump target: a line, label or register.
    pub jump: Option<usize>,
    /// How control leaves it; `None` runs on to the next line.
    pub flow: Option<Flow>,
    /// Operands whose value it also puts in a register (a call's arguments): (operand, register).
    pub into: &'static [(usize, &'static str)],
}

/// One registered extra instruction.
#[derive(Debug)]
pub struct ExtraInstruction {
    pub name: &'static str,
    pub signature: &'static InstructionSignature,
    pub description: &'static str,
    /// Each operand's name, for hovers and hints (`a(r?|num)`).
    pub operand_names: &'static [&'static str],
    /// The fewest operands it takes; the most is the signature's length.
    pub min_operands: usize,
    pub shape: Shape,
    /// The mod it's from, for hovers: the file's `"name"`, or the file's name without one.
    pub mod_name: &'static str,
    /// The file it came from, for messages.
    pub source: &'static str,
}

/// One file, as the extension sends it: where it came from and its text.
#[derive(Clone, Debug, Deserialize)]
pub struct InstructionFile {
    pub source: String,
    pub text: String,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct FileJson {
    #[serde(default)]
    name: Option<String>,
    #[serde(default, rename = "modId")]
    #[allow(dead_code)]
    mod_id: Option<String>,
    #[serde(rename = "$schema", default)]
    #[allow(dead_code)]
    schema: Option<String>,
    instructions: HashMap<String, InstructionJson>,
}

#[derive(Clone, PartialEq, Deserialize)]
#[serde(deny_unknown_fields)]
struct InstructionJson {
    #[serde(default)]
    description: String,
    #[serde(default)]
    operands: Vec<OperandJson>,
    #[serde(default)]
    flow: Option<Flow>,
}

#[derive(Clone, PartialEq, Deserialize)]
#[serde(deny_unknown_fields)]
struct OperandJson {
    name: String,
    #[serde(rename = "type")]
    typ: String,
    #[serde(default)]
    role: Role,
    #[serde(default)]
    optional: bool,
    #[serde(default)]
    into: Option<String>,
}

#[derive(Clone, Copy, Default, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "lowercase")]
enum Role {
    #[default]
    Read,
    Write,
    Jump,
}

static REGISTRY: LazyLock<RwLock<HashMap<String, &'static ExtraInstruction>>> =
    LazyLock::new(|| RwLock::new(HashMap::new()));

/// The extra instruction called `name`, if one is registered.
pub fn get(name: &str) -> Option<&'static ExtraInstruction> {
    REGISTRY.read().ok()?.get(name).copied()
}

/// Every registered extra instruction.
pub fn all() -> Vec<&'static ExtraInstruction> {
    REGISTRY
        .read()
        .map(|r| r.values().copied().collect())
        .unwrap_or_default()
}

/// Replace the registered instructions with those in `files`. Returns a warning for each file or
/// entry that couldn't be used; the rest are registered. The first file to name an instruction wins.
pub fn set_extra_instructions(files: &[InstructionFile]) -> Vec<String> {
    let mut warnings = Vec::new();
    let mut registry: HashMap<String, &'static ExtraInstruction> = HashMap::new();
    // Each name's first definition: the same mod's file can come from several places (its game
    // folder, and an npm package), and only a different definition is worth a warning.
    let mut first: HashMap<String, InstructionJson> = HashMap::new();
    for file in files {
        let parsed: FileJson = match serde_json::from_str(&file.text) {
            Ok(parsed) => parsed,
            Err(e) => {
                warnings.push(format!("{}: {}", file.source, e));
                continue;
            }
        };
        let source: &'static str = Box::leak(file.source.clone().into_boxed_str());
        let label = parsed.name.as_deref().unwrap_or(source);
        let mod_name: &'static str = match parsed.name.as_deref().map(str::trim) {
            Some(name) if !name.is_empty() => Box::leak(name.to_string().into_boxed_str()),
            _ => std::path::Path::new(source)
                .file_name()
                .and_then(|n| n.to_str())
                .unwrap_or(source),
        };
        let mut names: Vec<&String> = parsed.instructions.keys().collect();
        names.sort();
        for name in names {
            let entry = &parsed.instructions[name];
            if INSTRUCTIONS.contains_key(name.as_str()) {
                warnings.push(format!(
                    "{}: {} is one of the game's instructions; a file can't redefine it",
                    source, name
                ));
                continue;
            }
            if let Some(existing) = first.get(name) {
                if existing != entry {
                    let by = registry.get(name.as_str()).map_or("an earlier file", |e| e.source);
                    warnings.push(format!(
                        "{}: {} is already defined differently by {}; using that one",
                        source, name, by
                    ));
                }
                continue;
            }
            first.insert(name.clone(), entry.clone());
            match build(name, entry, mod_name, source) {
                Ok(instruction) => {
                    registry.insert(name.clone(), Box::leak(Box::new(instruction)));
                }
                Err(e) => warnings.push(format!("{} ({}): {}: {}", source, label, name, e)),
            }
        }
    }
    if let Ok(mut current) = REGISTRY.write() {
        *current = registry;
    }
    warnings
}

fn build(
    name: &str,
    entry: &InstructionJson,
    mod_name: &'static str,
    source: &'static str,
) -> Result<ExtraInstruction, String> {
    if name.is_empty() || !name.chars().all(|c| c.is_ascii_alphanumeric() || c == '_') {
        return Err("an instruction's name is letters, digits and _".to_string());
    }
    let mut unions: Vec<Union<'static>> = Vec::with_capacity(entry.operands.len());
    let mut names: Vec<&'static str> = Vec::with_capacity(entry.operands.len());
    let mut writes_first = false;
    let mut jump = None;
    let mut into: Vec<(usize, &'static str)> = Vec::new();
    let mut min_operands = 0;
    for (i, operand) in entry.operands.iter().enumerate() {
        let types = parse_type(&operand.typ)
            .ok_or_else(|| format!("operand {} has an unknown type \"{}\"", operand.name, operand.typ))?;
        if operand.optional {
            // Optional operands come last: min_operands stops growing at the first one.
        } else if min_operands == i {
            min_operands += 1;
        } else {
            return Err(format!(
                "operand {} is required but follows an optional one; optional operands come last",
                operand.name
            ));
        }
        match operand.role {
            Role::Read => {}
            // As for the game's instructions, only the first operand can be the one written.
            Role::Write if i == 0 => writes_first = true,
            Role::Write => return Err(format!("operand {} can't be written: only the first operand can", operand.name)),
            Role::Jump => {
                if jump.is_some() {
                    return Err("only one operand can be the jump target".to_string());
                }
                jump = Some(i);
            }
        }
        if let Some(register) = &operand.into {
            let register = register.trim();
            let valid = register
                .strip_prefix('r')
                .and_then(|n| n.parse::<u8>().ok())
                .is_some_and(|n| n <= 15 && register == format!("r{n}"));
            if !valid {
                return Err(format!("operand {}'s \"into\" is a register, r0 to r15", operand.name));
            }
            if operand.role != Role::Read {
                return Err(format!("operand {} is read to go into {}: its role can only be read", operand.name, register));
            }
            into.push((i, &*Box::leak(register.to_string().into_boxed_str())));
        }
        unions.push(Union(Box::leak(types.into_boxed_slice())));
        names.push(Box::leak(operand.name.clone().into_boxed_str()));
    }
    if entry.flow.is_some_and(|f| f != Flow::Return && f != Flow::Halt) && jump.is_none() {
        return Err("a jump, branch or call needs an operand with \"role\": \"jump\"".to_string());
    }
    let signature: &'static InstructionSignature =
        Box::leak(Box::new(InstructionSignature(Box::leak(unions.into_boxed_slice()))));
    Ok(ExtraInstruction {
        name: Box::leak(name.to_string().into_boxed_str()),
        signature,
        description: Box::leak(entry.description.clone().into_boxed_str()),
        operand_names: Box::leak(names.into_boxed_slice()),
        min_operands,
        shape: Shape {
            writes_first,
            jump,
            flow: entry.flow,
            into: Box::leak(into.into_boxed_slice()),
        },
        mod_name,
        source,
    })
}

/// An operand type in the game's help spelling (`r?|num`) as data types. Numbers also take a
/// register, as the built-in signatures do (see build.rs's `color_to_datatypes`).
fn parse_type(text: &str) -> Option<Vec<DataType>> {
    let mut types = Vec::new();
    for part in text.split('|') {
        let part = part.trim();
        let add: &[DataType] = match part.to_ascii_lowercase().as_str() {
            "r?" => &[DataType::Register],
            "num" | "int" | "id" => &[DataType::Number, DataType::Register],
            "d?" => &[DataType::Device],
            "logictype" => &[DataType::LogicType],
            "slotlogictype" | "logicslottype" => &[DataType::SlotLogicType],
            "batchmode" => &[DataType::BatchMode, DataType::Number],
            "reagentmode" => &[DataType::ReagentMode, DataType::Number, DataType::Register],
            "str" | "name" => &[DataType::Name],
            _ => return None,
        };
        for t in add {
            if !types.contains(t) {
                types.push(*t);
            }
        }
    }
    (!types.is_empty()).then_some(types)
}

#[cfg(test)]
mod test {
    use super::*;

    /// A made-up mod with function calls (main.rs's tests use it too).
    const EXAMPLE_CALLS: &str = include_str!("../tests/fixtures/example-calls.ic10-instructions.json");

    /// The registry is global and tests run in parallel: tests that set it hold this lock.
    static LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());

    fn load(text: &str) -> Vec<String> {
        set_extra_instructions(&[InstructionFile {
            source: "test.json".to_string(),
            text: text.to_string(),
        }])
    }

    #[test]
    fn registers_a_files_instructions() {
        let _guard = LOCK.lock().unwrap_or_else(|e| e.into_inner());
        assert_eq!(load(EXAMPLE_CALLS), Vec::<String>::new());
        let fcall = get("fcall").expect("fcall");
        assert_eq!(fcall.signature.0.len(), 7);
        assert_eq!(fcall.min_operands, 2);
        assert!(fcall.shape.writes_first);
        assert!(!get("finit").unwrap().shape.writes_first);
        assert_eq!(fcall.shape.jump, Some(1));
        assert_eq!(fcall.shape.flow, Some(Flow::Call));
        assert_eq!(fcall.shape.into, &[(2, "r0"), (3, "r1"), (4, "r2"), (5, "r3"), (6, "r4")]);
        assert_eq!(fcall.operand_names[2], "a");
        assert_eq!(fcall.mod_name, "Example Calls");
        assert!(fcall.signature.0[2].match_type(DataType::Register));
        assert!(fcall.signature.0[2].match_type(DataType::Number));
        assert_eq!(get("fret").unwrap().min_operands, 0);
        // Replacing the files replaces the instructions.
        load(r#"{ "instructions": {} }"#);
        assert!(get("fcall").is_none());
    }

    #[test]
    fn warns_about_what_it_cant_use() {
        let _guard = LOCK.lock().unwrap_or_else(|e| e.into_inner());
        let warnings = load(
            r#"{ "instructions": {
              "ok": { "operands": [ { "name": "a", "type": "r?" } ] },
              "badtype": { "operands": [ { "name": "a", "type": "banana" } ] },
              "order": { "operands": [ { "name": "a", "type": "r?", "optional": true }, { "name": "b", "type": "r?" } ] },
              "nojump": { "operands": [], "flow": "jump" },
              "secondwrite": { "operands": [ { "name": "a", "type": "r?" }, { "name": "b", "type": "r?", "role": "write" } ] },
              "badinto": { "operands": [ { "name": "a", "type": "num", "into": "r16" } ] },
              "add": { "operands": [] }
            } }"#,
        );
        assert_eq!(warnings.len(), 6, "{warnings:?}");
        assert!(warnings.iter().any(|w| w.contains("badinto") && w.contains("r0 to r15")));
        assert!(warnings.iter().any(|w| w.contains("secondwrite") && w.contains("only the first operand")));
        assert!(warnings.iter().any(|w| w.contains("badtype") && w.contains("banana")));
        assert!(warnings.iter().any(|w| w.contains("order") && w.contains("optional operands come last")));
        assert!(warnings.iter().any(|w| w.contains("nojump")));
        assert!(warnings.iter().any(|w| w.contains("add is one of the game's instructions")));
        assert!(get("ok").is_some());
        assert!(get("add").is_none());

        assert_eq!(load("{ not json").len(), 1);
        load(r#"{ "instructions": {} }"#);
    }

    #[test]
    fn the_first_file_to_name_an_instruction_wins() {
        let _guard = LOCK.lock().unwrap_or_else(|e| e.into_inner());
        let file = |source: &str, description: &str| InstructionFile {
            source: source.to_string(),
            text: format!(r#"{{ "instructions": {{ "xop": {{ "description": "{description}" }} }} }}"#),
        };
        let warnings = set_extra_instructions(&[file("one.json", "first"), file("two.json", "second")]);
        assert_eq!(warnings.len(), 1);
        assert!(warnings[0].contains("two.json") && warnings[0].contains("one.json"));
        assert_eq!(get("xop").unwrap().description, "first");
        // A file without a "name" names its mod by the file's name.
        assert_eq!(get("xop").unwrap().mod_name, "one.json");
        // The same definition twice (a mod's folder and its npm package) is fine.
        let warnings = set_extra_instructions(&[file("one.json", "same"), file("two.json", "same")]);
        assert_eq!(warnings, Vec::<String>::new());
        load(r#"{ "instructions": {} }"#);
    }
}
