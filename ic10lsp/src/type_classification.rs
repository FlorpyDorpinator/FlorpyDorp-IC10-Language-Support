//! Type classification and union matching for IC10 parameters
//!
//! This module provides utilities for classifying identifiers as logic types,
//! slot types, batch modes, or reagent modes, and converting those classifications
//! into type unions for parameter validation.

use ic10lsp::instructions;

/// Bitmask flags representing different keyword types
#[derive(Clone, Copy)]
pub struct KeywordFlags(u8);

impl KeywordFlags {
    /// Create keyword flags from boolean values for each type
    pub fn from_bools(logic: bool, slot: bool, batch: bool, reagent: bool) -> Self {
        KeywordFlags(
            (logic as u8) | ((slot as u8) << 1) | ((batch as u8) << 2) | ((reagent as u8) << 3),
        )
    }

    /// Check if any flag is set
    pub fn any(self) -> bool {
        self.0 != 0
    }

    /// Convert flags to a type union
    pub fn to_union(self) -> instructions::Union<'static> {
        union_from_mask(self.0)
    }
}

/// Classify an identifier with exact case matching
pub fn classify_exact_keyword(ident: &str) -> KeywordFlags {
    KeywordFlags::from_bools(
        instructions::LOGIC_TYPES.contains(ident),
        instructions::SLOT_LOGIC_TYPES.contains(ident),
        instructions::BATCH_MODES.contains(ident),
        instructions::REAGENT_MODES.contains(ident),
    )
}

/// Classify an identifier with case-insensitive matching
pub fn classify_ci_keyword(ident: &str) -> KeywordFlags {
    KeywordFlags::from_bools(
        instructions::LOGIC_TYPES
            .iter()
            .any(|x| x.eq_ignore_ascii_case(ident)),
        instructions::SLOT_LOGIC_TYPES
            .iter()
            .any(|x| x.eq_ignore_ascii_case(ident)),
        instructions::BATCH_MODES
            .iter()
            .any(|x| x.eq_ignore_ascii_case(ident)),
        instructions::REAGENT_MODES
            .iter()
            .any(|x| x.eq_ignore_ascii_case(ident)),
    )
}

/// Convert a bitmask to a type union
///
/// The mask bits represent: logic | slot | batch | reagent
fn union_from_mask(mask: u8) -> instructions::Union<'static> {
    use instructions::DataType;
    
    // Define type arrays as constants
    const LOGIC_ONLY: [DataType; 1] = [DataType::LogicType];
    const SLOT_ONLY: [DataType; 1] = [DataType::SlotLogicType];
    const BATCH_ONLY: [DataType; 1] = [DataType::BatchMode];
    const REAGENT_ONLY: [DataType; 1] = [DataType::ReagentMode];
    const LOGIC_SLOT: [DataType; 2] = [DataType::LogicType, DataType::SlotLogicType];
    const LOGIC_BATCH: [DataType; 2] = [DataType::LogicType, DataType::BatchMode];
    const LOGIC_REAGENT: [DataType; 2] = [DataType::LogicType, DataType::ReagentMode];
    const SLOT_BATCH: [DataType; 2] = [DataType::SlotLogicType, DataType::BatchMode];
    const SLOT_REAGENT: [DataType; 2] = [DataType::SlotLogicType, DataType::ReagentMode];
    const BATCH_REAGENT: [DataType; 2] = [DataType::BatchMode, DataType::ReagentMode];
    const LOGIC_SLOT_BATCH: [DataType; 3] = [
        DataType::LogicType,
        DataType::SlotLogicType,
        DataType::BatchMode,
    ];
    const LOGIC_SLOT_REAGENT: [DataType; 3] = [
        DataType::LogicType,
        DataType::SlotLogicType,
        DataType::ReagentMode,
    ];
    const LOGIC_BATCH_REAGENT: [DataType; 3] = [
        DataType::LogicType,
        DataType::BatchMode,
        DataType::ReagentMode,
    ];
    const SLOT_BATCH_REAGENT: [DataType; 3] = [
        DataType::SlotLogicType,
        DataType::BatchMode,
        DataType::ReagentMode,
    ];
    const LOGIC_SLOT_BATCH_REAGENT: [DataType; 4] = [
        DataType::LogicType,
        DataType::SlotLogicType,
        DataType::BatchMode,
        DataType::ReagentMode,
    ];
    
    match mask {
        0 => instructions::Union(&[]),
        0b0001 => instructions::Union(&LOGIC_ONLY),
        0b0010 => instructions::Union(&SLOT_ONLY),
        0b0100 => instructions::Union(&BATCH_ONLY),
        0b1000 => instructions::Union(&REAGENT_ONLY),
        0b0011 => instructions::Union(&LOGIC_SLOT),
        0b0101 => instructions::Union(&LOGIC_BATCH),
        0b1001 => instructions::Union(&LOGIC_REAGENT),
        0b0110 => instructions::Union(&SLOT_BATCH),
        0b1010 => instructions::Union(&SLOT_REAGENT),
        0b1100 => instructions::Union(&BATCH_REAGENT),
        0b0111 => instructions::Union(&LOGIC_SLOT_BATCH),
        0b1011 => instructions::Union(&LOGIC_SLOT_REAGENT),
        0b1101 => instructions::Union(&LOGIC_BATCH_REAGENT),
        0b1110 => instructions::Union(&SLOT_BATCH_REAGENT),
        0b1111 => instructions::Union(&LOGIC_SLOT_BATCH_REAGENT),
        _ => instructions::Union(&[]),
    }
}
