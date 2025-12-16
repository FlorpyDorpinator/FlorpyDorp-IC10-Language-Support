// Device descriptions from English.xml
//
// This module provides access to device descriptions extracted from the game's
// localization files. Descriptions are cross-referenced with device prefab names.

// Include the generated descriptions map
include!(concat!(env!("OUT_DIR"), "/descriptions_generated.rs"));

/// Get the display name and description for a device by its prefab name
pub fn get_device_description(prefab_name: &str) -> Option<(&'static str, &'static str)> {
    DEVICE_DESCRIPTIONS.get(prefab_name).copied()
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_device_description_exists() {
        // Test that we can retrieve a description for a known device
        let desc = get_device_description("StructureVolumePump");
        assert!(desc.is_some());
        if let Some((display_name, description)) = desc {
            assert!(!display_name.is_empty());
            assert!(!description.is_empty());
        }
    }
}
