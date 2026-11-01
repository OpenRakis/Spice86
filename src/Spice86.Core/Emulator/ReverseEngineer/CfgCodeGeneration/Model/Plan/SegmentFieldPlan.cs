namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

/// <summary>
/// One generated <c>ushort</c> field per distinct code segment the program was observed at:
/// the observed <see cref="Segment"/>, the <see cref="FieldName"/> the generated class declares,
/// and the <see cref="Initializer"/> expression the constructor assigns to it. Segments inside any
/// DOS-loaded image (a program started by the batch, an EXEC child, an overlay) get an initializer
/// relative to the <c>programStartSegment</c> constructor parameter so the generated class still
/// resolves them when the programs are loaded elsewhere; the BIOS and video BIOS segments get
/// named fields; any other segment keeps its observed value.
/// </summary>
internal sealed record SegmentFieldPlan(ushort Segment, string FieldName, string Initializer) {
    private const ushort BiosSegment = 0xF000;
    private const ushort VideoBiosSegment = 0xC000;
    private const int ParagraphSize = 16;

    /// <summary>
    /// Assigns roles to the distinct code segments: named fields for the BIOS and video BIOS
    /// segments, image-relative initializers when <paramref name="images"/> places a segment inside
    /// a DOS-loaded image, observed-value fields otherwise. The unnamed fields are numbered
    /// <c>cs1</c>, <c>cs2</c>, ... in segment order, leaving no gap where a named field sits.
    /// </summary>
    public static IReadOnlyList<SegmentFieldPlan> BuildAll(IEnumerable<ushort> segments, ProgramImages images) {
        List<SegmentFieldPlan> plans = [];
        int csIndex = 0;
        foreach (ushort segment in segments.Distinct().OrderBy(segment => segment)) {
            plans.Add(ForSegment(segment, images, ref csIndex));
        }
        return plans;
    }

    private static SegmentFieldPlan ForSegment(ushort segment, ProgramImages images, ref int csIndex) {
        if (segment == BiosSegment) {
            return new SegmentFieldPlan(segment, "biosSegment", "0xF000");
        }
        if (segment == VideoBiosSegment) {
            return new SegmentFieldPlan(segment, "videoBiosSegment", "0xC000");
        }
        csIndex++;
        string fieldName = $"cs{csIndex}";
        if (!IsInsideImage(segment, images)) {
            return new SegmentFieldPlan(segment, fieldName, $"0x{segment:X4}");
        }
        return new SegmentFieldPlan(segment, fieldName, RelativeInitializer(segment, images.ConfiguredEntrySegment));
    }

    private static bool IsInsideImage(ushort segment, ProgramImages images) {
        return images.Images.Any(image => IsInside(segment, image));
    }

    private static bool IsInside(ushort segment, LoadedImage image) {
        uint imageEnd = (image.StartAddress + image.SizeInBytes + ParagraphSize - 1) / ParagraphSize;
        return segment >= image.FirstSegment && segment < imageEnd;
    }

    private static string RelativeInitializer(ushort segment, ushort configuredEntrySegment) {
        int difference = segment - configuredEntrySegment;
        if (difference == 0) {
            return "programStartSegment";
        }
        if (difference > 0) {
            return $"(ushort)(programStartSegment + 0x{difference:X4})";
        }
        return $"(ushort)(programStartSegment - 0x{-difference:X4})";
    }
}
