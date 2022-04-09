namespace Spice86.Emulator.Function.Dump;

using Spice86.Emulator.CPU;
using Spice86.Emulator.Memory;
using Spice86.Utils;

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

public abstract class ClrFunctionToStringConverter : FunctionInformationToStringConverter {
    private static readonly SegmentRegisters _segmentRegisters = new();
    public override string GetFileHeader(List<SegmentRegisterBasedAddress> allPotentialGlobals, HashSet<SegmentedAddress> whiteListOfSegmentForOffset) {

        // Take only addresses which have been accessed (and not only computed)
        List<SegmentRegisterBasedAddress> globals = allPotentialGlobals
            .Where(x => x.AddressOperations.Any())
            .Where(y => whiteListOfSegmentForOffset
                .All(z => IsOffsetEqualsAndSegmentDifferent(y, z) == false))
            .ToList();
        int numberOfGlobals = globals.Count;

        // Various classes with values per segment
        string globalsContent = JoinNewLine(
            MapBySegment(globals)
                .Select(x => GenerateClassForGlobalsOnSegment(x.Key, x.Value))
        );
        string segmentValues = JoinNewLine(
            GetValuesTakenBySegments(globals)
                .Select(x => GetStringSegmentValuesForDisplay(x.Key, x.Value)));
        return GenerateFileHeaderWithAccessors(numberOfGlobals, globalsContent, segmentValues);
    }

    protected abstract string GenerateFileHeaderWithAccessors(int numberOfGlobals, string globalsContent, string segmentValues);
    private static bool IsOffsetEqualsAndSegmentDifferent(SegmentedAddress address1, SegmentedAddress address2) {
        return address1.Segment != address2.Segment && address1.Offset == address2.Offset;
    }

    private static string GetStringSegmentValuesForDisplay(int segmentIndex, IEnumerable<ushort> values) {
        string segmentName = _segmentRegisters.GetRegName(segmentIndex);
        string segmentValues = string.Join(",", values.Select(x => $"{ConvertUtils.ToHex16(x)}"));
        return $"{segmentName}:{segmentValues}";
    }

    private static Dictionary<int, List<ushort>> GetValuesTakenBySegments(List<SegmentRegisterBasedAddress> globals) {
        return MapBySegment(globals)
            .ToDictionary(
                x => x.Key,
                (x) => GetSegmentValues(x.Value));
    }

    private static Dictionary<ushort, List<SegmentRegisterBasedAddress>> GetAddressesBySegmentValues(ISet<SegmentRegisterBasedAddress> globals) {
        return globals.GroupBy(
                x => x.Segment)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static List<ushort> GetSegmentValues(ISet<SegmentRegisterBasedAddress> globals) {
        return globals.Select(x => x.Segment).Distinct().ToList();
    }

    private static Dictionary<int, ISet<SegmentRegisterBasedAddress>> MapBySegment(List<SegmentRegisterBasedAddress> globals) {
        Dictionary<int, ISet<SegmentRegisterBasedAddress>> res = new();
        foreach (SegmentRegisterBasedAddress address in globals) {
            IEnumerable<int> segmentIndexes = address.AddressOperations
                .Values
                .SelectMany(x => x);
            foreach (int segmentIndex in segmentIndexes) {
                if (!res.TryGetValue(segmentIndex, out ISet<SegmentRegisterBasedAddress>? addressesForSegment)) {
                    addressesForSegment = new HashSet<SegmentRegisterBasedAddress>();
                    res.Add(segmentIndex, addressesForSegment);
                }
                addressesForSegment.Add(address);
            }
        }
        return res;
    }

    private string GenerateClassForGlobalsOnCS(ISet<SegmentRegisterBasedAddress> globals) {
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
    private string GenerateClassForGlobalsOnSegment(int segmentIndex, ISet<SegmentRegisterBasedAddress> globals) {
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
        Dictionary<AddressOperation, ISet<int>> addressOperations = address.AddressOperations;
        if (addressOperations.Any() == false) {
            // Nothing was ever read or written there
            return "";
        }
        // TreeMap so already sorted
        string gettersAndSetters = JoinNewLine(
            new SortedDictionary<AddressOperation, ISet<int>>(CompleteWithOppositeOperationsAndPointers(addressOperations))
                .Select(x => GenerateAddressOperationAsGetterOrSetter(x.Key, x.Value, address))
            );
        return $"// Getters and Setters for address {address}.{gettersAndSetters}";
    }

    private static Dictionary<AddressOperation, ISet<int>> CompleteWithOppositeOperationsAndPointers(Dictionary<AddressOperation, ISet<int>> addressOperations) {

        // Ensures that for each read there is a write, even with empty registers so that we can generate valid java
        // properties
        Dictionary<AddressOperation, ISet<int>> res = new(addressOperations);
        foreach (AddressOperation operation in addressOperations.Keys) {
            OperandSize operandSize = operation.OperandSize;
            ValueOperation valueOperation = operation.ValueOperation;
            ValueOperation oppositeValueOperation = valueOperation.OppositeOperation();
            res.ComputeIfAbsent(new AddressOperation(oppositeValueOperation, operandSize), new HashSet<int>());
            if (operandSize == OperandSize.Dword32) {
                // Ensures getter and setters are generated for segmented address accessors
                res.ComputeIfAbsent(new AddressOperation(valueOperation, OperandSize.Dword32Ptr), new HashSet<int>());
                res.ComputeIfAbsent(new AddressOperation(oppositeValueOperation, OperandSize.Dword32Ptr), new HashSet<int>());
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

        OperandSize operandSize = addressOperation.OperandSize;
        string javaName = $"{operandSize.Name}_{ConvertUtils.ToCSharpString(address)}";
        string? name = address.Name;
        if (string.IsNullOrWhiteSpace(name) == false) {
            javaName += "_" + name;
        }

        string offset = ConvertUtils.ToHex16(address.Offset);
        if (ValueOperation.READ.Equals(addressOperation.ValueOperation)) {
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
        if (functionInformation.HasOverride) {
            return GetNoStubReasonCommentForMethod(functionInformation, "Function already has an override");
        }

        List<CallType> returnTypes = functionInformation.Returns.Keys.Concat(functionInformation.UnalignedReturns.Keys)
            .Select(x => x.ReturnCallType)
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
        string? functionName = RemoveDotsFromFunctionName(functionInformation.Name);
        SegmentedAddress functionAddress = functionInformation.Address;
        string functionNameInJava = ToCSharpName(functionInformation, false);
        string segment = ConvertUtils.ToHex16(functionAddress.Segment);
        string offset = ConvertUtils.ToHex16(functionAddress.Offset);
        string retType = returnType.ToString().ToLowerInvariant();
        return GenerateFunctionStub(callsAsComments, functionName, functionNameInJava, segment, offset, retType);
    }

    protected abstract string GenerateFunctionStub(string callsAsComments, string? functionName, string functionNameInJava, string segment, string offset, string retType);
    private string GetCallsAsComments(IEnumerable<FunctionInformation> calls) {
        return JoinNewLine(calls.Select(x => $"// {ToCSharpName(x, true)}();"));
    }

    private static string GetNoStubReasonCommentForMethod(FunctionInformation functionInformation, string reason) {
        return $"  // Not providing stub for {functionInformation.Name}. Reason: {reason}\n";
    }
}