namespace Spice86.Emulator.Function.Dump;

using System;

/// <summary>
/// Converts FunctionInformation to java stubs for easy override
/// </summary>
public class CSharpStubToStringConverter : ClrFunctionToStringConverter {
    protected override string GenerateClassForGlobalsOnCSWithValue(string segmentValueHex, string globalsContent) {
        return
            $@"
            public class GlobalsOnCsSegment{segmentValueHex} : MemoryBasedDataStructureWithBaseAddress {{
                public GlobalsOnCsSegment{segmentValueHex}(Machine machine) {{
                  base(machine.GetMemory(), {segmentValueHex} * 0x10);
                }}

                {globalsContent}
            }}
            ";
    }

    protected override string GenerateClassForGlobalsOnSegment(string segmentName, string segmentNameCamel, string globalsContent) {
        return
            $@"
            // Accessors for values accessed via register {segmentName}
            public class GlobalsOn{segmentNameCamel} : MemoryBasedDataStructureWith{segmentNameCamel}BaseAddress {{
                public GlobalsOn{segmentNameCamel}(Machine machine) {{
                    base(machine);
                }}

                {globalsContent};
                }}
            ";
    }

    protected override string GenerateFileHeaderWithAccessors(int numberOfGlobals, String globalsContent, String segmentValues) {
        return
            $@"
            using System;
            using System.Linq;
            using System.Collections.Generic;

            using Spice86.Emulator.Function;
            using Spice86.Emulator.Machine;
            using Spice86.Emulator.Memory;
            using Spice86.Emulator.Reverseengineer;

            ///<summary>
            /// Getters and setters for what could be global variables, split per segment register. {0} addresses in total.
            /// Observed values for segments:
            /// {segmentValues}
            /// Number of globals:
            /// {numberOfGlobals}
            /// Globals content:
            /// {globalsContent}
            /// Stubs for overrides
            /// </summary>
            public class Stubs : CSharpOverrideHelper {{
                public Stubs(Dictionary<SegmentedAddress, FunctionInformation> functionInformations, string prefix, Machine machine) {{
                    base(functionInformations, prefix, machine);
                }}
            ";
    }

    protected override string GenerateFunctionStub(string callsAsComments, string? functionName, string functionNameInCSharp, string segment, string offset, string retType) {
        return
            $@"
            // defineFunction({segment}, {offset}, ""{functionName}"", this.{functionNameInCSharp});
            public Action {functionNameInCSharp}() {{
                {callsAsComments,-4}
                return {retType}Ret();
            }}";
    }

    protected override string GenerateNonPointerGetter(string comment, string cSharpName, string offset, int bits) {
        return
            $@"
            {comment}
            public int Get{cSharpName}() {{
                return GetUint{bits}({offset});
            }}
            ";
    }

    protected override string GenerateNonPointerSetter(string comment, string cSharpName, string offset, int bits) {
        return
            $@"
            {comment}
            public void Set{cSharpName}(int value) {{
                SetUint{bits}({offset}, value);
            }}
            ";
    }

    protected override string GeneratePointerGetter(string comment, string cSharpName, string offset) {
        return
            $@"
            {comment}
            public SegmentedAddress GetPtr{cSharpName}() {{
                return new SegmentedAddress(GetUint16({offset} + 2), GetUint16({offset}));
            }}
            ";
    }

    protected override string GeneratePointerSetter(string comment, string cSharpName, string offset) {
        return
            $@"
            {comment}
            public void SetPtr{cSharpName}(SegmentedAddress value) {{
                SetUint16({offset} + 2, value.GetSegment());
                SetUint16({offset}, value.GetOffset());
            }}
            ";
    }
}
