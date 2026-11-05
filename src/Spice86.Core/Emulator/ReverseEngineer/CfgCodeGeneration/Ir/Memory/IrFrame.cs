namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>
/// The standard frame recognized at a method entry: <c>push bp; mov bp,sp; [sub sp,n]</c> at the
/// top of the entry block, and the recognized teardown at every return exit of the method.
/// </summary>
/// <param name="Entry">The entry the frame belongs to.</param>
/// <param name="PushBp">The <c>push bp</c> statement, at entry delta 0.</param>
/// <param name="SetBp">The <c>bp = sp</c> statement, at entry delta -2.</param>
/// <param name="LocalBytes">The bytes <c>sub sp,n</c> right after <see cref="SetBp"/> reserves for
/// the locals, 0 without one.</param>
/// <param name="Epilogues">One entry per return exit of the method.</param>
/// <param name="ReturnAddressBytes">2 when every return exit is a near <c>ret</c>, 4 for
/// <c>retf</c>, 6 for <c>iret</c>; null when they disagree or there is none.</param>
/// <param name="PoppedArgumentBytes">The <c>ret n</c> / <c>retf n</c> operand when every return
/// exit agrees (0 for a plain <c>ret</c>), else null.</param>
internal sealed record IrFrame(IrEntry Entry, IrStatement PushBp, IrStatement SetBp,
    int LocalBytes, IReadOnlyList<IrFrameEpilogue> Epilogues, int? ReturnAddressBytes,
    int? PoppedArgumentBytes);
