namespace Spice86.Tests;

using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// Readability metrics for a single generated-code fixture, computed from the Roslyn syntax tree and the
/// compilation diagnostics. Used by the code-generator quality harness to track how later phases change the
/// generated output. Each row is written to <c>metrics.txt</c> and checked by
/// <see cref="GeneratedCodeMetricsAssertions"/> on every compile.
/// </summary>
internal sealed record GeneratedCodeMetrics {
    public required string Fixture { get; init; }
    public string SourceLocation { get; init; } = "";
    public required int Methods { get; init; }
    public required int Lines { get; init; }
    public required int Labels { get; init; }
    public required int GotoStatements { get; init; }
    /// <summary>Distinct goto target identifiers summed per method (not file-wide), since the same label text can appear in multiple methods.</summary>
    public required int DistinctGotoTargets { get; init; }
    public required int CheckExternalEvents { get; init; }
    public required int VerifySpeculativeEntryOrFail { get; init; }
    public required int FailAsUntested { get; init; }
    public required int UncheckedExpressions { get; init; }
    public required int Casts { get; init; }
    public required int TryStatements { get; init; }
    /// <summary>Invocations of any method on <c>Alu8</c>, <c>Alu16</c> or <c>Alu32</c>.</summary>
    public required int AluCalls { get; init; }
    /// <summary>Reads of the six status flags (C, P, A, Z, S, O); assignments to them are not counted.</summary>
    public required int StatusFlagReads { get; init; }
    /// <summary><c>do ... while</c> statements.</summary>
    public required int DoLoops { get; init; }
    /// <summary><c>while (true)</c> statements; other <c>while</c> statements are not counted.</summary>
    public required int WhileTrueLoops { get; init; }
    /// <summary><c>for</c> statements (the counted-loop form of a promoted counter local).</summary>
    public required int ForLoops { get; init; }
    /// <summary><c>if</c> statements; an <c>else if</c> counts as a second one.</summary>
    public required int IfStatements { get; init; }
    /// <summary><c>break</c> statements, in loops and in <c>switch</c> sections.</summary>
    public required int BreakStatements { get; init; }
    /// <summary><c>continue</c> statements.</summary>
    public required int ContinueStatements { get; init; }
    /// <summary>Invocations of <c>Stack.Push16/Push32/Pop16/Pop32</c>.</summary>
    public required int StackCalls { get; init; }
    /// <summary>Assignments whose target is one of the six status flags.</summary>
    public required int FlagAssignments { get; init; }
    /// <summary>Local declarations directly in a method body block (the <c>flagOperand_</c> temporaries).</summary>
    public required int MethodVariables { get; init; }
    /// <summary><c>+=</c>, <c>-=</c>, <c>&amp;=</c>, <c>|=</c>, <c>^=</c>, <c>&lt;&lt;=</c>, <c>&gt;&gt;=</c> assignments.</summary>
    public required int CompoundAssignments { get; init; }
    /// <summary>Two-element tuple assignments that exchange the same two symbols.</summary>
    public required int TupleSwaps { get; init; }
    /// <summary>Element accesses on <c>UInt8</c>, <c>UInt16</c>, <c>UInt32</c>, <c>Int8</c>, <c>Int16</c> or <c>Int32</c> (the memory indexers), plus member accesses on a globals class (<c>DsGlobals.Word0004</c>, ...).</summary>
    public required int MemoryAccesses { get; init; }
    /// <summary>How many of <see cref="MemoryAccesses"/> are globals property accesses.</summary>
    public required int GlobalAccesses { get; init; }
    /// <summary>How many of <see cref="GlobalAccesses"/> print through a location class
    /// (<c>Segment2000Globals.Word0030</c>, <c>Image0010Globals.Byte0004</c>, ...), plus the element
    /// accesses on the location table classes.</summary>
    public required int LocationAccesses { get; init; }
    /// <summary>How many of <see cref="MemoryAccesses"/> are table element accesses
    /// (<c>DsTables.Word0200[SI]</c>, <c>Segment2000Tables.Byte0120[BX]</c>, ...).</summary>
    public required int TableAccesses { get; init; }
    /// <summary>Comment lines whose trimmed text starts with <c>// DOS </c> (the data comments printed at DOS calls).</summary>
    public required int DataComments { get; init; }
    /// <summary>Invocations of <c>InterruptCall</c> (untyped <c>int</c> sites).</summary>
    public required int InterruptCalls { get; init; }
    /// <summary>Invocations of <c>InterruptCallReturnedElsewhere</c>.</summary>
    public required int InterruptCallReturnedElsewhere { get; init; }
    /// <summary>Calls to service implementations, grouped by their handler receiver.</summary>
    public required IReadOnlyDictionary<string, int> ServiceCallsByReceiver { get; init; }
    /// <summary>Total service calls, excluding DOS caller-stack bookkeeping.</summary>
    public int TypedServiceCalls => ServiceCallsByReceiver.Values.Sum();
    /// <summary>Service calls containing a validated string literal argument.</summary>
    public required int StringServiceCalls { get; init; }
    /// <summary>Local declarations whose name is a register local (<c>ushort ax;</c>, <c>byte al;</c>,
    /// <c>ushort ds;</c>, <c>ushort ax1 = 0;</c>): the promoted register webs.</summary>
    public required int RegisterLocals { get; init; }
    /// <summary>How many of <see cref="RegisterLocals"/> are numbered (<c>ax1</c>): the webs spanning
    /// blocks or crossing a preserving call.</summary>
    public required int RegisterLocalsMulti { get; init; }
    /// <summary>How many of <see cref="RegisterLocals"/> declare a signed type (<c>short ax;</c>,
    /// <c>sbyte al;</c>, <c>int eax;</c>): the webs whose reads vote signed.</summary>
    public required int SignedLocals { get; init; }
    /// <summary>Simple assignments of a local value, optionally cast, to a CPU register.</summary>
    public required int WriteBacks { get; init; }
    public required IReadOnlyDictionary<string, int> DeviceCallsByReceiver { get; init; }
    public required IReadOnlyDictionary<string, int> ServiceCallsByOperation { get; init; }
    public required IReadOnlyDictionary<string, int> DeviceCallsByOperation { get; init; }
    public required int PortWrapperCalls { get; init; }
    public required int RawIoCalls { get; init; }
    public required int RegisterReads { get; init; }
    public required int RegisterWrites { get; init; }
    public required int PlainRegisterAssignments { get; init; }
    public required int LocalVariables { get; init; }
    public required int LocalReads { get; init; }
    public required int LocalWrites { get; init; }
    public required int BookkeepingCalls { get; init; }
    public required int Statements { get; init; }
    public int DeviceCalls => DeviceCallsByReceiver.Values.Sum();
    /// <summary>How many of <see cref="MemoryAccesses"/> are frame property accesses (<c>frame.Arg0</c>, ...).</summary>
    public required int FrameAccesses { get; init; }
    /// <summary>Declarations of a save local (<c>ushort savedAx_0012 = 0;</c>): the
    /// <c>push R</c> ... <c>pop R</c> pairs printed without the stack.</summary>
    public required int SavedPairs { get; init; }
    public required int Warnings { get; init; }
    public required ImmutableArray<string> WarningIds { get; init; }

    // Every value is written as name=value, so a row can be read without counting columns. Fields are padded to fixed
    // widths so rows stay aligned regardless of the order fixtures are appended in; the last field (warning ids) is
    // left unpadded because it is free text of varying length.
    private const int FixtureFieldWidth = 56;
    private const int FieldWidth = 15;

    /// <summary>The metrics as one line of <c>name=value</c> fields, in a fixed order.</summary>
    public string ToLine() {
        string warningIds = WarningIds.IsDefaultOrEmpty ? "" : string.Join(",", WarningIds);
        (string Name, string Value)[] fields = [
            ("fixture", Fixture),
            ("methods", Methods.ToString()),
            ("lines", Lines.ToString()),
            ("labels", Labels.ToString()),
            ("gotos", GotoStatements.ToString()),
            ("gotoTgts", DistinctGotoTargets.ToString()),
            ("extEvents", CheckExternalEvents.ToString()),
            ("specGuard", VerifySpeculativeEntryOrFail.ToString()),
            ("untested", FailAsUntested.ToString()),
            ("unchecked", UncheckedExpressions.ToString()),
            ("casts", Casts.ToString()),
            ("tries", TryStatements.ToString()),
            ("aluCalls", AluCalls.ToString()),
            ("flagReads", StatusFlagReads.ToString()),
            ("doLoops", DoLoops.ToString()),
            ("whileTrue", WhileTrueLoops.ToString()),
            ("forLoops", ForLoops.ToString()),
            ("ifs", IfStatements.ToString()),
            ("breaks", BreakStatements.ToString()),
            ("continues", ContinueStatements.ToString()),
            ("stackCalls", StackCalls.ToString()),
            ("flagAssign", FlagAssignments.ToString()),
            ("methodVars", MethodVariables.ToString()),
            ("compound", CompoundAssignments.ToString()),
            ("tupleSwaps", TupleSwaps.ToString()),
            ("memAccess", MemoryAccesses.ToString()),
            ("globals", GlobalAccesses.ToString()),
            ("locations", LocationAccesses.ToString()),
            ("tables", TableAccesses.ToString()),
            ("dataCmts", DataComments.ToString()),
            ("intCalls", InterruptCalls.ToString()),
            ("intRetElse", InterruptCallReturnedElsewhere.ToString()),
            ("typedCalls", TypedServiceCalls.ToString()),
            ("strCalls", StringServiceCalls.ToString()),
            ("regLocals", RegisterLocals.ToString()),
            ("regLocalsMulti", RegisterLocalsMulti.ToString()),
            ("signedLocals", SignedLocals.ToString()),
            ("writeBacks", WriteBacks.ToString()),
            ("portWrapperCalls", PortWrapperCalls.ToString()),
            ("rawIoCalls", RawIoCalls.ToString()),
            ("registerReads", RegisterReads.ToString()),
            ("registerWrites", RegisterWrites.ToString()),
            ("plainRegisterAssignments", PlainRegisterAssignments.ToString()),
            ("localVariables", LocalVariables.ToString()),
            ("localReads", LocalReads.ToString()),
            ("localWrites", LocalWrites.ToString()),
            ("bookkeepingCalls", BookkeepingCalls.ToString()),
            ("statements", Statements.ToString()),
            ("frames", FrameAccesses.ToString()),
            ("savedPairs", SavedPairs.ToString()),
            ("warnings", Warnings.ToString()),
            ("warningIds", warningIds)
        ];
        IEnumerable<string> counts = ServiceCallsByReceiver.Select(pair => ($"serviceCalls.{pair.Key}", pair.Value))
            .Concat(DeviceCallsByReceiver.Select(pair => ($"deviceCalls.{pair.Key}", pair.Value)))
            .Concat(ServiceCallsByOperation.Select(pair => ($"serviceOperations.{pair.Key}", pair.Value)))
            .Concat(DeviceCallsByOperation.Select(pair => ($"deviceOperations.{pair.Key}", pair.Value)))
            .OrderBy(pair => pair.Item1, StringComparer.Ordinal)
            .Select(pair => $"{pair.Item1}={pair.Value}");
        return string.Concat(fields.Select((field, index) => Pad($"{field.Name}={field.Value}", WidthOf(index, fields.Length))))
            + " " + string.Join(" ", counts);
    }

    private static int WidthOf(int index, int count) {
        if (index == 0) {
            return FixtureFieldWidth;
        }
        if (index == count - 1) {
            return 0;
        }
        return FieldWidth;
    }

    private static string Pad(string value, int width) {
        if (width == 0) {
            return value;
        }
        // One trailing space guarantees a separator even when a value is wider than its column.
        return value.PadRight(width - 1) + " ";
    }
}
