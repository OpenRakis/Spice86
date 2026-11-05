namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ranges;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// Where every constant global access of a program prints through, decided once over the prepared
/// IR of every method after the segment summaries converge: a <see cref="SegmentedPointerNode"/>
/// the segment value proof resolves to <see cref="SegmentValue.Known"/> prints through the location
/// class of that value (<c>Segment2000Globals</c> outside every DOS-loaded image,
/// <c>Image0010Globals</c> / <c>ImageMinus0003Globals</c> inside one); otherwise the first register
/// in class order DS, ES, CS, FS, GS provably equal to its register gives its register class
/// (which may be its own). A pointer the classification does not know - one the printed body holds
/// that no IR statement walked - prints through its register class and counts as
/// <c>unclassified</c> in the census. One node instance two statements share under different segment
/// proofs keeps neither classification: it is demoted to the class of its own register, which is
/// exact at every statement.
/// <para>
/// Every access printed through a location class provably carries that segment value at its
/// statement, so the class's constant <c>Segment</c> is the register value on every execution that
/// reaches the print.
/// </para>
/// </summary>
internal sealed class GlobalAccessClassification {
    private const int ParagraphSize = 16;

    private readonly Dictionary<SegmentedPointerNode, Target> _targets =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SegmentedPointerNode, TableTarget> _tableTargets =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ushort, TableClassInfo> _tableLocationByValue = new();
    private readonly Dictionary<SegmentRegisterIndex, TableClassInfo> _tableRegisterClass = new();
    private readonly Dictionary<ushort, GlobalClassInfo> _locationByValue = new();
    private readonly Dictionary<SegmentRegisterIndex, GlobalClassInfo> _registerClass = new();
    private readonly Dictionary<ConstantMemoryAccess, HashSet<GlobalClassInfo>> _classesByAccess = new();
    private readonly ProgramImages _images;
    private readonly HashSet<ushort> _immediateSetValues = [];

    private GlobalAccessClassification(ProgramImages images) {
        _images = images;
    }

    /// <summary>One emitted globals class: a register class (named after the segment register) or a location class.</summary>
    internal sealed class GlobalClassInfo {
        public required string Name { get; init; }
        /// <summary>The register a register class indexes through; <see langword="null"/> for a
        /// location class.</summary>
        public SegmentRegisterIndex? Register { get; init; }
        /// <summary>The proven segment value of a location class, observed.</summary>
        public ushort? SegmentValue { get; init; }
        /// <summary>The symbol-map value of a location class: image-relative inside a DOS-loaded
        /// image, absolute otherwise.</summary>
        public DataSegmentValue LocationValue { get; init; }
        /// <summary>Whether <see cref="SegmentValue"/> is a constant the code assigns to a segment
        /// register from an immediate: such code is not relocatable.</summary>
        public bool SetFromImmediate { get; set; }
        /// <summary>The (offset, width) properties the class emits, sorted by offset then width.</summary>
        public SortedDictionary<(ushort Offset, int Width), GlobalClassProperty> Properties { get; } = new();
    }

    /// <summary>One property of a globals class and the property key its name resolves under.</summary>
    internal sealed record GlobalClassProperty(ushort Offset, int Width, GlobalPropertyKey Key);

    /// <summary>What one pointer node prints: the class and the key its property resolves under.</summary>
    internal sealed record Target(GlobalClassInfo Class, GlobalPropertyKey Key);

    /// <summary>One emitted table class: a register class (<c>DsTables</c>, ...) or a location
    /// class (<c>Segment2000Tables</c>, <c>Image0010Tables</c>, ...).</summary>
    internal sealed class TableClassInfo {
        public required string Name { get; init; }
        /// <summary>The register a register class indexes through; <see langword="null"/> for a
        /// location class.</summary>
        public SegmentRegisterIndex? Register { get; init; }
        /// <summary>The proven segment value of a location class, observed.</summary>
        public ushort? SegmentValue { get; init; }
        /// <summary>The symbol-map value of a location class: image-relative inside a DOS-loaded
        /// image, absolute otherwise.</summary>
        public DataSegmentValue LocationValue { get; init; }
        /// <summary>The (base, element width) tables the class emits, sorted by base then width.</summary>
        public SortedDictionary<(ushort Base, int Width), TableClassProperty> Properties { get; } = new();
    }

    /// <summary>One table of a table class: its base, its element width and the documented length,
    /// the largest index the program's accesses reach plus one.</summary>
    internal sealed class TableClassProperty(ushort tableBase, int width) {
        public ushort Base { get; } = tableBase;
        public int Width { get; } = width;
        /// <summary>One more than the largest index bound an access to this table proved.</summary>
        public int Length { get; internal set; } = 1;
    }

    /// <summary>What one table pointer node prints: the class, the table property and the index
    /// register inside its offset.</summary>
    internal sealed record TableTarget(TableClassInfo Class, TableClassProperty Property,
        RegisterNode Index);

    /// <summary>Every emitted globals class: the register classes in class order first, then the
    /// location classes sorted by value.</summary>
    public IReadOnlyList<GlobalClassInfo> Classes { get; private set; } = [];

    /// <summary>Every emitted table class, in the same order as <see cref="Classes"/>.</summary>
    public IReadOnlyList<TableClassInfo> TableClasses { get; private set; } = [];

    /// <summary>The table-shaped pointer nodes skipped because their statement's instruction
    /// carries a shape tag (self-modifying or otherwise rewritten code).</summary>
    internal int TableShapeTagMisses { get; private set; }

    /// <summary>The table-shaped pointer nodes skipped because the index range was unknown,
    /// empty or reached past the segment end.</summary>
    internal int TableRangeMisses { get; private set; }

    /// <summary>The pointer node instances seen at two statements under different segment proofs,
    /// demoted to the class of their own register.</summary>
    internal int ConflictDemotions { get; private set; }

    /// <summary>The location classes whose segment value the code assigns from an immediate.</summary>
    public int ImmediateSetLocationClassCount =>
        Classes.Count(classInfo => classInfo.SegmentValue is not null && classInfo.SetFromImmediate);

    /// <summary>The class and key <paramref name="pointer"/> prints through; false when the
    /// classification did not see this node instance (a pointer rebuilt after the methods were
    /// prepared: the printer then falls back to the register class).</summary>
    public bool TryTarget(SegmentedPointerNode pointer, [NotNullWhen(true)] out Target? target) =>
        _targets.TryGetValue(pointer, out target);

    /// <summary>The class, property and index <paramref name="pointer"/> prints through; false
    /// when it is not a table access.</summary>
    public bool TryTableTarget(SegmentedPointerNode pointer, [NotNullWhen(true)] out TableTarget? target) =>
        _tableTargets.TryGetValue(pointer, out target);

    /// <summary>The classes the accesses <paramref name="access"/> was classified into (empty when
    /// the program does not access it).</summary>
    public IReadOnlySet<GlobalClassInfo> ClassesOf(ConstantMemoryAccess access) =>
        _classesByAccess.TryGetValue(access, out HashSet<GlobalClassInfo>? classes) ? classes : [];

    /// <summary>Classifies every global and table access of <paramref name="methods"/> against
    /// their statement's resolved segment values and the index ranges of one
    /// <see cref="IrValueRanges"/> per method.</summary>
    public static GlobalAccessClassification Build(
        IEnumerable<(IrMethod Method, IrSegmentValues Values, IrSsa Ssa)> methods,
        ProgramImages images) {
        GlobalAccessClassification classification = new(images);
        List<(IrMethod Method, IrSegmentValues Values, IrSsa Ssa)> prepared = [.. methods];
        foreach ((IrMethod method, _, _) in prepared) {
            foreach (IrBlock block in method.Blocks) {
                foreach (IrStatement statement in block.Statements) {
                    classification.CollectImmediateValue(statement);
                }
            }
        }
        foreach ((IrMethod method, IrSegmentValues values, IrSsa ssa) in prepared) {
            IrValueRanges ranges = IrValueRanges.Compute(method, ssa);
            foreach (IrBlock block in method.Blocks) {
                for (int index = 0; index < block.Statements.Count; index++) {
                    IrStatement statement = block.Statements[index];
                    AccessContext context = new(method, block, index, ranges, statement.Origin,
                        register => values.ValueBefore(block, index, register),
                        (first, second) => values.SameValueBefore(block, index, first, second));
                    foreach (IVisitableAstNode descendant in IrAstChildren.Descendants(statement.Node)) {
                        classification.ClassifyPointer(descendant, context);
                    }
                }
                AccessContext terminatorContext = new(method, block, block.Statements.Count, ranges,
                    block.Terminator.Origin,
                    register => values.ValueAt(block.Terminator, register),
                    (first, second) => values.SameValue(block.Terminator, first, second));
                foreach (IVisitableAstNode root in IrAstChildren.RootsOf(block.Terminator)) {
                    foreach (IVisitableAstNode descendant in IrAstChildren.Descendants(root)) {
                        classification.ClassifyPointer(descendant, terminatorContext);
                    }
                }
            }
        }
        classification.OrderClasses();
        return classification;
    }

    /// <summary>Where an access sits: its method, block and statement index (the count for a
    /// terminator), the value ranges, the instruction origin for the shape tag, and the segment
    /// value lookups of its position.</summary>
    private sealed record AccessContext(IrMethod Method, IrBlock Block, int Index,
        IrValueRanges Ranges, ICfgNode Origin, Func<IrSsaValue, SegmentValue> ValueAt,
        Func<IrSsaValue, IrSsaValue, bool> SameValue);

    private void CollectImmediateValue(IrStatement statement) {
        if (statement.Node is BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Left: SegmentRegisterNode,
                Right: ValueNode right }
            && IrSegmentValueResolver.ConstantWordOf(right) is { } value) {
            _immediateSetValues.Add(value);
        }
    }

    private void ClassifyPointer(IVisitableAstNode node, AccessContext context) {
        if (node is not SegmentedPointerNode pointer) {
            return;
        }
        if (!IrGlobalAccesses.TryAccess(pointer, out ConstantMemoryAccess access)) {
            ClassifyTable(pointer, context);
            return;
        }
        IrSsaValue register = ToSsaValue(access.Segment);
        GlobalClassInfo classInfo;
        GlobalPropertyKey key;
        if (context.ValueAt(register) is SegmentValue.Known known) {
            classInfo = LocationClass(known.Value);
            key = new LocationGlobalPropertyKey(classInfo.LocationValue, access.Offset, access.Width);
        } else {
            SegmentRegisterIndex classRegister = IrGlobalAccesses.RegistersInClassOrder
                .FirstOrDefault(candidate => candidate == access.Segment
                    || context.SameValue(ToSsaValue(candidate), register), access.Segment);
            classInfo = RegisterClass(classRegister);
            key = new RegisterGlobalPropertyKey(classRegister, access.Offset, access.Width);
        }
        if (_targets.TryGetValue(pointer, out Target? seen)) {
            if (seen.Equals(new Target(classInfo, key))) {
                return;
            }
            ConflictDemotions++;
            classInfo = RegisterClass(access.Segment);
            key = new RegisterGlobalPropertyKey(access.Segment, access.Offset, access.Width);
        }
        if (!classInfo.Properties.ContainsKey((access.Offset, access.Width))) {
            classInfo.Properties[(access.Offset, access.Width)] =
                new GlobalClassProperty(access.Offset, access.Width, key);
        }
        if (!_classesByAccess.TryGetValue(access, out HashSet<GlobalClassInfo>? classes)) {
            classes = _classesByAccess[access] = [];
        }
        classes.Add(classInfo);
        _targets[pointer] = new Target(classInfo, key);
    }

    /// <summary>Whether <paramref name="pointer"/> is a table access whose index range keeps its
    /// last element inside the segment, and records the class and property it prints through.</summary>
    private void ClassifyTable(SegmentedPointerNode pointer, AccessContext context) {
        if (pointer.Segment is not SegmentRegisterNode segment
            || segment.RegisterIndex == (int)SegmentRegisterIndex.SsIndex
            || !IrGlobalAccesses.TryTableAccess(pointer,
                out (RegisterNode Index, ushort Base) access)) {
            return;
        }
        if (context.Method.Shapes.TryGetValue(context.Origin, out IrInstructionShape shape)
            && shape != IrInstructionShape.None) {
            TableShapeTagMisses++;
            return;
        }
        int width = (int)pointer.DataType.BitWidth / 8;
        IrValueRange range = context.Ranges.RangeAt(access.Index, context.Block, context.Index);
        if (range.IsEmpty || range.Max > 0x7FFF
            || (ulong)access.Base + (ulong)width * range.Max + (ulong)width - 1 > 0xFFFF) {
            TableRangeMisses++;
            return;
        }
        IrSsaValue register = ToSsaValue((SegmentRegisterIndex)segment.RegisterIndex);
        TableClassInfo classInfo;
        if (context.ValueAt(register) is SegmentValue.Known known) {
            classInfo = LocationTableClass(known.Value);
        } else {
            SegmentRegisterIndex accessRegister = (SegmentRegisterIndex)segment.RegisterIndex;
            SegmentRegisterIndex classRegister = IrGlobalAccesses.RegistersInClassOrder
                .FirstOrDefault(candidate => candidate == accessRegister
                    || context.SameValue(ToSsaValue(candidate), register), accessRegister);
            classInfo = RegisterTableClass(classRegister);
        }
        if (_tableTargets.TryGetValue(pointer, out TableTarget? seen)
            && (seen.Class != classInfo || seen.Property.Base != access.Base
                || seen.Property.Width != width)) {
            ConflictDemotions++;
            classInfo = RegisterTableClass((SegmentRegisterIndex)segment.RegisterIndex);
        }
        if (!classInfo.Properties.TryGetValue((access.Base, width),
                out TableClassProperty? property)) {
            property = classInfo.Properties[(access.Base, width)] =
                new TableClassProperty(access.Base, width);
        }
        property.Length = Math.Max(property.Length, checked((int)range.Max + 1));
        _tableTargets[pointer] = new TableTarget(classInfo, property, access.Index);
    }

    private GlobalClassInfo RegisterClass(SegmentRegisterIndex register) {
        if (_registerClass.TryGetValue(register, out GlobalClassInfo? classInfo)) {
            return classInfo;
        }
        return _registerClass[register] = new GlobalClassInfo {
            Name = IrGlobalAccesses.ClassName(register),
            Register = register
        };
    }

    private GlobalClassInfo LocationClass(ushort value) {
        if (_locationByValue.TryGetValue(value, out GlobalClassInfo? classInfo)) {
            return classInfo;
        }
        (string name, DataSegmentValue symbol) = LocationName(value, "Globals");
        return _locationByValue[value] = new GlobalClassInfo {
            Name = name,
            SegmentValue = value,
            LocationValue = symbol
        };
    }

    private TableClassInfo RegisterTableClass(SegmentRegisterIndex register) {
        if (_tableRegisterClass.TryGetValue(register, out TableClassInfo? classInfo)) {
            return classInfo;
        }
        return _tableRegisterClass[register] = new TableClassInfo {
            Name = IrGlobalAccesses.TableClassName(register),
            Register = register
        };
    }

    private TableClassInfo LocationTableClass(ushort value) {
        if (_tableLocationByValue.TryGetValue(value, out TableClassInfo? classInfo)) {
            return classInfo;
        }
        (string name, DataSegmentValue symbol) = LocationName(value, "Tables");
        return _tableLocationByValue[value] = new TableClassInfo {
            Name = name,
            SegmentValue = value,
            LocationValue = symbol
        };
    }

    /// <summary>The location class name of a value: image-relative inside a DOS-loaded image (the
    /// same test as the segment fields) when the value comes from the loader, absolute otherwise -
    /// a value the code assigns to a segment register from an immediate prints as a literal and is
    /// not relocatable, so it keeps the absolute <c>SegmentXXXX</c> class even inside an image.</summary>
    private (string Name, DataSegmentValue Symbol) LocationName(ushort value, string suffix) {
        if (!_immediateSetValues.Contains(value)
            && _images.Images.Any(image => InsideImage(value, image))) {
            int difference = value - _images.ConfiguredEntrySegment;
            string name =
                difference < 0 ? $"ImageMinus{-difference:X4}{suffix}" : $"Image{difference:X4}{suffix}";
            return (name, new DataSegmentValue(true, difference));
        }
        return ($"Segment{value:X4}{suffix}", new DataSegmentValue(false, value));
    }

    private static bool InsideImage(ushort segment, LoadedImage image) {
        uint imageEnd = (image.StartAddress + image.SizeInBytes + ParagraphSize - 1) / ParagraphSize;
        return segment >= image.FirstSegment && segment < imageEnd;
    }

    private void OrderClasses() {
        foreach (GlobalClassInfo classInfo in _locationByValue.Values) {
            classInfo.SetFromImmediate = classInfo.SegmentValue is { } segmentValue && _immediateSetValues.Contains(segmentValue);
        }
        Classes = IrGlobalAccesses.RegistersInClassOrder
            .Where(_registerClass.ContainsKey)
            .Select(register => _registerClass[register])
            .Concat(_locationByValue.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            .ToList();
        TableClasses = IrGlobalAccesses.RegistersInClassOrder
            .Where(_tableRegisterClass.ContainsKey)
            .Select(register => _tableRegisterClass[register])
            .Concat(_tableLocationByValue.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            .ToList();
    }

    private static IrSsaValue ToSsaValue(SegmentRegisterIndex register) =>
        (IrSsaValue)((int)IrSsaValue.Es + (int)register);
}
