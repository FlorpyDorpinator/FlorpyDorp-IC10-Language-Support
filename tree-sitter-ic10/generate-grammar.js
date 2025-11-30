// Generate grammar.js from game source files
// This script auto-generates the instruction operations and logic types
// from the same Enums.json that the LSP uses, ensuring they stay in sync.

const fs = require('fs');
const path = require('path');

// Read Enums.json
const enumsPath = path.join(__dirname, '../data/game-sources/Enums.json');
const enumsData = JSON.parse(fs.readFileSync(enumsPath, 'utf8'));

// Read Stationpedia.json for constants
const stationpediaPath = path.join(__dirname, '../data/game-sources/Stationpedia.json');
const stationpediaData = JSON.parse(fs.readFileSync(stationpediaPath, 'utf8'));

// Read ProgrammableChip.cs to get instructions
const chipPath = path.join(__dirname, '../data/game-sources/ProgrammableChip.cs');
const chipCs = fs.readFileSync(chipPath, 'utf8');

// Extract instructions from GetCommandExample method's case statements
const instructions = [];
const caseRegex = /case ScriptCommand\.([a-z0-9]+):/gi;
let match;

while ((match = caseRegex.exec(chipCs)) !== null) {
    const cmd = match[1].toLowerCase();
    if (cmd && !instructions.includes(cmd)) {
        instructions.push(cmd);
    }
}

// Sort for consistency
instructions.sort();

// Extract constants from Stationpedia.json
const constants = [];
if (stationpediaData.scriptConstants) {
    for (const name of Object.keys(stationpediaData.scriptConstants)) {
        constants.push(name);
    }
}
constants.sort();

// Extract LogicTypes from Enums.json
const logicTypes = [];
if (enumsData.scriptEnums?.LogicType?.values) {
    for (const [name, data] of Object.entries(enumsData.scriptEnums.LogicType.values)) {
        if (!data.deprecated) {
            logicTypes.push(name);
        }
    }
}

// Extract LogicSlotTypes from Enums.json
const slotTypes = [];
if (enumsData.scriptEnums?.LogicSlotType?.values) {
    for (const [name, data] of Object.entries(enumsData.scriptEnums.LogicSlotType.values)) {
        if (!data.deprecated) {
            slotTypes.push(name);
        }
    }
}

// Extract BatchModes from Enums.json
const batchModes = [];
if (enumsData.scriptEnums?.LogicBatchMethod?.values) {
    for (const [name, data] of Object.entries(enumsData.scriptEnums.LogicBatchMethod.values)) {
        if (!data.deprecated) {
            batchModes.push(name);
        }
    }
}

// Combine all logictype tokens (LogicTypes, SlotTypes, and BatchModes)
const allLogicTypes = [...logicTypes, ...slotTypes, ...batchModes];

// Generate the grammar file
const grammarTemplate = `module.exports = grammar({
    name: 'ic10',

    extras: $ => [$._whitespace],

    word: $ => $.identifier,

    rules: {
        source_file: $ => alias(repeat($.line), $.program),

        line: $ => seq(
            optional(choice($.instruction, $.label)),
            optional($.comment),
            $.newline,
        ),

        label: $ => seq($.identifier, ':'),

        newline: $ => choice('\\n', '\\r\\n', '\\r'),

        _whitespace: $ => /[ \\t]+/,

        comment: $ => seq(
            /#.*/
        ),

        instruction: $ => seq(
            field('operation', choice($.operation, alias($.identifier, $.invalid_instruction))),
            repeat(field('operand',
                $.operand
            ))
        ),

    // Elevate STR/HASH functions to highest precedence and list before identifier/logictype to avoid partial matches.
    operand: $ => choice(
        $.str_function,
        $.hash_function,
        $.register,
        $.device_spec,
        $.number,
        $.logictype,
        $.identifier
    ),

        identifier: $ => /[a-zA-Z_.][a-zA-Z0-9_.]*/,

        register: $ => token(prec(5,choice(
            'ra',
            'sp',
            seq(
                repeat1('r'),
                /[0-9]|1[0-5]/
            )
        ))),

        network_index: $ => /[0-9]+/,

        device_spec: $ => seq(
            $.device,
            optional(
                seq(
                    ":",
                    $.network_index,
                )
            )
        ),

        device: $ => token(prec(5,seq(
            'd',
            choice(
                'b',
                /[0-5]/,
                seq(
                    repeat1('r'),
                    /[0-9]|1[0-5]/
                )
            ),
        ))),

        _constant: $ => choice(
${constants.map(c => `            '${c}',`).join('\n')}
        ),

        preproc_string: $ => /[^"\\n]*/,

        // Parse HASH("...") as a proper function call with child nodes
        hash_function: $ => prec(15, seq(
            field('function', alias('HASH', $.hash_keyword)),
            '(',
            field('argument', alias(token(/"[^"\\n]*"/), $.hash_string)),
            ')'
        )),

        // Parse STR("...") as a proper function call with child nodes  
        str_function: $ => prec(15, seq(
            field('function', alias('STR', $.str_keyword)),
            '(',
            field('argument', alias(token(/"[^"\\n]*"/), $.str_string)),
            ')'
        )),

        number: $ => choice(
            token(
                choice(
                    seq(
                        optional('-'),
                        /[0-9]+/,
                        optional(seq(
                            '.',
                            /[0-9]+/
                        ))
                    ),
                    seq("%", /[01_]+/),
                    seq("$", /[0-9a-fA-F_]+/),
                ),
            ),
            $._constant
        ),

        operation: $ => choice(
${instructions.map(cmd => `            '${cmd}',`).join('\n')}
        ),

        logictype: $ => token(prec(5,choice(
${allLogicTypes.map(type => `            '${type}',`).join('\n')}
        )))
    }
});
`;

// Write the generated grammar
const outputPath = path.join(__dirname, 'grammar.js');
fs.writeFileSync(outputPath, grammarTemplate, 'utf8');

console.log(`✅ Generated grammar.js with:`);
console.log(`   - ${instructions.length} instructions`);
console.log(`   - ${logicTypes.length} LogicTypes`);
console.log(`   - ${slotTypes.length} SlotLogicTypes`);
console.log(`   - ${batchModes.length} BatchModes`);
console.log(`   - ${constants.length} constants`);
console.log(`   - ${allLogicTypes.length} total logictype tokens`);
