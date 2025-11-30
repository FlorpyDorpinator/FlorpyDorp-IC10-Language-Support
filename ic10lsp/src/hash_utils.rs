use crate::device_hashes::{DEVICE_NAME_TO_HASH, HASH_TO_DISPLAY_NAME};
use crc32fast::Hasher;

/// Computes CRC32 hash for a given string using the same algorithm as Stationeers
pub fn compute_crc32(input: &str) -> i32 {
    let mut hasher = Hasher::new();
    hasher.update(input.as_bytes());
    let hash = hasher.finalize();
    // Convert to signed 32-bit integer (Stationeers uses signed values)
    hash as i32
}

/// Extracts the device name from a HASH("device_name") function call
pub fn extract_hash_argument(input: &str) -> Option<String> {
    // Handle HASH("device_name") format
    let input = input.trim();

    // New format: just the quoted string "device_name" (from hash_string node)
    // or legacy format: HASH("device_name")
    
    // Check if it's just a quoted string
    if input.starts_with('"') && input.ends_with('"') && input.len() >= 2 {
        return Some(input[1..input.len() - 1].to_string());
    }

    // Legacy: Must start with HASH(
    if !input.to_uppercase().starts_with("HASH(") {
        return None;
    }

    // Must end with )
    if !input.ends_with(')') {
        return None;
    }

    // Extract content between HASH( and )
    let content = &input[5..input.len() - 1].trim();

    // Handle quoted strings
    if content.len() >= 2 {
        let first_char = content.chars().next()?;
        let last_char = content.chars().last()?;

        if (first_char == '"' && last_char == '"') || (first_char == '\'' && last_char == '\'') {
            return Some(content[1..content.len() - 1].to_string());
        }
    }

    // Handle unquoted strings (edge case)
    Some(content.to_string())
}

/// Extracts the string from a STR("string") function call
pub fn extract_str_argument(input: &str) -> Option<String> {
    // Handle STR("string") format
    let input = input.trim();

    // New format: just the quoted string "string" (from str_string node)
    // or legacy format: STR("string")
    
    // Check if it's just a quoted string
    if input.starts_with('"') && input.ends_with('"') && input.len() >= 2 {
        return Some(input[1..input.len() - 1].to_string());
    }

    // Legacy: Must start with STR(
    if !input.to_uppercase().starts_with("STR(") {
        return None;
    }

    // Must end with )
    if !input.ends_with(')') {
        return None;
    }

    // Extract content between STR( and )
    let content = &input[4..input.len() - 1].trim();

    // Handle quoted strings
    if content.len() >= 2 {
        let first_char = content.chars().next()?;
        let last_char = content.chars().last()?;

        if (first_char == '"' && last_char == '"') || (first_char == '\'' && last_char == '\'') {
            return Some(content[1..content.len() - 1].to_string());
        }
    }

    // Handle unquoted strings (edge case)
    Some(content.to_string())
}

/// Checks if a string is a valid HASH() function call
pub fn is_hash_function_call(input: &str) -> bool {
    extract_hash_argument(input).is_some()
}

/// Checks if a string is a valid STR() function call
pub fn is_str_function_call(input: &str) -> bool {
    extract_str_argument(input).is_some()
}

/// Looks up device name in device registry and returns the corresponding hash
pub fn get_device_hash(device_name: &str) -> Option<i32> {
    DEVICE_NAME_TO_HASH.get(device_name).copied()
}

/// Gets device name for a given hash value from the registry
pub fn get_device_name_for_hash(hash_value: i32) -> Option<&'static str> {
    HASH_TO_DISPLAY_NAME.get(&hash_value).copied()
}

/// Checks if a string contains only digits (potentially negative)
pub fn is_numeric_string(s: &str) -> bool {
    let trimmed = s.trim();
    if trimmed.is_empty() {
        return false;
    }
    
    // Check if it starts with optional minus and contains only digits
    let without_minus = trimmed.strip_prefix('-').unwrap_or(trimmed);
    !without_minus.is_empty() && without_minus.chars().all(|c| c.is_ascii_digit())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_compute_crc32() {
        assert_eq!(compute_crc32("StructureVolumePump"), -321403609);
        assert_eq!(compute_crc32("StructureDaylightSensor"), 1076425094);
    }

    #[test]
    fn test_extract_hash_argument() {
        assert_eq!(
            extract_hash_argument("HASH(\"StructureVolumePump\")"),
            Some("StructureVolumePump".to_string())
        );
        assert_eq!(
            extract_hash_argument("HASH('StructureVolumePump')"),
            Some("StructureVolumePump".to_string())
        );
        assert_eq!(
            extract_hash_argument("HASH(StructureVolumePump)"),
            Some("StructureVolumePump".to_string())
        );
        assert_eq!(
            extract_hash_argument("HASH(\"Volume Pump\")"),
            Some("Volume Pump".to_string())
        );
        assert_eq!(extract_hash_argument("not_hash"), None);
        assert_eq!(extract_hash_argument("HASH("), None);
    }

    #[test]
    fn test_get_device_hash() {
        assert_eq!(get_device_hash("StructureVolumePump"), Some(-321403609));
        assert_eq!(get_device_hash("StructureDaylightSensor"), Some(1076425094));
        assert_eq!(get_device_hash("NonExistentDevice"), None);
    }

    #[test]
    fn test_is_hash_function_call() {
        assert!(is_hash_function_call("HASH(\"StructureVolumePump\")"));
        assert!(is_hash_function_call("HASH('StructureDaylightSensor')"));
        assert!(is_hash_function_call("HASH(Volume Pump)"));
        assert!(!is_hash_function_call("define"));
        assert!(!is_hash_function_call("HASH"));
    }
}
