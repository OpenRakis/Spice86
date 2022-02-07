namespace Spice86.Emulator.Function.Dump;

using Spice86.Emulator.CPU;
using Spice86.Emulator.Memory;
using Spice86.Utils;

using System.Collections.Generic;
using System.Linq;
public abstract class ClrFunctionToStringConverter : FunctionInformationToStringConverter {
    private static readonly SegmentRegisters _segmentRegisters = new();
    public override string GetFileHeader(List<SegmentRegisterBasedAddress> allPotentialGlobals, HashSet<SegmentedAddress> whiteListOfSegmentForOffset) {

        // Take only addresses which have been accessed (and not only computed)
        List<SegmentRegisterBasedAddress> globals = allPotentialGlobals
            .Where(x => (x.GetAddressOperations()).Any())
                .Where(y => whiteListOfSegmentForOffset
                    .All(z => IsOffsetEqualsAndSegmentDifferent(y, z) == false))
            .ToList();
        int numberOfGlobals = globals.Count;

        // Various classes with values per segment
        string globalsContent = JoinNewLine(MapBySegment(globals).ToDictionary(
            x => x.Key, 
            x => GenerateClassForGlobalsOnSegment(x.Key, x.Value)).Values);
        string segmentValues = JoinNewLine(GetValuesTakenBySegments(globals).ToDictionary(
            x => x.Key, 
            x => GetStringSegmentValuesForDisplay(x.Key, x.Value)).Values);
        return GenerateFileHeaderWithAccessors(numberOfGlobals, globalsContent, segmentValues);
    }

    protected abstract string GenerateFileHeaderWithAccessors(int numberOfGlobals, string globalsContent, string segmentValues);
    private bool IsOffsetEqualsAndSegmentDifferent(SegmentedAddress address1, SegmentedAddress address2) {
        return address1.GetSegment() != address2.GetSegment() && address1.GetOffset() == address2.GetOffset();
    }

    private string GetStringSegmentValuesForDisplay(int segmentIndex, IEnumerable<ushort> values) {
        string segmentName = _segmentRegisters.GetRegName(segmentIndex);
        string segmentValues = string.Join(",", values.Select(x => $"{ConvertUtils.ToHex16(x)}"));
        return $"{segmentName}:{segmentValues}";
    }

    private Dictionary<int, List<ushort>> GetValuesTakenBySegments(List<SegmentRegisterBasedAddress> globals) {
        return MapBySegment(globals)
            .ToDictionary(
                x => x.Key,
                (x) => GetSegmentValues(x.Value));
    }

    private Dictionary<ushort, List<SegmentRegisterBasedAddress>> GetAddressesBySegmentValues(List<SegmentRegisterBasedAddress> globals) {
        return globals
            .ToDictionary(
                x => x.GetSegment()
                , (x) => globals
                    .Where(y => x.GetSegment() == y.GetSegment()).ToList()
                );
    }

    private List<ushort> GetSegmentValues(List<SegmentRegisterBasedAddress> globals) {
        return globals.Select(x => x.GetSegment()).ToList();
    }

    private Dictionary<int, List<SegmentRegisterBasedAddress>> MapBySegment(List<SegmentRegisterBasedAddress> globals) {
        Dictionary<int, List<SegmentRegisterBasedAddress>> res = new();
        foreach (SegmentRegisterBasedAddress address in globals) {
            List<int> segmentIndexes = address.GetAddressOperations()
                .Values
                .SelectMany(x => x)
                .ToList();
                
            segmentIndexes.ForEach((segmentIndex) => res
                    .ComputeIfAbsent(segmentIndex, new List<SegmentRegisterBasedAddress>())
                    .Add(address));
        }
        return res;
    }

    private string GenerateClassForGlobalsOnCS(List<SegmentRegisterBasedAddress> globals) {
        // CS is special, program cannot explicitly change it in the emulator, and it doesn't usually change in the
        // overrides when it should.
        return JoinNewLine(GetAddressesBySegmentValues(globals).Select(x => GenerateClassForGlobalsOnCSWithValue(x.Key, x.Value)));
    }

    private string GenerateClassForGlobalsOnCSWithValue(ushort segmentValue, IEnumerable<SegmentRegisterBasedAddress> globals) {
        string segmentValueHex = ConvertUtils.ToHex16(segmentValue);
        string globalsContent = GenerateGettersSettersForAddresses(globals);
        return GenerateClassForGlobalsOnCSWithValue(segmentValueHex, globalsContent);
    }

    protected abstract string GenerateClassForGlobalsOnCSWithValue(string segmentValueHex, string globalsContent);
    private string GenerateClassForGlobalsOnSegment(int segmentIndex, List<SegmentRegisterBasedAddress> globals) {
        if (SegmentRegisters.CsIndex == segmentIndex) {
            return GenerateClassForGlobalsOnCS(globals);
        }

        string segmentName = _segmentRegisters.GetRegName(segmentIndex);
        string segmentNameCamel = segmentName.ToUpperInvariant();

        // Generate accessors
        string globalsContent = GenerateGettersSettersForAddresses(globals);
        return GenerateClassForGlobalsOnSegment(segmentName, segmentNameCamel, globalsContent);
    }

    protected abstract string GenerateClassForGlobalsOnSegment(string segmentName, string segmentNameCamel, string globalsContent);
    private string GenerateGettersSettersForAddresses(IEnumerable<SegmentRegisterBasedAddress> addresses) {
        return JoinNewLine(addresses
            .OrderBy(x => x)
            .Select(x => this.GenerateGetterSetterForAddress(x)));
    }

    private string GenerateGetterSetterForAddress(SegmentRegisterBasedAddress address) {
        Dictionary<AddressOperation, List<int>> addressOperations = address.GetAddressOperations();
        if (addressOperations.Any() == false) {
            // Nothing was ever read or written there
            return "";
        }
        // TreeMap so already sorted
        string gettersAndSetters = JoinNewLine(
            CompleteWithOppositeOperationsAndPointers(addressOperations)
            .Select(x => GenerateAddressOperationAsGetterOrSetter(x.Key, x.Value, address)));
        return $"// Getters and Setters for address {address}.{gettersAndSetters}";
    }

    private Dictionary<AddressOperation, List<int>> CompleteWithOppositeOperationsAndPointers(Dictionary<AddressOperation, List<int>> addressOperations) {

        // Ensures that for each read there is a write, even with empty registers so that we can generate valid java
        // properties
        Dictionary<AddressOperation, List<int>> res = new(addressOperations);
        foreach (AddressOperation operation in addressOperations.Keys) {
            OperandSize operandSize = operation.GetOperandSize();
            ValueOperation valueOperation = operation.GetValueOperation();
            ValueOperation oppositeValueOperation = valueOperation.OppositeOperation();
            res.ComputeIfAbsent(new AddressOperation(oppositeValueOperation, operandSize), new List<int>());
            if (operandSize == OperandSize.Dword32) {
                // Ensures getter and setters are generated for segmented address accessors
                res.ComputeIfAbsent(new AddressOperation(valueOperation, OperandSize.Dword32Ptr), new List<int>());
                res.ComputeIfAbsent(new AddressOperation(oppositeValueOperation, OperandSize.Dword32Ptr), new List<int>());
            }
        }

        return res;
    }

    private string GenerateAddressOperationAsGetterOrSetter(AddressOperation addressOperation, IEnumerable<int> registerIndexes, SegmentRegisterBasedAddress address) {
        string comment = "// Operation not registered by running code";
        if (!registerIndexes.Any() == false) {
            IEnumerable<string> registersArray = registerIndexes
                .Select(x => _segmentRegisters.GetRegName(x))
                .OrderBy(x => x);
            string registers = string.Join(", ", registersArray);
            comment = "// Was accessed via the following registers: " + registers;
        }

        OperandSize operandSize = addressOperation.GetOperandSize();
        string javaName = $"{operandSize}_{ConvertUtils.ToCSharpString(address)}";
        string? name = address.GetName();
        if (string.IsNullOrWhiteSpace(name) == false) {
            javaName += "_" + name;
        }

        string offset = ConvertUtils.ToHex16(address.GetOffset());
        if (ValueOperation.READ.Equals(addressOperation.GetValueOperation())) {
            return GenerateGetter(comment, operandSize, javaName, offset);
        }


        // WRITE
        return GenerateSetter(comment, operandSize, javaName, offset);
    }

    private string GenerateGetter(string comment, OperandSize operandSize, string javaName, string offset) {
        if (operandSize == OperandSize.Dword32Ptr) {

            // segmented address
            return GeneratePointerGetter(comment, javaName, offset);
        }

        int bits = operandSize.Bits;
        return GenerateNonPointerGetter(comment, javaName, offset, bits);
    }

    protected abstract string GeneratePointerGetter(string comment, string javaName, string offset);
    protected abstract string GenerateNonPointerGetter(string comment, string javaName, string offset, int bits);
    private string GenerateSetter(string comment, OperandSize operandSize, string javaName, string offset) {
        if (operandSize == OperandSize.Dword32Ptr) {

            // segmented address
            return GeneratePointerSetter(comment, javaName, offset);
        }

        int bits = operandSize.Bits;
        return GenerateNonPointerSetter(comment, javaName, offset, bits);
    }

    protected abstract string GeneratePointerSetter(string comment, string javaName, string offset);
    protected abstract string GenerateNonPointerSetter(string comment, string javaName, string offset, int bits);
    public override string GetFileFooter() {
        return "}";
    }

    public override string Convert(FunctionInformation functionInformation, IEnumerable<FunctionInformation> allFunctions) {
        if (functionInformation.HasOverride()) {
            return GetNoStubReasonCommentForMethod(functionInformation, "Function already has an override");
        }

        List<CallType> returnTypes = functionInformation.GetReturns().Keys.
            Concat(functionInformation.GetUnalignedReturns().Keys)
            .Select(x => x.GetReturnCallType())
            .Distinct()
            .ToList();
        if (returnTypes.Count != 1) {

            // Cannot generate code with either no return or mixed returns
            string reason = "Function has no return";
            if (!returnTypes.Any() == false) {
                reason = $"Function has different return types: {string.Join(",", returnTypes)}";
            }

            return GetNoStubReasonCommentForMethod(functionInformation, reason);
        }

        IEnumerable<FunctionInformation> calls = this.GetCalls(functionInformation, allFunctions);
        string callsAsComments = this.GetCallsAsComments(calls);
        CallType returnType = returnTypes[0];
        string? functionName = RemoveDotsFromFunctionName(functionInformation.GetName());
        SegmentedAddress functionAddress = functionInformation.GetAddress();
        string functionNameInJava = ToCSharpName(functionInformation, false);
        string segment = ConvertUtils.ToHex16(functionAddress.GetSegment());
        string offset = ConvertUtils.ToHex16(functionAddress.GetOffset());
        string retType = returnType.ToString().ToLowerInvariant();
        return GenerateFunctionStub(callsAsComments, functionName, functionNameInJava, segment, offset, retType);
    }

    protected abstract string GenerateFunctionStub(string callsAsComments, string? functionName, string functionNameInJava, string segment, string offset, string retType);
    private string GetCallsAsComments(IEnumerable<FunctionInformation> calls) {
        return JoinNewLine(calls.Select(x => $"// {ToCSharpName(x, true)}();"));
    }

    private string GetNoStubReasonCommentForMethod(FunctionInformation functionInformation, string reason) {
        return $"  // Not providing stub for {functionInformation.GetName()}. Reason: {reason}\n";
    }
}
