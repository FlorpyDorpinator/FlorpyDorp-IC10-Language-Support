# Mod catalogue

Instructions for mods that don't ship their own `ic10-instructions.json`. Each file here is an
`ic10-instructions.json` (see `schemas/ic10-instructions.schema.json`) with one more field,
`"modId"`: the `<ModID>` in the mod's `About/About.xml`.

```json
{
  "name": "Some Mod",
  "modId": "author.somemod",
  "instructions": {
    "xop": {
      "description": "What it does.",
      "operands": [{ "name": "dest", "type": "r?", "role": "write" }, { "name": "a", "type": "r?|num" }]
    }
  }
}
```

While the mod is enabled in the game, the extension uses its file. A file the mod ships itself (in
its folder, or its `About` folder) comes first.
