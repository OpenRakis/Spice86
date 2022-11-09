namespace Spice86.Core.Emulator.Function.Dump;

/// <summary>
/// Converts FunctionInformation to C# stubs for easy override
/// </summary>
public class CSharpStubToStringConverter : ClrFunctionToStringConverter {
    protected override string GenerateClassForGlobalsOnCSWithValue(string segmentValueHex, string globalsContent) {
        return
            $@"
            public class GlobalsOnCsSegment{segmentValueHex} : MemoryBasedDataStructureWithBaseAddress {{
                public GlobalsOnCsSegment{segmentValueHex}(Machine machine) : base(machine.Memory, {segmentValueHex} * 0x10) {{
                  
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
                public GlobalsOn{segmentNameCamel}(Machine machine) : base(machine) {{
                    
                }}

                {globalsContent};
                }}
            ";
    }

    protected override string GenerateFileHeaderWithAccessors(int numberOfGlobals, string globalsContent, string segmentValues) {
        return
            $@"
namespace Stubs;
using System;
using System.Linq;
using System.Collections.Generic;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;

///<summary>
/// Getters and setters for what could be global variables, split per segment register. {0} addresses in total.
/// Observed values for segments:
/// {segmentValues}
/// Number of globals:
/// {numberOfGlobals}
/// Globals content:
/// {globalsContent}
/// <summary>
/// Stubs for overrides
/// </summary>
public class Stubs : CSharpOverrideHelper {{
    public Stubs(Dictionary<SegmentedAddress, FunctionInformation> functionInformations, Machine machine) : base(functionInformations, machine) {{
        
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
        string downCast = "(byte)";
        if(bits is 16) {
            downCast = "(ushort)";
        }
        else if(bits > 16) {
            downCast = "(uint)";
        }
        return
    $@"
    {comment}
    public void Set{cSharpName}(int value) {{
        SetUint{bits}({offset}, {downCast}value);
    }}
    ";
    }

    protected override string GeneratePointerGetter(string comment, string cSharpName, string offset) {
        return
            $@"
    {comment}
    public SegmentedAddress GetPtr{cSharpName}() {{
        return new SegmentedAddress(GetUint16((ushort){offset} + 2), GetUint16((ushort){offset}));
    }}
    ";
    }

    protected override string GeneratePointerSetter(string comment, string cSharpName, string offset) {
        return
            $@"
    {comment}
    public void SetPtr{cSharpName}(SegmentedAddress value) {{
        SetUint16((ushort){offset} + 2, (ushort)value.Segment);
        SetUint16((ushort){offset}, (ushort)value.Offset);
    }}
    ";
    }
}
