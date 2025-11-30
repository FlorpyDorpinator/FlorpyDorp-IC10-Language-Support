### Changelog Beginning 11-01-2025

## [2.1.7] - 2025-11-30 The "BatchMode Validation" Update

### 🐛 Bug Fixes
- Added strict validation for BatchMode functionality, allowing only numeric values (e.g., 0, 1, 2, 3, etc.) while removing support for named strings (e.g., "Average", "Sum"). This ensures consistent and unambiguous BatchMode input and resolves ambiguities related to mixed input types.