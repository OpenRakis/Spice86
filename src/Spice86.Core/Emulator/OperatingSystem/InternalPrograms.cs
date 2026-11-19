namespace Spice86.Core.Emulator.OperatingSystem;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Collections.Generic;

/// <summary>
/// Registry of the managed internal programs, following the DOSBox Staging architecture: every
/// registered program is a small COM stub generated on the Z memory drive. The stub resizes the
/// child's memory block, crosses into C# through one shared FE38 callback and hands control to the
/// program's <see cref="InternalProgram"/> implementation, so no program binary is embedded.
/// </summary>
internal sealed class InternalPrograms {
    /// <summary>
    /// PSP offset (PSP size 0x100 plus the 19 stub code bytes) where the stub stores its program index.
    /// </summary>
    private const ushort StubIndexOffset = 0x113;

    /// <summary>
    /// Return address offset (PSP size 0x100 plus the 14 stub bytes up to the callback) just after the
    /// FE38 callback. A child launched by an internal program resumes its parent at this offset.
    /// </summary>
    public const ushort StubReturnOffset = 0x10E;

    private readonly DosDriveManager _driveManager;
    private readonly IMemory _memory;
    private readonly DosSwappableDataArea _sda;
    private readonly ILogger _loggerService;
    private readonly List<Func<InternalProgram>> _creators = new();

    /// <summary>
    /// Initializes a new instance and registers the shared internal program callback.
    /// </summary>
    /// <param name="callbackHandler">Dispatches the shared callback to the running program.</param>
    /// <param name="driveManager">Resolves the Z memory drive the stubs are stored on.</param>
    /// <param name="memory">Reads the program index from the current child PSP.</param>
    /// <param name="sda">Provides the current child PSP segment.</param>
    /// <param name="loggerService">The logger service.</param>
    internal InternalPrograms(CallbackHandler callbackHandler, DosDriveManager driveManager,
        IMemory memory, DosSwappableDataArea sda, ILogger loggerService) {
        _driveManager = driveManager;
        _memory = memory;
        _sda = sda;
        _loggerService = loggerService;
        SegmentedAddress representativeStubAddress = new(DosProcessManager.CommandComSegment, 0x100);
        callbackHandler.AddCallback(new Callback(CallbackNumbers.InternalProgram, RunCurrentProgram, representativeStubAddress));
    }

    /// <summary>
    /// Registers an internal program under its DOS name on the Z drive.
    /// </summary>
    /// <param name="name">DOS file name of the program (e.g. "COMMAND.COM").</param>
    /// <param name="creator">Creates the program when its stub crosses into C#.</param>
    internal void Register(string name, Func<InternalProgram> creator) {
        byte index = (byte)_creators.Count;
        _creators.Add(creator);
        if (!_driveManager.TryGetMemoryDrive('Z', out MemoryDrive? zDrive)) {
            _loggerService.LogWarning("No Z memory drive to store the internal program '{Name}'", name);
            return;
        }
        zDrive.AddFile(name, BuildStub(index));
    }

    /// <summary>
    /// Shared callback of every internal program stub: reads the program index from the child PSP and
    /// runs that program.
    /// </summary>
    private void RunCurrentProgram() {
        ushort pspSegment = _sda.CurrentProgramSegmentPrefix;
        byte index = _memory[MemoryUtils.ToPhysicalAddress(pspSegment, StubIndexOffset)];
        if (index >= _creators.Count) {
            throw new UnrecoverableException(
                $"Internal program index {index} is out of range: {_creators.Count} program(s) registered");
        }
        _creators[index]().Run();
    }

    /// <summary>
    /// Builds the DOSBox Staging style stub: shrink the stack and resize the memory block to 0x40
    /// paragraphs, cross into C#, then terminate the child process with exit code 0.
    /// </summary>
    /// <param name="index">Index of the program in the registry.</param>
    /// <returns>The stub bytes.</returns>
    private static byte[] BuildStub(byte index) {
        ushort callbackNumber = CallbackNumbers.InternalProgram;
        byte[] stub =
        [
            0xBC, 0x00, 0x04,
            0xBB, 0x40, 0x00,
            0xB4, 0x4A,
            0xCD, 0x21,
            0xFE, 0x38, (byte)(callbackNumber & 0xFF), (byte)(callbackNumber >> 8),
            0xB8, 0x00, 0x4C,
            0xCD, 0x21,
            index,
        ];
        return stub;
    }
}
