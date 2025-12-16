use phf::phf_map;

/// Enhanced documentation for instruction hover tooltips
/// This module provides examples, categories, and related instruction mappings
/// for comprehensive hover documentation in the IC10 language server.

pub(crate) const INSTRUCTION_EXAMPLES: phf::Map<&'static str, &'static str> = phf_map! {
    "add" => "add r0 r1 r2      # Simple: r0 = r1 + r2\nadd r7 r5 r6      # Total charge from both batteries\nadd r10 r8 r9   # Total max power",
    "sub" => "sub r0 r1 r2      # Simple: r0 = r1 - r2\nsub currentRoomTemperature currentRoomTemperature 273.15\nsub temp temp 10  # temp = temp - 10",
    "mul" => "mul r0 r1 r2      # Simple: r0 = r1 * r2\nmul r3 r1 2       # PowerRequired in 1 second\nmul r15 r15 r14  # Temperature * TotalMoles",
    "div" => "div r0 r1 r2      # Simple: r0 = r1 / r2\ndiv timeRemaining r2 r3  # Power reserve in seconds\ndiv chargePercent totalCharge r10",
    "mod" => "mod r0 r1 r2      # r0 = r1 mod r2\nmod r0 timer 10   # r0 = timer mod 10",
    "move" => "move r0 100           # Simple: set r0 to 100\nmove targetPipePressure 5000  # Set to 5MPa\nmove fillVentBatch HASH(\"ActiveVentExhaust\")",
    "l" => "l r0 d0 Temperature     # Simple: read temperature from device 0\nl currentRoomPressure gasSensor Pressure\nl leverState01 leverSwitch01 Open",
    "s" => "s d1 Setting r0         # Simple: set device 1 setting to r0\ns pressureRegulator Setting targetPipePressure\ns db Setting currentRoomPressure",
    "lb" => "lb r0 HASH(\"StructureBattery\") Charge Average  # Simple: average battery charge\nlb r1 HASH(\"StructureGasSensor\") Pressure Sum\nlb totalPower HASH(\"StructureGasTurbine\") PowerGeneration Maximum",
    "sb" => "sb HASH(\"StructureHeater\") Setting r0        # Simple: set all heaters to r0\nsb HASH(\"StructureGasTurbine\") On r1\nsb heaterHASH Setting targetTemperature",
    "lbn" => "lbn r0 HASH(\"Sensor\") HASH(\"TempSensor\") Temperature 0\nlbn r0 PipeAnalyser AnalyserBeforePump Pressure 0\nlbn vertical DaylightSensor DaylightSensorClock Vertical 0",
    "sbn" => "sbn HASH(\"Pump\") HASH(\"MainPump\") On r0\nsbn TurboVolumePump TurboVolPumpIntake Setting r13\nsbn DisplayMedium DisplayHour Color hourColor",
    "ls" => "ls r0 d0 0 Occupied     # Simple: check if slot 0 occupied in d0\nls r1 sorter 1 Quantity\nls quantity conveyor 2 Damage",
    "lr" => "lr r0 d1 Contents Iron  # Simple: read iron content from d1\nlr r2 furnace Required Uranium\nlr totalMoles centrifuge Contents Volatiles",
    "beq" => "beq r0 r1 loop     # Simple: branch to 'loop' if r0 == r1\nbeq r12 r11 end    # Branch to 'end' if recipe matches\nbeq leverState01 0 InitialSetup",
    "bne" => "bne r0 r1 loop     # Simple: branch to 'loop' if r0 != r1\nbne r2 0 continue  # Continue if r2 is not zero\nbne temp 0 continue",
    "bgt" => "bgt r0 100 heating # Simple: branch if r0 > 100\nbgt r12 1 2        # Skip next instruction if ratio > 1\nbgt leverState02 0 FillRoomMode",
    "blt" => "blt r0 50 cooling  # Simple: branch if r0 < 50\nblt r15 8000 2     # Skip if fuel pressure low\nblt temp 273 heat_on",
    "bge" => "bge r0 r1 ready    # Simple: branch if r0 >= r1\nbge currentRoomPressure targetRoomPressure StopActiveVents\nbge pressure 1000 emergency",
    "ble" => "ble r0 10 low      # Simple: branch if r0 <= 10\nble r1 7 2         # Skip next 2 instructions\nble temp 373 normal",
    "beqz" => "beqz r0 off        # Simple: branch to 'off' if r0 == 0\nbeqz leverState01 InitialSetup  # Restart if lever off\nbeqz r7 end",
    "bnez" => "bnez r0 loop       # Simple: branch to 'loop' if r0 != 0\nbnez r1 active     # Jump to 'active' if r1 is not zero\nbnez power continue",
    "j" => "j loop             # Jump to 'loop' label\nj main             # Jump to 'main' label",
    "jal" => "jal subroutine     # Jump to subroutine, save return address\njal calculate_avg  # Call function",
    "jr" => "jr ra             # Return from subroutine\njr r15            # Jump to address in r15",
    "alias" => "alias temp r0        # Simple: name r0 as 'temp'\nalias sensor d0       # Simple: name d0 as 'sensor'\nalias currentRoomPressure r4  # Room pressure reading",
    "define" => "define MAX_TEMP 100  # Simple: define constant\ndefine ActiveVentHASH -1129453144  # Hash for ActiveVent devices\ndefine stationBattery HASH(\"StructureBattery\")  # Prefab Hash of Station Battery",
    "bdse" => "bdse d0 ready       # Simple: branch to 'ready' if d0 is set\nbdse sensor loop   # Branch to 'loop' if sensor is set\nbdse pump pump_active",
    "bdns" => "bdns d1 off         # Simple: branch to 'off' if d1 not set\nbdns pump shutdown # Branch to 'shutdown' if pump not set\nbdns furnace heat_off",
    "slt" => "slt r0 r1 r2       # Simple: r0 = 1 if r1 < r2\nslt r3 temp 100    # Check if temp < 100\nslt r10 temp mintemp",
    "sgt" => "sgt r0 r1 r2       # Simple: r0 = 1 if r1 > r2\nsgt r3 temp 200    # Check if temp > 200\nsgt r6 press maxpress",
    "seq" => "seq r0 r1 r2       # Simple: r0 = 1 if r1 == r2\nseq r3 temp 273    # Check if temp == 273\nseq ready power 1",
    "sne" => "sne r0 r1 r2       # Simple: r0 = 1 if r1 != r2\nsne r3 temp 0      # Check if temp != 0\nsne active error 0",
    "and" => "and r0 r1 r2      # Simple: r0 = 1 if both r1 and r2 not zero\nand r3 power heat  # Check both power and heat\nand ready sensor1 sensor2",
    "or" => "or r0 r1 r2       # Simple: r0 = 1 if r1 or r2 not zero\nor r3 error1 error2 # Check any error\nor alarm high_temp low_pressure",
    "sleep" => "sleep 1           # Wait 1 second between readings\nsleep 3           # Wait before checking vents again",
    "yield" => "yield             # Allow other scripts to run\nyield             # Pause execution for 1 tick",
    "sqrt" => "sqrt r0 r1        # Simple: r0 = square root of r1\nsqrt r2 r3         # Calculate square root\nsqrt distance r1  # distance = sqrt(r1)",
    "abs" => "abs r0 r1         # Simple: r0 = absolute value of r1\nabs r2 temperature # Get absolute temperature\nabs magnitude velocity",
    "sin" => "sin r0 r1         # Simple: r0 = sine of r1 (radians)\nsin r2 angle       # Calculate sine\nsin y_pos angle",
    "cos" => "cos r0 r1         # Simple: r0 = cosine of r1 (radians)\ncos r2 angle       # Calculate cosine\ncos x_pos angle",
    "select" => "select r0 r1 10 20       # Simple: r0 = 10 if r1!=0, else 20\nselect r2 isEast 1 0       # Choose 1 if east, 0 if west\nselect r9 playerOrigin 0 1   # Choose door index",
    "floor" => "floor r0 r1             # Simple: r0 = floor of r1\nfloor r2 temperature       # Round down temperature\nfloor minutes minutes     # Round down to integer",
    "round" => "round r0 r1             # Simple: r0 = rounded r1\nround r2 pressure          # Round pressure value\nround timeRemaining timeRemaining",
    "min" => "min r0 r1 r2             # Simple: r0 = minimum of r1 and r2\nmin r3 temp 100           # Limit temp to max 100\nmin r13 r13 MaxPumpSetting",
    "max" => "max r0 r1 r2             # Simple: r0 = maximum of r1 and r2\nmax r3 temp 0             # Ensure temp >= 0\nmax r13 r13 MinPumpSetting",
    "snez" => "snez r0 r1               # Simple: r0=1 if r1>0, else 0\nsnez r2 pressure          # Check if pressure exists\nsnez r14 r3                # Check if nitrogen detected",
    "bnezal" => "bnezal r0 subroutine     # Simple: call 'subroutine' if r0 != 0\nbnezal r2 ProcessData      # Call function if r2 is true\nbnezal condition ProcessGas",
    "trunc" => "trunc r0 r1             # Simple: r0 = integer part of r1\ntrunc r2 temperature       # Remove decimal from temp\ntrunc result calculation   # Integer portion only",
    "lbs" => "lbs r0 HASH(\"Processor\") 0 Occupied Average  # Simple: average slot 0 occupancy\nlbs r1 HASH(\"Furnace\") 1 Quantity Sum\nlbs totalOre HASH(\"Centrifuge\") 2 Quantity Maximum",
    "lbns" => "lbns r0 HASH(\"Processor\") HASH(\"Circuit\") 0 Occupied Average  # Simple: slot occupancy by type\nlbns r1 HASH(\"Furnace\") HASH(\"Coal\") 1 Quantity Sum\nlbns result HASH(\"Sorter\") itemHash 0 Damage Minimum",
    "not" => "not r0 r1               # Simple: r0 = 1 if r1 is 0, else 0\nnot r2 powered            # Invert powered state\nnot r15 error             # Check if no error",
    "sla" => "sla r0 r1 2             # Simple: r0 = r1 shifted left 2 bits\nsla r2 value 3            # Multiply by 8 using shift\nsla result data shiftAmount",
    "sll" => "sll r0 r1 2             # Simple: r0 = r1 shifted left 2 bits\nsll r2 flags 1            # Logical left shift\nsll result mask bitCount",
    "sra" => "sra r0 r1 2             # Simple: r0 = r1 shifted right 2 bits\nsra r2 value 3            # Divide by 8 using shift\nsra result data shiftAmount",
    "srl" => "srl r0 r1 2             # Simple: r0 = r1 shifted right 2 bits\nsrl r2 flags 1            # Logical right shift\nsrl result mask bitCount",
    "pow" => "pow r0 r1 r2           # r0 = r1 ^ r2 (power)\npow watts base exp       # compute base^exp\npow growth temp 2        # square temp",
    "ext" => "ext r0 r1 8 4          # extract 4 bits from r1 starting at bit 8 into r0\next flags value 0 1      # extract LSB\next r3 mask 16 8        # mid-byte",
    "ins" => "ins r0 r1 8 4          # insert 4-bit field from r1 at bit 8 into r0\nins flags bits 0 1       # insert LSB\nins r3 r2 16 8          # insert byte",
    "lerp" => "lerp r0 a b t          # r0 = a + (b - a) * clamp(t,0,1)\nlerp target min max ratio\nlerp temp temp0 temp1 alpha",
    "bdnvl" => "bdnvl device(d?|r?|id) logicType line   # branch if device invalid for load of logicType\nbdnvl sensor Temperature error\nbdnvl d0 Setting 100",
    "bdnvs" => "bdnvs device(d?|r?|id) logicType line   # branch if device invalid for store of logicType\nbdnvs writer Setting fixup\nbdnvs d1 Mode 200",
};

pub(crate) const INSTRUCTION_CATEGORIES: phf::Map<&'static str, &'static str> = phf_map! {
    "add" => "Arithmetic", "sub" => "Arithmetic", "mul" => "Arithmetic", "div" => "Arithmetic", "mod" => "Arithmetic",
    "abs" => "Arithmetic", "sqrt" => "Arithmetic", "round" => "Arithmetic", "trunc" => "Arithmetic", "ceil" => "Arithmetic", "floor" => "Arithmetic",
    "min" => "Arithmetic", "max" => "Arithmetic",
    "sin" => "Arithmetic", "cos" => "Arithmetic", "tan" => "Arithmetic", "asin" => "Arithmetic", "acos" => "Arithmetic", "atan" => "Arithmetic", "atan2" => "Arithmetic",
    "exp" => "Arithmetic", "log" => "Arithmetic", "rand" => "Arithmetic",
        "pow" => "Arithmetic", "lerp" => "Arithmetic",
    "l" => "Device I/O", "s" => "Device I/O", "lr" => "Device I/O", "ls" => "Device I/O", "ld" => "Device I/O", "sd" => "Device I/O", "ss" => "Device I/O",
    "lb" => "Batch Operations", "sb" => "Batch Operations", "lbn" => "Batch Operations", "lbs" => "Batch Operations", "lbns" => "Batch Operations", "sbn" => "Batch Operations", "sbs" => "Batch Operations",
    "move" => "Register Operations", "select" => "Register Operations", "peek" => "Register Operations", "push" => "Register Operations", "pop" => "Register Operations",
    "get" => "Stack Operations", "getd" => "Stack Operations", "put" => "Stack Operations", "putd" => "Stack Operations", "poke" => "Stack Operations", "clr" => "Stack Operations", "clrd" => "Stack Operations",
    "slt" => "Comparison", "sgt" => "Comparison", "sle" => "Comparison", "sge" => "Comparison", "seq" => "Comparison", "sne" => "Comparison",
    "sltz" => "Comparison", "sgtz" => "Comparison", "slez" => "Comparison", "sgez" => "Comparison", "seqz" => "Comparison", "snez" => "Comparison",
    "sap" => "Comparison", "sna" => "Comparison", "sapz" => "Comparison", "snaz" => "Comparison", "snan" => "Comparison", "snanz" => "Comparison",
    "sdse" => "Device Status", "sdns" => "Device Status", "bdse" => "Device Status", "bdns" => "Device Status", "brdse" => "Device Status", "brdns" => "Device Status",
    "and" => "Logic", "or" => "Logic", "xor" => "Logic", "nor" => "Logic", "not" => "Logic",
    "sla" => "Bit Operations", "sll" => "Bit Operations", "sra" => "Bit Operations", "srl" => "Bit Operations",
    "j" => "Control Flow", "jr" => "Control Flow", "jal" => "Control Flow",
    "beq" => "Branching", "bne" => "Branching", "blt" => "Branching", "bgt" => "Branching", "ble" => "Branching", "bge" => "Branching",
    "beqz" => "Branching", "bnez" => "Branching", "bltz" => "Branching", "bgtz" => "Branching", "blez" => "Branching", "bgez" => "Branching",
    "bap" => "Branching", "bna" => "Branching", "bapz" => "Branching", "bnaz" => "Branching", "bnan" => "Branching",
    "breq" => "Relative Branching", "brne" => "Relative Branching", "brlt" => "Relative Branching", "brgt" => "Relative Branching", "brle" => "Relative Branching", "brge" => "Relative Branching",
    "breqz" => "Relative Branching", "brnez" => "Relative Branching", "brltz" => "Relative Branching", "brgtz" => "Relative Branching", "brlez" => "Relative Branching", "brgez" => "Relative Branching",
    "brap" => "Relative Branching", "brna" => "Relative Branching", "brapz" => "Relative Branching", "brnaz" => "Relative Branching", "brnan" => "Relative Branching",
    "beqal" => "Branch and Link", "bneal" => "Branch and Link", "bltal" => "Branch and Link", "bgtal" => "Branch and Link", "bleal" => "Branch and Link", "bgeal" => "Branch and Link",
    "beqzal" => "Branch and Link", "bnezal" => "Branch and Link", "bltzal" => "Branch and Link", "bgtzal" => "Branch and Link", "blezal" => "Branch and Link", "bgezal" => "Branch and Link",
    "bapal" => "Branch and Link", "bnaal" => "Branch and Link", "bapzal" => "Branch and Link", "bnazal" => "Branch and Link", "bdseal" => "Branch and Link", "bdnsal" => "Branch and Link",
    "alias" => "Assembly", "define" => "Assembly", "label" => "Assembly",
    "sleep" => "Flow Control", "yield" => "Flow Control", "hcf" => "Flow Control",
    "rmap" => "Advanced", "hash" => "Advanced",
    "ext" => "Bit Operations", "ins" => "Bit Operations", "bdnvl" => "Device Status", "bdnvs" => "Device Status",
};

pub(crate) const RELATED_INSTRUCTIONS: phf::Map<&'static str, &'static [&'static str]> = phf_map! {
    "add" => &["sub", "mul", "div", "mod"],
    "sub" => &["add", "mul", "div", "mod"],
    "mul" => &["add", "sub", "div", "mod"],
    "div" => &["add", "sub", "mul", "mod"],
    "mod" => &["add", "sub", "mul", "div"],
    "l" => &["s", "lb", "sb", "lr", "ls", "ld", "sd"],
    "s" => &["l", "lb", "sb", "lr", "ls", "ld", "sd"],
    "lb" => &["l", "s", "sb", "lbn", "lbs", "lbns", "sbn", "sbs"],
    "sb" => &["l", "s", "lb", "lbn", "lbs", "lbns", "sbn", "sbs"],
    "ls" => &["l", "s", "lb", "sb", "lr"],
    "lr" => &["l", "s", "lb", "sb", "ls"],
    "beq" => &["bne", "blt", "bgt", "ble", "bge", "breq", "beqz"],
    "bne" => &["beq", "blt", "bgt", "ble", "bge", "brne", "bnez"],
    "blt" => &["beq", "bne", "bgt", "ble", "bge", "brlt", "bltz"],
    "bgt" => &["beq", "bne", "blt", "ble", "bge", "brgt", "bgtz"],
    "ble" => &["beq", "bne", "blt", "bgt", "bge", "brle", "blez"],
    "bge" => &["beq", "bne", "blt", "bgt", "ble", "brge", "bgez"],
    "beqz" => &["bnez", "bltz", "bgtz", "blez", "bgez", "beq"],
    "bnez" => &["beqz", "bltz", "bgtz", "blez", "bgez", "bne"],
    "j" => &["jr", "jal", "beq", "bne", "blt", "bgt"],
    "jr" => &["j", "jal", "breq", "brne", "brlt", "brgt"],
    "jal" => &["j", "jr", "beqal", "bneal", "bltal", "bgtal"],
    "slt" => &["sgt", "sle", "sge", "seq", "sne", "blt"],
    "sgt" => &["slt", "sle", "sge", "seq", "sne", "bgt"],
    "seq" => &["sne", "slt", "sgt", "sle", "sge", "beq"],
    "sne" => &["seq", "slt", "sgt", "sle", "sge", "bne"],
    "and" => &["or", "xor", "nor"],
    "or" => &["and", "xor", "nor"],
    "xor" => &["and", "or", "nor"],
    "nor" => &["and", "or", "xor"],
    "bdse" => &["bdns", "brdse", "brdns", "sdse", "sdns"],
    "bdns" => &["bdse", "brdse", "brdns", "sdse", "sdns"],
    "sqrt" => &["abs", "sin", "cos", "exp", "log"],
    "sin" => &["cos", "tan", "asin", "acos", "atan"],
    "cos" => &["sin", "tan", "asin", "acos", "atan"],
    "move" => &["select", "add", "sub", "l", "peek"],
    "sleep" => &["yield", "hcf"],
    "yield" => &["sleep", "hcf"],
    "alias" => &["define", "label"],
    "define" => &["alias", "label"],
    "lbn" => &["lb", "lbs", "lbns", "sbn", "sb", "sbs"],
    "sbn" => &["sb", "lbn", "lbs", "lbns", "lb", "sbs"],
    "min" => &["max", "add", "sub", "mul", "div"],
    "max" => &["min", "add", "sub", "mul", "div"],
    "floor" => &["round", "trunc", "ceil", "abs"],
    "round" => &["floor", "trunc", "ceil", "abs"],
    "trunc" => &["floor", "round", "ceil", "abs"],
    "snez" => &["seq", "sne", "slt", "sgt"],
    "select" => &["move", "beq", "bne", "and", "or"],
    "bnezal" => &["bnez", "jal", "beqal", "bneal"],
    "lbs" => &["lb", "lbn", "lbns", "sb", "sbn", "sbs"],
    "lbns" => &["lbs", "lbn", "lb", "sbn", "sb", "sbs"],
    "not" => &["and", "or", "xor", "nor"],
    "sla" => &["sll", "sra", "srl"],
    "sll" => &["sla", "sra", "srl"],
    "sra" => &["sla", "sll", "srl"],
    "srl" => &["sla", "sll", "sra"],
    "pow" => &["mul", "div", "exp", "log"],
    "ext" => &["ins", "and", "or", "srl", "sll", "sra", "sla"],
    "ins" => &["ext", "and", "or", "srl", "sll", "sra", "sla"],
    "lerp" => &["add", "sub", "mul", "div", "select"],
    "bdnvl" => &["bdse", "bdns", "brdse", "brdns", "l"],
    "bdnvs" => &["bdse", "bdns", "brdse", "brdns", "s"],
};

/// Helper functions for enhanced hover documentation
pub(crate) fn get_instruction_examples(instruction: &str) -> Option<&'static str> {
    INSTRUCTION_EXAMPLES.get(instruction).copied()
}

pub(crate) fn get_instruction_category(instruction: &str) -> Option<&'static str> {
    INSTRUCTION_CATEGORIES.get(instruction).copied()
}

pub(crate) fn get_related_instructions(instruction: &str) -> Option<&'static [&'static str]> {
    RELATED_INSTRUCTIONS.get(instruction).copied()
}

pub(crate) fn get_instruction_syntax(instruction: &str) -> String {
    use crate::instructions::{DataType, InstructionSignature, INSTRUCTIONS};

    // Explicit parameter label mapping derived from in‑game formatting conventions.
    // Only mnemonic differences are captured; first destination register (r?) usually left unlabeled.
    // For branch instructions last VALUE token is a label target; for *z variants second VALUE is label.
    static PARAM_LABELS: phf::Map<&'static str, &'static [&'static str]> = phf::phf_map! {
        // Stack / memory operations
        "get" => &["dest", "device", "address"],
        "getd" => &["dest", "deviceId", "address"],
        "put" => &["device", "address", "value"],
        "putd" => &["deviceId", "address", "value"],
        "poke" => &["address", "value"],
        "clr" => &["device"],
        "clrd" => &["deviceId"],
        // Device logic I/O
        "l" => &["dest", "device", "logicType"],
        "ld" => &["dest", "deviceId", "logicType"],
        "s" => &["device", "logicType", "source"],
        "sd" => &["deviceId", "logicType", "value"],
        // ss(device, slotIndex, logicSlotType, source)
        "ss" => &["device", "slotIndex", "logicSlotType", "source"],
        // Slot / reagent
        "ls" => &["dest", "device", "slotIndex", "logicSlotType"],
        "lr" => &["dest", "device", "reagentMode", "value"],
        // Batch read/write
        "lb" => &["dest", "deviceHash", "logicType", "batchMode"],
        "lbn" => &["dest", "deviceHash", "nameHash", "logicType", "batchMode"],
        "lbs" => &["dest", "deviceHash", "slotIndex", "logicSlotType", "batchMode"],
        "lbns" => &["dest", "deviceHash", "nameHash", "slotIndex", "logicSlotType", "batchMode"],
        "sb" => &["deviceHash", "logicType", "source"],
        "sbn" => &["deviceHash", "nameHash", "logicType", "source"],
        "sbs" => &["deviceHash", "slotIndex", "logicSlotType", "source"],
        // Register move / math (label a,b,c for clarity; dest omitted as per game rendering examples)
        "move" => &["dest", "value"],
        "add" => &["dest", "a", "b"],
        "sub" => &["dest", "a", "b"],
        "mul" => &["dest", "a", "b"],
        "div" => &["dest", "a", "b"],
        "mod" => &["dest", "a", "b"],
        "pow" => &["dest", "a", "b"],
        "lerp" => &["dest", "a", "b", "t"],
        "ext" => &["dest", "value", "offset", "length"],
        "ins" => &["dest", "value", "offset", "length"],
        // Unary math
        "abs" => &["dest", "a"],
        "acos" => &["dest", "a"],
        "asin" => &["dest", "a"],
        "atan" => &["dest", "a"],
        "ceil" => &["dest", "a"],
        "cos" => &["dest", "a"],
        "exp" => &["dest", "a"],
        "floor" => &["dest", "a"],
        "log" => &["dest", "a"],
        "rand" => &["dest"],
        "round" => &["dest", "a"],
        "sin" => &["dest", "a"],
        "sqrt" => &["dest", "a"],
        "tan" => &["dest", "a"],
        "trunc" => &["dest", "a"],
        // Logic ops
        "and" => &["dest", "a", "b"],
        "nor" => &["dest", "a", "b"],
        "or" => &["dest", "a", "b"],
        "xor" => &["dest", "a", "b"],
        "not" => &["dest", "a"],
        // Shift ops
        "sla" => &["dest", "value", "amount"],
        "sll" => &["dest", "value", "amount"],
        "sra" => &["dest", "value", "amount"],
        "srl" => &["dest", "value", "amount"],
        // Stack register
        "peek" => &["dest"],
        "pop" => &["dest"],
        "push" => &["value"],
        // Comparison set (dest, a, b)
        "select" => &["dest", "a", "b", "c"],
        "sap" => &["dest", "a", "b", "epsilon"],
        "sna" => &["dest", "a", "b", "epsilon"],
        "sapz" => &["dest", "a", "b"],
        "snaz" => &["dest", "a", "b"],
        "slt" => &["dest", "a", "b"],
        "sgt" => &["dest", "a", "b"],
        "sle" => &["dest", "a", "b"],
        "sge" => &["dest", "a", "b"],
        "seq" => &["dest", "a", "b"],
        "sne" => &["dest", "a", "b"],
        "sltz" => &["dest", "a"],
        "sgtz" => &["dest", "a"],
        "slez" => &["dest", "a"],
        "sgez" => &["dest", "a"],
        "seqz" => &["dest", "a"],
        "snez" => &["dest", "a"],
        "snan" => &["dest", "a"],
        "snanz" => &["dest", "a"],
        // Branches (a,b,label) or (a,label)
        "beq" => &["a", "b", "label"],
        "bne" => &["a", "b", "label"],
        "blt" => &["a", "b", "label"],
        "bgt" => &["a", "b", "label"],
        "ble" => &["a", "b", "label"],
        "bge" => &["a", "b", "label"],
        "bap" => &["a", "b", "epsilon"],
        "bna" => &["a", "b", "epsilon"],
        "beqz" => &["a", "label"],
        "bnez" => &["a", "label"],
        "bltz" => &["a", "label"],
        "bgtz" => &["a", "label"],
        "blez" => &["a", "label"],
        "bgez" => &["a", "label"],
        "bapz" => &["a", "epsilon"],
        "bnaz" => &["a", "epsilon"],
        "bnan" => &["a", "label"],
        // Relative branches
        "brlt" => &["a", "b", "label"],
        "brgt" => &["a", "b", "label"],
        "brle" => &["a", "b", "label"],
        "brge" => &["a", "b", "label"],
        "breq" => &["a", "b", "label"],
        "brne" => &["a", "b", "label"],
        "brap" => &["a", "b", "epsilon"],
        "brna" => &["a", "b", "epsilon"],
        "brltz" => &["a", "label"],
        "brgtz" => &["a", "label"],
        "brlez" => &["a", "label"],
        "brgez" => &["a", "label"],
        "breqz" => &["a", "label"],
        "brnez" => &["a", "label"],
        "brapz" => &["a", "epsilon"],
        "brnaz" => &["a", "epsilon"],
        "brnan" => &["a", "label"],
        // Branch & link
        "beqal" => &["a", "b", "label"],
        "bneal" => &["a", "b", "label"],
        "bltal" => &["a", "b", "label"],
        "bgtal" => &["a", "b", "label"],
        "bleal" => &["a", "b", "label"],
        "bgeal" => &["a", "b", "label"],
        "beqzal" => &["a", "label"],
        "bnezal" => &["a", "label"],
        "bltzal" => &["a", "label"],
        "bgtzal" => &["a", "label"],
        "blezal" => &["a", "label"],
        "bgezal" => &["a", "label"],
        "bapzal" => &["a", "epsilon"],
        "bnazal" => &["a", "epsilon"],
        // Device validity branches
        "bdse" => &["device", "label"],
        "bdns" => &["device", "label"],
        "brdse" => &["device", "label"],
        "brdns" => &["device", "label"],
        "bdseal" => &["device", "label"],
        "bdnsal" => &["device", "label"],
        "bdnvl" => &["device", "logicType", "label"],
        "bdnvs" => &["device", "logicType", "label"],
        // Device status to register (dest, device)
        "sdse" => &["dest", "device"],
        "sdns" => &["dest", "device"],
        // Jumps
        "j" => &["label"],
        "jal" => &["label"],
        "jr" => &["address"],
        // Misc / map
        "rmap" => &["dest", "device", "reagentHash"],
        // Assembly/meta
        "alias" => &["name", "target"],
        "label" => &["name", "target"],
        "define" => &["name", "value"],
        // Timing / control
        "sleep" => &["seconds"],
        "yield" => &[],
        "hcf" => &[],
    };

    fn format_union_with_label(
        label: &str,
        opcode: &str,
        u: &crate::instructions::Union,
    ) -> String {
        use crate::instructions::DataType;
        // Special cases
        if label == "device" || label == "deviceId" {
            return format!("{}(d?|r?|id)", label);
        }
        if label == "deviceHash" || label == "nameHash" {
            return format!("{}(r?|num)", label);
        }
        if label == "slotIndex"
            || label == "address"
            || label == "value"
            || label == "a"
            || label == "b"
            || label == "c"
            || label == "t"
            || label == "epsilon"
            || label == "offset"
            || label == "length"
            || label == "seconds"
        {
            return format!("{}(r?|num)", label);
        }
        if label == "logicType" {
            return "logicType".to_string();
        }
        if label == "logicSlotType" {
            return "logicSlotType".to_string();
        }
        if label == "batchMode" {
            return "batchMode".to_string();
        }
        if label == "reagentMode" {
            return "reagentMode".to_string();
        }
        if label == "name" {
            return "name".to_string();
        }
        if label == "target" {
            return "target(r?|d?|num)".to_string();
        }
        if label == "source" {
            return "source(r?)".to_string();
        }
        if label == "dest" {
            return "r?".to_string();
        }
        if label == "label" {
            return "label(r?|num)".to_string();
        }
        if label == "address" {
            return "address(r?|num)".to_string();
        }
        if label == "seconds" {
            return "seconds(r?|num)".to_string();
        }
        // Fallback: infer from union
        let contains_reg = u.match_type(DataType::Register);
        let contains_num = u.match_type(DataType::Number);
        if contains_reg && contains_num {
            return format!("{}(r?|num)", label);
        }
        if contains_reg {
            return format!("{}(r?)", label);
        }
        if contains_num {
            return format!("{}(num)", label);
        }
        // device etc already handled above; show label raw
        label.to_string()
    }

    if let Some(InstructionSignature(params)) = INSTRUCTIONS.get(instruction) {
        let labels = PARAM_LABELS.get(instruction).copied().or_else(|| {
            // Heuristic: generate sensible labels based on common patterns when not explicitly defined
            // - First REGISTER => "dest"
            // - Subsequent VALUEs => a, b, c in order
            // - Preserve well-known typed names for logicType, slotLogicType, batchMode, reagentMode, device
            use crate::instructions::DataType;
            let mut dyn_labels: Vec<&'static str> = Vec::with_capacity(params.len());
            let mut next_ab = 0usize;
            for (idx, u) in params.iter().enumerate() {
                if u.match_type(DataType::Device) {
                    dyn_labels.push("device");
                } else if u.match_type(DataType::LogicType) {
                    dyn_labels.push("logicType");
                } else if u.match_type(DataType::SlotLogicType) {
                    dyn_labels.push("logicSlotType");
                } else if u.match_type(DataType::BatchMode) {
                    dyn_labels.push("batchMode");
                } else if u.match_type(DataType::ReagentMode) {
                    dyn_labels.push("reagentMode");
                } else if u.match_type(DataType::Name) {
                    dyn_labels.push("name");
                } else if idx == 0 && u.match_type(DataType::Register) {
                    dyn_labels.push("dest");
                } else {
                    let lbl = match next_ab {
                        0 => "a",
                        1 => "b",
                        2 => "c",
                        3 => "t",
                        _ => "value",
                    };
                    dyn_labels.push(lbl);
                    next_ab += 1;
                }
            }
            Some(Box::leak(dyn_labels.into_boxed_slice()) as &'static [&'static str])
        });
        let mut out = String::with_capacity(96);
        out.push_str(instruction);
        for (idx, u) in params.iter().enumerate() {
            out.push(' ');
            if let Some(lbls) = labels {
                if idx < lbls.len() {
                    out.push_str(&format_union_with_label(lbls[idx], instruction, u));
                    continue;
                }
            }
            // Fallback (no label): use register or union textual form similar to previous logic
            use crate::instructions::DataType;
            if u.match_type(DataType::Device) {
                out.push_str("device(d?|r?|id)");
                continue;
            }
            if u.match_type(DataType::LogicType) {
                out.push_str("logicType");
                continue;
            }
            if u.match_type(DataType::SlotLogicType) {
                out.push_str("logicSlotType");
                continue;
            }
            if u.match_type(DataType::BatchMode) {
                out.push_str("batchMode");
                continue;
            }
            if u.match_type(DataType::ReagentMode) {
                out.push_str("reagentMode");
                continue;
            }
            if u.match_type(DataType::Name) {
                out.push_str("name");
                continue;
            }
            let contains_reg = u.match_type(DataType::Register);
            let contains_num = u.match_type(DataType::Number);
            match (contains_reg, contains_num) {
                (true, true) => out.push_str("(r?|num)"),
                (true, false) => out.push_str("r?"),
                (false, true) => out.push_str("num"),
                _ => out.push_str("..."),
            }
        }
        out
    } else {
        instruction.to_string()
    }
}

/// Create enhanced hover content for instructions with examples, syntax, and related commands
pub(crate) fn create_enhanced_instruction_hover(
    instruction: &str,
) -> Vec<tower_lsp::lsp_types::MarkedString> {
    use tower_lsp::lsp_types::{LanguageString, MarkedString};

    let mut hover_content = Vec::new();

    // Add instruction syntax
    let syntax = get_instruction_syntax(instruction);
    hover_content.push(MarkedString::LanguageString(LanguageString {
        language: "ic10".to_string(),
        value: syntax,
    }));

    // Build markdown content
    let mut markdown_parts = Vec::new();

    // Add instruction title and description
    if let Some(doc) = crate::instructions::INSTRUCTION_DOCS.get(instruction) {
        markdown_parts.push(format!("**{}**\n\n{}", instruction, doc));
    } else {
        markdown_parts.push(format!("**{}**", instruction));
    }

    // Add the description first
    let initial_content = markdown_parts.join("\n\n");
    hover_content.push(MarkedString::String(initial_content));

    // Add examples immediately after description (restore original order)
    if let Some(examples) = get_instruction_examples(instruction) {
        hover_content.push(MarkedString::String("**Examples:**".to_string()));

        // Split examples by newlines and add each as a syntax-highlighted language string
        let example_lines: Vec<&str> = examples.split('\n').collect();
        for example in example_lines {
            if !example.trim().is_empty() {
                hover_content.push(MarkedString::LanguageString(LanguageString {
                    language: "ic10".to_string(),
                    value: example.trim().to_string(),
                }));
            }
        }
    }

    // Add category and related instructions at the bottom
    let mut bottom_parts = Vec::new();

    if let Some(category) = get_instruction_category(instruction) {
        bottom_parts.push(format!("**Category:** {}", category));
    }

    if let Some(related) = get_related_instructions(instruction) {
        if !related.is_empty() {
            let related_list = related
                .iter()
                .take(5) // Limit to avoid overwhelming the tooltip
                .map(|r| format!("`{}`", r))
                .collect::<Vec<_>>()
                .join(", ");
            bottom_parts.push(format!("**Related:** {}", related_list));
        }
    }

    // Add interactive guidance hint only
    bottom_parts.push("---".to_string());
    if let Some(_category) = get_instruction_category(instruction) {
        bottom_parts.push(
            "💡 **Interactive Actions:** Press **Ctrl+.** or click the lightbulb 💡".to_string(),
        );
    }

    // Add the bottom content
    if !bottom_parts.is_empty() {
        let bottom_content = bottom_parts.join("\n\n");
        hover_content.push(MarkedString::String(bottom_content));
    }
    // Deduplicate identical ic10 language blocks (avoid double signature lines)
    let mut seen_ic10: std::collections::HashSet<String> = std::collections::HashSet::new();
    hover_content
        .into_iter()
        .filter(|ms| match ms {
            MarkedString::LanguageString(lang) if lang.language.eq_ignore_ascii_case("ic10") => {
                let key = lang.value.trim().to_string();
                if seen_ic10.contains(&key) {
                    false
                } else {
                    seen_ic10.insert(key);
                    true
                }
            }
            _ => true,
        })
        .collect()
}

/// Create enhanced hover content with integrated operation history
pub(crate) fn create_enhanced_instruction_hover_with_history(
    instruction: &str,
    instruction_node: tree_sitter::Node,
    content: &str,
    register_analyzer: &crate::additional_features::RegisterAnalyzer,
) -> Vec<tower_lsp::lsp_types::MarkedString> {
    use tower_lsp::lsp_types::MarkedString;

    // Start with the base instruction hover content
    let mut hover_content = create_enhanced_instruction_hover(instruction);

    // Try to find registers in this instruction and add their operation history
    let mut register_histories = Vec::new();

    // Parse the instruction to find registers
    let mut cursor = instruction_node.walk();
    for child in instruction_node.children(&mut cursor) {
        if child.kind() == "register" {
            if let Ok(register_name) = child.utf8_text(content.as_bytes()) {
                if let Some(register_info) = register_analyzer.get_register_info(register_name) {
                    if !register_info.operation_history.is_empty() {
                        let display_name = register_info
                            .alias_name
                            .as_ref()
                            .map(|alias| format!("{} ({})", alias, register_name))
                            .unwrap_or_else(|| register_name.to_string());

                        let mut history_parts =
                            vec![format!("**Register {} Operation History:**", display_name)];

                        // Limit history to avoid overwhelming the tooltip
                        let history_limit = 5; // Fewer entries when combined with instruction docs
                        let start_idx = if register_info.operation_history.len() > history_limit {
                            register_info.operation_history.len() - history_limit
                        } else {
                            0
                        };

                        for record in &register_info.operation_history[start_idx..] {
                            history_parts.push(format!(
                                "  • Line {}: `{}`",
                                record.line_number, record.operation
                            ));
                        }

                        if start_idx > 0 {
                            history_parts.push(format!("  ... and {} more operations", start_idx));
                        }

                        register_histories.push(history_parts.join("\n"));
                    }
                }
            }
        }
    }

    // Add register histories if any were found
    if !register_histories.is_empty() {
        hover_content.push(MarkedString::String("---".to_string()));
        for history in register_histories {
            hover_content.push(MarkedString::String(history));
        }
    }
    // Final dedup pass for ic10 blocks (some history additions may repeat opcode line)
    use tower_lsp::lsp_types::MarkedString as MS;
    let mut seen: std::collections::HashSet<String> = std::collections::HashSet::new();
    hover_content
        .into_iter()
        .filter(|ms| match ms {
            MS::LanguageString(lang) if lang.language.eq_ignore_ascii_case("ic10") => {
                let key = lang.value.trim().to_string();
                if seen.contains(&key) {
                    false
                } else {
                    seen.insert(key);
                    true
                }
            }
            _ => true,
        })
        .collect()
}
