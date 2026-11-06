using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// Provides DOS applications with EMS memory via INT 67h. <br/>
/// Expanded memory is memory beyond DOS's 640K-byte limit. This implementation
/// supports 8 MB of expanded memory (512 pages of 16 KB each). <br/>
/// Because the 8086, 8088, and 80286 (in real mode) microprocessors can <br/>
/// physically address only 1M byte of memory, they access expanded memory <br/>
/// through a 64 KB window (page frame) in their physical address range.
/// <para>
/// This implementation is based on the LIM EMS 4.0 specification with the following scope:
/// <list type="bullet">
/// <item>Core EMS 3.2 functions (0x40-0x4E): Get Status, Page Frame, Allocate/Deallocate, Map/Unmap, etc.</item>
/// <item>Selected EMS 4.0 functions: 0x50 (Map Multiple), 0x51 (Reallocate), 0x53 (Handle Names), 0x58 (Mappable Array), 0x59 (Hardware Info)</item>
/// <item>VCPI (Virtual Control Program Interface) is not currently implemented</item>
/// <item>GEMMIS and other LIM 4.0 OS-specific functions (0x5D-0x5F) are not currently implemented due to limited development resources</item>
/// </list>
/// </para>
/// <para>
/// The EMS implementation uses its own RAM separate from conventional memory, consistent with
/// how real EMS hardware works. This is correct behavior for a real-mode emulator.
/// </para>
/// </summary>
/// <remarks>
/// This is a LIM standard implementation where EMM pages and raw pages are identical (both 16 KB).
/// The page frame is located at segment 0xE000, which is above 640K in the Upper Memory Area.
/// </remarks>
public sealed class ExpandedMemoryManager : InterruptHandler, IVirtualDevice {
    /// <summary>
    /// The string identifier in main memory for the EMS Handler. <br/>
    /// DOS programs can detect the presence of an EMS handler by looking for it <br/>
    /// (this is one, of two, methods to do so).
    /// </summary>
    public const string EmsIdentifier = "EMMXXXX0";

    /// <summary>
    /// The Emm Page Frame is 64 KB of EMM Memory, available at 0xE000 (segmented address).
    /// </summary>
    public const uint EmmPageFrameSize = 64 * 1024;

    /// <summary>
    /// Expands to 4.
    /// </summary>
    public const byte EmmMaxPhysicalPages = (byte)(EmmPageFrameSize / EmmPageSize);

    /// <summary>
    /// Value used when a page is unmapped or unallocated.
    /// </summary>
    public const ushort EmmNullPage = 0xFFFF;

    /// <summary>
    /// Value used when an EMM handle is unmapped un unallocated.
    /// </summary>
    public const ushort EmmNullHandle = 0xFFFF;

    /// <summary>
    /// Maximum number of EMM handles as per LIM EMS specification.
    /// Handle IDs range from 0 to 254 (255 total, excluding the system handle 0).
    /// </summary>
    public const int EmmMaxHandles = 255;

    /// <summary>
    /// The start address of the Emm Page Frame, as a segment value. <br/>
    /// The page frame is located above 640K bytes.  Normally, only video <br/>
    /// adapters, network cards, and similar devices exist between 640K and 1024K.
    /// </summary>
    public const ushort EmmPageFrameSegment = 0xE000;

    /// <summary>
    /// The size of an EMM logical or physical page: 16 KB.
    /// </summary>
    public const ushort EmmPageSize = 16384;

    /// <summary>
    /// This is required so EMS can be detected by DOS applications. <br/>
    /// (this is one, of two, methods to do so).
    /// </summary>
    public const ushort DosDeviceSegment = 0xF100;

    /// <inheritdoc />
    public override byte VectorNumber => 0x67;

    /// <summary>
    /// Because the 8086, 8088, and 80286 (in real mode) microprocessors can
    /// physically address only 1M byte of memory, they access expanded memory
    /// through a window in their physical address range. <br/>
    /// This is referred as the Emm Page Frame.
    /// </summary>
    public IDictionary<ushort, EmmRegister> EmmPageFrame { get; init; } = new Dictionary<ushort, EmmRegister>();

    /// <summary>
    /// Stores the saved page mappings for each physical page. <br/>
    /// Save Page Map stores which EmmPage is mapped to each physical page. <br/>
    /// Restore Page Map restores these mappings.
    /// </summary>
    private readonly IDictionary<ushort, EmmPage> _savedPageMappings = new Dictionary<ushort, EmmPage>();

    /// <summary>
    /// The EMM handles given to the DOS programs. An EMM Handle has one or more unique logical pages.
    /// </summary>
    public IDictionary<int, EmmHandle> EmmHandles { get; } = new Dictionary<int, EmmHandle>();

    public uint DeviceNumber { get; set; } = 0;

    public DosDeviceHeader Header { get; init; }

    public ushort Information => 0xc0c0; //Lifted from DOSBox.

    public string Name {
        get {
            return EmsIdentifier;
        }
        set => throw new InvalidOperationException("Cannot rename DOS device!");
    }

    private readonly EMMCharacterDevice _emmCharacterDevice;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public ExpandedMemoryManager(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, ILogger loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        Header = new DosDeviceHeader(memory, new SegmentedAddress(DosDeviceSegment, 0x0).Linear) {
            Name = EmsIdentifier,
            Attributes = DeviceAttributes.Ioctl | DeviceAttributes.Character,
            StrategyEntryPoint = 0,
            InterruptEntryPoint = 0,
            NextDevicePointer = new SegmentedAddress(0xFFFF, 0xFFFF)
        };

        // Allocation of system handle 0.
        AllocatePages(4);

        for (ushort i = 0; i < EmmMaxPhysicalPages; i++) {
            uint startAddress = MemoryUtils.ToPhysicalAddress(EmmPageFrameSegment, (ushort)(EmmPageSize * i));
            EmmRegister emmRegister = new(new EmmPage(EmmPageSize), startAddress);
            EmmPageFrame.Add(i, emmRegister);
            Memory.RegisterMapping(startAddress, EmmPageSize, emmRegister);
        }

        // A character device wrapper so EMS can be opened via DOSINT21H OpenFile function
        // fixes Wolf3D not finding Expanded Memory.
        _emmCharacterDevice = new EMMCharacterDevice(memory, DosDeviceSegment, Header, this);
    }

    private sealed class EMMCharacterDevice : CharacterDevice {
        private readonly ExpandedMemoryManager _ems;

        public EMMCharacterDevice(IMemory memory, ushort segment, DosDeviceHeader header, ExpandedMemoryManager ems)
            : base(memory, new SegmentedAddress(segment, 0).Linear, EmsIdentifier,
                   DeviceAttributes.Ioctl | DeviceAttributes.Character) {
            _ems = ems;
            Header = header;
        }

        public override ushort Information => _ems.Information;
        public override string Name {
            get => _ems.Name;
            set => throw new InvalidOperationException("Cannot rename DOS device!");
        }

        // Stream overrides: EMS is not a readable/writable stream
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override void Write(byte[] buffer, int offset, int count) { }
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Flush() { }
        public override bool CanRead => false;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => 0;
        public override long Position { get; set; }

        // IOCTL and status delegation
        public override byte GetStatus(bool inputFlag) => _ems.GetStatus(inputFlag);
        public override bool TryReadFromControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode)
            => _ems.TryReadFromControlChannel(address, size, out returnCode);
        public override bool TryWriteToControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode)
            => _ems.TryWriteToControlChannel(address, size, out returnCode);
    }

    /// <inheritdoc />
    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // We are going to install the handler elsewhere.
        // Let's make a backup so that other interrupt handlers don't get installed in our space.
        SegmentedAddress beginningAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.CurrentAddress = new SegmentedAddress(DosDeviceSegment, 0);
        // Install the handler normally, at our address
        SegmentedAddress handlerAddress = base.WriteAssemblyInRam(memoryAsmWriter);
        // Restore the address
        memoryAsmWriter.CurrentAddress = beginningAddress;
        return handlerAddress;
    }


    public CharacterDevice AsCharacterDevice() => _emmCharacterDevice;

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("EMS function: 0x{@Function:X2} AL=0x{Al:X2}", operation, State.AL);
        }

        if (!HasRunnable(operation)) {
            if (LoggerService.IsEnabled(LogLevel.Error)) {
                LoggerService.LogError("EMS function not provided: {@Function}", operation);
            }
            State.AH = EmmStatus.EmmFunctionNotSupported;
            return;
        }
        Run(operation);
    }

    /// <summary>Returns whether the expanded memory manager is working correctly in AH.
    /// </summary>
    public byte GetStatus() {
        if (LoggerService.IsEnabled(LogLevel.Debug)) {
            LoggerService.LogDebug("EMS: {@MethodName}: {@Result}", nameof(GetStatus), EmmStatus.EmmNoError);
        }
        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// Returns in _state.AH whether the expanded memory manager is working correctly.
    /// </summary>
    [InterruptService(0x40, Name = nameof(GetStatus), Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Ah, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteStatus() {
        State.AH = GetStatus();
    }

    /// <summary>Returns the EMM page frame segment in BX; status in AH.</summary>
    public (byte ah, ushort bx) GetPageFrameSegment() {
        if (LoggerService.IsEnabled(LogLevel.Debug)) {
            LoggerService.LogDebug("EMS: {@MethodName}: 0x{Result:X4}", nameof(GetPageFrameSegment), EmmPageFrameSegment);
        }
        return (EmmStatus.EmmNoError, EmmPageFrameSegment);
    }

    /// <summary>
    /// Returns in _state.BX where the 64KB EMM Page Frame is located.
    /// </summary>
    [InterruptService(0x41, Name = nameof(GetPageFrameSegment), Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Ah | CpuValues.Bx, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WritePageFrameSegment() {
        (State.AH, State.BX) = GetPageFrameSegment();
    }

    /// <summary>Returns the total number of pages in DX and the number of unallocated
    /// pages in BX; status in AH.</summary>
    public (byte ah, ushort dx, ushort bx) GetUnallocatedPageCount() {
        ushort totalPages = EmmMemory.TotalPages;
        ushort freePages = GetFreePageCount();
        if (LoggerService.IsEnabled(LogLevel.Debug)) {
            LoggerService.LogDebug("{MethodName}: Total pages: {TotalPages} Free pages: {FreePages}", nameof(GetUnallocatedPageCount), totalPages, freePages);
        }
        return (EmmStatus.EmmNoError, totalPages, freePages);
    }

    /// <summary>
    /// The Get Unallocated Page Count function returns in _state.BX the number of
    /// unallocated pages (pages available to your program),
    /// and the total number of pages in expanded memory, in _state.DX.
    /// </summary>
    [InterruptService(0x42, Name = nameof(GetUnallocatedPageCount), Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Ah | CpuValues.Bx | CpuValues.Dx, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteUnallocatedPageCount() {
        (State.AH, State.DX, State.BX) = GetUnallocatedPageCount();
    }

    /// <summary>
    /// Gets the number of pages available.
    /// </summary>
    /// <returns>The number of pages available.</returns>
    public ushort GetFreePageCount() {
        return (ushort)Math.Max(0, EmmMemory.TotalPages - EmmHandles.Sum(static x => x.Value.LogicalPages.Count));
    }

    /// <summary>Allocates <paramref name="pageCount"/> pages and returns the new handle
    /// in DX; status in AH. A failed allocation preserves DX.</summary>
    public (byte ah, ushort? dx) AllocatePageHandle(ushort pageCount) {
        if (pageCount is 0) {
            return (EmmStatus.EmmTriedToAllocateZeroPages, null);
        }
        if (EmmHandles.Count >= EmmMaxHandles) {
            return (EmmStatus.EmmOutOfHandles, null);
        }
        if (pageCount > EmmMemory.TotalPages) {
            return (EmmStatus.EmmNotEnoughPages, null);
        }
        return (EmmStatus.EmmNoError, (ushort)AllocatePages(pageCount, (EmmHandle?)null).HandleNumber);
    }

    /// <summary>
    /// The Allocate Pages function allocates the number of pages requested
    /// and assigns a unique EMM handle to these pages.  The EMM handle owns
    /// these pages until the DOS application deallocates them. <br/>
    /// It returns an unique EMM handle in _state.BX, &gt; 0 and &lt; 256. <br/>
    /// Parameters: <br/>
    /// _state.DX: The number of pages to allocate to the handle.
    /// </summary>
    [InterruptService(0x43, Name = nameof(AllocatePages), Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx, Writes = CpuValues.Ah | CpuValues.Dx, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteAllocatedPages() {
        (State.AH, ushort? handle) = AllocatePageHandle(State.BX);
        if (handle is { } handleValue) {
            State.DX = handleValue;
        }
    }

    /// <summary>
    /// Allocates logical pages to a handle.
    /// </summary>
    /// <param name="numberOfPagesToAlloc">The number of pages to allocate</param>
    /// <param name="existingHandle">Used to reallocate logical pages to an existing handle. Optional.</param>
    /// <returns>The modified <see cref="EmmHandle"/> instance.</returns>
    public EmmHandle AllocatePages(ushort numberOfPagesToAlloc, EmmHandle? existingHandle = null) {
        int key;
        if (existingHandle != null) {
            key = existingHandle.HandleNumber;
        } else {
            // Find the next available handle ID (don't just use Count as it can collide after deallocation)
            key = 0;
            while (key < EmmMaxHandles && EmmHandles.ContainsKey(key)) {
                key++;
            }
            if (key >= EmmMaxHandles) {
                throw new InvalidOperationException($"Maximum number of EMM handles ({EmmMaxHandles}) reached");
            }
        }
        existingHandle ??= new() {
            HandleNumber = (ushort)key
        };
        while (numberOfPagesToAlloc > 0) {
            ushort pageNumber = (ushort)existingHandle.LogicalPages.Count;
            existingHandle.LogicalPages.Add(new EmmPage(EmmPageSize) {
                PageNumber = pageNumber
            });
            numberOfPagesToAlloc--;
        }

        if (!EmmHandles.TryAdd(key, existingHandle)) {
            EmmHandles[key] = existingHandle;
        }
        return existingHandle;
    }

    /// <summary>
    /// The Map/Unmap Handle Page function maps a logical page at a specific
    /// physical page anywhere in the mappable regions of system memory. <br/>
    /// The lowest valued physical page numbers are associated with regions of
    /// memory outside the conventional memory range.  Use Function 25 (Get Mappable Physical Address Array)
    /// to determine which physical pages within a system are mappable and determine the segment addresses which
    /// correspond to a specific physical page number.  Function 25 provides a
    /// cross reference between physical page numbers and segment addresses. <br/>
    /// This function can also unmap physical pages, making them inaccessible for reading or writing. <br/>
    /// You unmap a physical page by setting its associated logical page to FFFFh.
    /// </summary>
    /// <remarks>
    /// The handle determines what type of pages are being mapped. <br/>
    /// Logical pages allocated by Function 4 and Function 27 (Allocate Standard Pages
    /// subFunction) are referred to as pages and are 16K bytes long. <br/>
    /// Logical pages allocated by Function 27 (Allocate Raw Pages subFunction) are
    /// referred to as raw pages and might not be the same size as logical pages.
    /// </remarks>
    [InterruptService(0x44, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Dx, Writes = CpuValues.Ah, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void MapUnmapHandlePage() {
        State.AH = MapUnmapHandlePage(State.BX, State.AL, State.DX);
    }

    /// <summary>Maps or unmaps logical page <paramref name="logicalPageNumber"/> of
    /// <paramref name="handleId"/> at physical page <paramref name="physicalPageNumber"/>;
    /// returns the status.</summary>
    /// <returns>The status code.</returns>
    public byte MapUnmapHandlePage(ushort logicalPageNumber, ushort physicalPageNumber, ushort handleId) {
        if (physicalPageNumber >= EmmMaxPhysicalPages) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("Physical page {PhysicalPage} out of range",
                    physicalPageNumber);
            }
            return EmmStatus.EmmIllegalPhysicalPage;
        }

        if (!IsValidHandle(handleId)) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("Invalid Handle {InvalidHandle}", handleId);
            }
            return EmmStatus.EmmInvalidHandle;
        }

        EmmRegister emmRegister = EmmPageFrame[physicalPageNumber];

        // Unmapping
        if (logicalPageNumber == EmmNullPage) {
            if (LoggerService.IsEnabled(LogLevel.Debug)) {
                LoggerService.LogDebug("Unmapped physical page: {PhysicalPage}",
                    emmRegister.PhysicalPage.PageNumber);
            }
            emmRegister.PhysicalPage.PageNumber = EmmNullPage;
            return EmmStatus.EmmNoError;
        }

        // Mapping
        EmmHandle allocatedEmmHandle = EmmHandles[handleId];
        if (logicalPageNumber > allocatedEmmHandle.LogicalPages.Count - 1) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("Logical page {LogicalPage} out of range",
                    logicalPageNumber);
            }
            return EmmStatus.EmmLogicalPageOutOfRange;
        }
        emmRegister.PhysicalPage = allocatedEmmHandle.LogicalPages[logicalPageNumber];
        if (LoggerService.IsEnabled(LogLevel.Debug)) {
            LoggerService.LogDebug("Mapped logical page {LogicalPage} to physical page {PhysicalPage}",
                logicalPageNumber,
                physicalPageNumber);
        }

        return EmmStatus.EmmNoError;
    }

    private bool IsValidHandle(ushort handleId) => EmmHandles.ContainsKey(handleId);

    /// <summary>
    /// Maps a memory segment to a logical page.
    /// </summary>
    /// <param name="logicalPage">The logical page number</param>
    /// <param name="segment">The memory segment</param>
    /// <param name="handleId">The EMM Handle Id</param>
    public void MapSegment(ushort logicalPage, ushort segment, ushort handleId) {
        uint address = MemoryUtils.ToPhysicalAddress(segment, 0);
        uint pageFrameAddress = MemoryUtils.ToPhysicalAddress(EmmPageFrameSegment, 0);
        uint pageFrameAddressEnd = MemoryUtils.ToPhysicalAddress(EmmPageFrameSegment, EmmPageSize);
        if (address >= pageFrameAddress && address < pageFrameAddressEnd) {
            MapUnmapHandlePage(logicalPage, (ushort)((address - pageFrameAddress) / EmmPageSize), handleId);
        }
    }

    /// <summary>
    /// Deallocate Pages deallocates the logical pages currently allocated to
    /// an EMM handle.  Only after the application deallocates these pages can
    /// other applications use them.  When a handle is deallocated, its name is
    /// set to all ASCII nulls (binary zeros).
    /// </summary>
    /// <remarks>
    /// A program must perform this function before it exits to DOS. If it
    /// doesn't, no other programs can use these pages or the EMM handle.
    /// <summary>Deallocates the logical pages of <paramref name="handleId"/>; status in
    /// AH.</summary>
    public byte DeallocatePages(ushort handleId) {
        if (!IsValidHandle(handleId)) {
            return EmmStatus.EmmInvalidHandle;
        }
        if (EmmHandles[handleId].SavedPageMap) {
            return EmmStatus.EmmSaveMapError;
        }
        EmmHandles.Remove(handleId);
        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// Deallocate Pages deallocates the logical pages currently allocated to
    /// an EMM handle.  Only after the application deallocates these pages can
    /// other applications use them.  When a handle is deallocated, its name is
    /// set to all ASCII nulls (binary zeros).
    /// </summary>
    /// <remarks>
    /// A program must perform this function before it exits to DOS. If it
    /// doesn't, no other programs can use these pages or the EMM handle.
    /// This means that a program using expanded memory should trap critical
    /// errors and control-break if there is a chance that the program will
    /// have allocated pages when either of these events occur.
    /// </remarks>
    [InterruptService(0x45, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void DeallocatePages() {
        State.AH = DeallocatePages(State.DX);
    }

    /// <summary>Returns the EMS version in AX (AH=status, AL=version in BCD).
    /// </summary>
    public ushort GetEmmVersion() {
        // Return EMS version 3.2 in BCD format.
        // Note: We implement some EMS 4.0 functions but report 3.2 for broader compatibility.
        if (LoggerService.IsEnabled(LogLevel.Debug)) {
            LoggerService.LogDebug("EMS: {@MethodName}: 0x{Version:X2}", nameof(GetEmmVersion), 0x32);
        }
        return (ushort)((EmmStatus.EmmNoError << 8) | 0x32);
    }

    /// <summary>
    /// Returns the EMS version in _state.AL using BCD format. <br/>
    /// This implementation returns version 3.2 (0x32) for compatibility,
    /// even though it supports selected EMS 4.0 functions (0x50, 0x51, 0x53, 0x58, 0x59).
    /// Programs that need to use EMS 4.0 functions should check for specific
    /// function support rather than relying solely on the version number.
    /// </summary>
    [InterruptService(0x46, Name = nameof(GetEmmVersion), Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Ax, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteEmmVersion() {
        State.AX = GetEmmVersion();
    }

    /// <summary>
    /// Save Page Map saves the contents of the page mapping registers on all
    /// expanded memory boards in an internal save area.  The function is
    /// typically used to save the memory mapping context of the EMM handle
    /// that was active when a software or hardware interrupt occurred.  (See
    /// Function 9, Restore Page Map, for the restore operation.) <br/>
    /// If you're writing a resident program, an interrupt service, or a device driver
    /// that uses expanded memory, you must save the state of the mapping
    /// hardware.  You must save this state because application software using
    /// expanded memory may be running when your program is invoked by a
    /// hardware interrupt, a software interrupt, or DOS. <br/>
    /// The Save Page Map function requires the EMM handle that was assigned
    /// to your resident program, interrupt service routine, or device driver
    /// at the time it was initialized. This is not the EMM handle that the
    /// application software was using when your software interrupted it. <br/>
    /// The Save Page Map function saves the state of the map registers for
    /// only the 64K-byte page frame defined in versions 3.x of this
    /// specification.  Since all applications written to LIM versions 3.x
    /// require saving the map register state of only this 64K-byte page
    /// frame, saving the entire mapping state for a large number of mappable
    /// pages would be inefficient use of memory. <br/>
    /// </summary>
    [InterruptService(0x47, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SavePageMap() {
        State.AH = SavePageMap(State.DX);
    }

    /// <summary>
    /// Saves the page map to the <see cref="_savedPageMappings"/> dictionary.
    /// </summary>
    /// <param name="handleId">The Id of the EMM handle to be saved.</param>
    /// <returns>The status code.</returns>
    public byte SavePageMap(ushort handleId) {
        if (!IsValidHandle(handleId)) {
            if (handleId != 0) {
                return EmmStatus.EmmInvalidHandle;
            }
        }

        if (EmmHandles[handleId].SavedPageMap) {
            return EmmStatus.EmmPageMapSaved;
        }

        _savedPageMappings.Clear();

        // Save which EmmPage is mapped to each physical page
        foreach (KeyValuePair<ushort, EmmRegister> item in EmmPageFrame) {
            _savedPageMappings.Add(item.Key, item.Value.PhysicalPage);
        }

        EmmHandles[handleId].SavedPageMap = true;
        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// The Restore Page Map function restores the page mapping register
    /// contents on the expanded memory boards for a particular EMM handle. <br/>
    /// This function lets your program restore the contents of the mapping
    /// registers its EMM handle saved. (See Function 8, Save Page Map for the
    /// save operation.) <br/>
    /// If you're writing a resident program, an interrupt service routine, or
    /// a device driver that uses expanded memory, you must restore the
    /// mapping hardware to the state it was in before your program took over. <br/>
    /// You must save this state because application software using expanded
    /// memory might have been running when your program was invoked. <br/>
    /// The Restore Page Map function requires the EMM handle that was
    /// assigned to your resident program, interrupt service routine, or
    /// device driver at the time it was initialized.  This is not the EMM
    /// handle that the application software was using when your software interrupted it. <br/>
    /// The Restore Page Map function restores the state of the map registers
    /// for only the 64K-byte page frame defined in versions 3.x of this
    /// specification.  <br/>
    /// Since all applications written to LIM versions 3.x require restoring the map
    /// register state of only this 64K-byte page frame, restoring the entire mapping state
    /// for a large number of mappable pages would be inefficient use of memory.
    /// </summary>
    [InterruptService(0x48, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void RestorePageMap() {
        State.AH = RestorePageMap(State.DX);
    }

    /// <summary>
    /// Restores the page map from the <see cref="_savedPageMappings"/> dictionary.
    /// </summary>
    /// <param name="handleId">The Id of the EMM handle to restore.</param>
    /// <returns>The status code.</returns>
    public byte RestorePageMap(ushort handleId) {
        if (!IsValidHandle(handleId)) {
            if (handleId != 0) {
                return EmmStatus.EmmInvalidHandle;
            }
        }

        if (!EmmHandles[handleId].SavedPageMap) {
            return EmmStatus.EmmPageNotSavedFirst;
        }

        // Restore the EmmPage mappings to each physical page register
        foreach (KeyValuePair<ushort, EmmPage> item in _savedPageMappings) {
            if (EmmPageFrame.TryGetValue(item.Key, out EmmRegister? register)) {
                register.PhysicalPage = item.Value;
            }
        }

        EmmHandles[handleId].SavedPageMap = false;
        return EmmStatus.EmmNoError;
    }

    /// <summary>Returns the number of open EMM handles in BX; status in AH.</summary>
    /// <remarks>This number will not exceed 255.</remarks>
    public (byte ah, ushort bx) GetEmmHandleCount() {
        return (EmmStatus.EmmNoError, (ushort)EmmHandles.Count);
    }

    /// <summary>
    /// Returns in _state.BX the number of open EMM handles.
    /// </summary>
    [InterruptService(0x4B, Name = nameof(GetEmmHandleCount), Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Ah | CpuValues.Bx, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteEmmHandleCount() {
        (State.AH, State.BX) = GetEmmHandleCount();
    }

    /// <summary>Returns the number of logical pages of <paramref name="handleId"/> in BX;
    /// status in AH.</summary>
    /// <remarks>
    /// _state.BX contains the number of logical pages allocated to the
    /// specified EMM handle.  This number never exceeds 512
    /// because the memory manager allows a maximum of 512 pages
    /// (8 MB) of expanded memory.
    /// </remarks>
    public (byte ah, ushort bx) GetHandlePages(ushort handleId) {
        return (EmmStatus.EmmNoError, (ushort)EmmHandles[handleId].LogicalPages.Count);
    }

    /// <summary>
    /// The Get Handle Pages function returns in _state.BX, the number of pages allocated to
    /// a specific EMM handle. <br/>
    /// Params: <br/>
    /// _state.DX: The EMM Handle.
    /// </summary>
    [InterruptService(0x4C, Name = nameof(GetHandlePages), Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ax | CpuValues.Bx, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteHandlePages() {
        (byte ah, State.BX) = GetHandlePages(State.DX);
        State.AX = ah;
    }

    /// <summary>Writes the open-handle/page-count table at
    /// <paramref name="tableAddress"/>; returns the status in AH and the allocated page
    /// count in BX.</summary>
    public (byte ah, ushort bx) GetAllHandlePages(uint tableAddress) {
        ushort count = GetAllocatedHandlePagesCount();
        foreach (KeyValuePair<int, EmmHandle> allocatedHandle in EmmHandles) {
            Memory.UInt16[tableAddress] = allocatedHandle.Value.HandleNumber;
            tableAddress += 2;
            Memory.UInt16[tableAddress] = (ushort)allocatedHandle.Value.LogicalPages.Count;
            tableAddress += 2;
        }
        return (EmmStatus.EmmNoError, count);
    }

    /// <summary>
    /// The Get All Handle Pages function returns an array of the open EMM
    /// handles and the number of pages allocated to each one.
    /// </summary>
    [InterruptService(0x4D, Name = nameof(GetAllHandlePages), Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Es | CpuValues.Di, Writes = CpuValues.Ax | CpuValues.Bx, Memory = ServiceMemoryEffect.Write, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteAllHandlePages() {
        (byte ah, State.BX) = GetAllHandlePages(MemoryUtils.ToPhysicalAddress(State.ES, State.DI));
        State.AX = ah;
    }

    /// <summary>Maps or unmaps <paramref name="numberOfPages"/> pages of
    /// <paramref name="handleId"/> per the map at <paramref name="mapAddress"/>;
    /// <paramref name="operation"/> selects physical-page or segmented-address mode.
    /// Returns the status.</summary>
    public byte MapUnmapMultipleHandlePages(byte operation, ushort numberOfPages,
        ushort handleId, uint mapAddress) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace(
                "EMS: {@MethodName} Map {@NumberOfPages} pages from handle {@Handle} according to the map at address 0x{@MapAddress:X6}",
                nameof(MapUnmapMultipleHandlePages), numberOfPages, handleId, mapAddress);
        }

        switch (operation) {
            case EmmSubFunctionsCodes.UsePhysicalPageNumbers:
                for (int i = 0; i < numberOfPages; i++) {
                    ushort logicalPage = Memory.UInt16[mapAddress];
                    mapAddress += 2;
                    ushort physicalPage = Memory.UInt16[mapAddress];
                    mapAddress += 2;
                    MapUnmapHandlePage(logicalPage, physicalPage, handleId);
                }
                return EmmStatus.EmmNoError;
            case EmmSubFunctionsCodes.UseSegmentedAddress:
                for (int i = 0; i < numberOfPages; i++) {
                    ushort logicalPage = Memory.UInt16[mapAddress];
                    mapAddress += 2;
                    ushort segment = Memory.UInt16[mapAddress];
                    mapAddress += 2;
                    MapSegment(logicalPage, segment, handleId);
                }
                return EmmStatus.EmmNoError;
            default:
                if (LoggerService.IsEnabled(LogLevel.Error)) {
                    LoggerService.LogError(
                        "{@MethodName} subFunction number {@SubFunctionId} not supported",
                        nameof(MapUnmapMultipleHandlePages), operation);
                }
                return EmmStatus.EmmInvalidSubFunction;
        }
    }

    /// <summary>
    /// This function can, in a single invocation, map (or unmap) logical
    /// pages into as many physical pages as the system supports. <br/>
    /// Consequently, it has less execution overhead than mapping pages one at
    /// a time.  For applications which do a lot of page mapping, this is the
    /// preferred mapping method.
    /// </summary>
    [InterruptService(0x50, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Cx | CpuValues.Dx | CpuValues.Ds | CpuValues.Si, Writes = CpuValues.Ah, Memory = ServiceMemoryEffect.Read, ExecutionPath = ServiceExecutionPath.Direct)]
    public void MapUnmapMultipleHandlePages() {
        State.AH = MapUnmapMultipleHandlePages(State.AL, State.CX, State.DX,
            MemoryUtils.ToPhysicalAddress(State.DS, State.SI));
    }

    /// <summary>Resizes <paramref name="handleId"/> to
    /// <paramref name="reallocationCount"/> pages; returns the status.</summary>
    public byte ReallocatePages(ushort handleId, ushort reallocationCount) {
        if (!IsValidHandle(handleId)) {
            return EmmStatus.EmmInvalidHandle;
        }

        if (EmmHandles.Count == EmmMemory.TotalPages) {
            return EmmStatus.EmmOutOfHandles;
        }
        if (reallocationCount > EmmMemory.TotalPages) {
            return EmmStatus.EmmNotEnoughPages;
        }

        EmmHandle handle = EmmHandles[handleId];

        if (handle.LogicalPages.Count == reallocationCount) {
            return EmmStatus.EmmNoError;
        }

        if (reallocationCount == 0) {
            handle.LogicalPages.Clear();
        }

        if (reallocationCount > handle.LogicalPages.Count) {
            ushort addedPages = (ushort)(reallocationCount - handle.LogicalPages.Count);
            AllocatePages(addedPages, handle);
        }

        if (reallocationCount < handle.LogicalPages.Count) {
            ushort removedPages = (ushort)(handle.LogicalPages.Count - reallocationCount);
            while (removedPages > 0) {
                if (handle.LogicalPages.Any()) {
                    handle.LogicalPages.Remove(handle.LogicalPages.Last());
                }
                removedPages--;
            }
        }

        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// This function allows an application program to increase or decrease
    /// (reallocate) the number of logical pages allocated to an EMM handle.
    /// </summary>
    [InterruptService(0x51, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Dx, Writes = CpuValues.Ah, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ReallocatePages() {
        State.AH = ReallocatePages(State.DX, State.BX);
    }

    /// <summary>
    /// Gets or Set a handle name, depending on the <paramref name="operation"/>
    /// </summary>
    /// <param name="handleId">The handle reference</param>
    /// <param name="operation">Get: 0, Set: 1</param>
    /// <param name="nameAddress">The linear address of the 8-byte name buffer.</param>
    /// <returns>The state of the operation</returns>
    public byte GetSetHandleName(ushort handleId, byte operation, uint nameAddress) {
        if (!IsValidHandle(handleId)) {
            return EmmStatus.EmmInvalidHandle;
        }
        switch (operation) {
            case EmmSubFunctionsCodes.HandleNameGet:
                GetHandleName(handleId, nameAddress);
                break;

            case EmmSubFunctionsCodes.HandleNameSet:
                SetHandleName(handleId,
                    Memory.GetZeroTerminatedString(nameAddress, 8));
                break;

            default:
                if (LoggerService.IsEnabled(LogLevel.Error)) {
                    LoggerService.LogError("{@MethodName}: subFunction {@FunctionId} invalid", nameof(GetSetHandleName), operation);
                }
                return EmmStatus.EmmInvalidSubFunction;
        }
        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// This subFunction gets the eight character name currently assigned to a handle.
    /// There is no restriction on the characters which may be used
    /// in the handle name (that is, anything from 00h through FFh). <br/>
    /// The handle name is initialized to ASCII nulls (binary zeros) three
    /// times: when the memory manager is installed, when a handle is
    /// allocated, and when a handle is deallocated. <br/>
    /// A handle with a name which is all ASCII nulls, by definition, has no name. <br/>
    /// When a handle is assigned a name, at least one character in the name must be a non-null character,
    /// in order to distinguish it from a handle without a name.
    /// </summary>
    [InterruptService(0x53, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx | CpuValues.Es | CpuValues.Di | CpuValues.Si, Writes = CpuValues.Ah, Memory = ServiceMemoryEffect.ReadWrite, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetSetHandleName() {
        uint nameAddress = State.AL == EmmSubFunctionsCodes.HandleNameSet
            ? MemoryUtils.ToPhysicalAddress(State.SI, State.DI)
            : MemoryUtils.ToPhysicalAddress(State.ES, State.DI);
        State.AH = GetSetHandleName(State.DX, State.AL, nameAddress);
    }

    /// <summary>Writes the mappable physical address array to
    /// <paramref name="bufferAddress"/>; returns the status in AH and the entry count in
    /// CX.</summary>
    public (byte ah, ushort cx) GetMappablePhysicalAddressArray(uint bufferAddress) {
        for (ushort i = 0; i < EmmPageFrame.Count; i++) {
            ushort segment = (ushort)(EmmPageFrameSegment + (i * EmmPageSize / 16));
            Memory.UInt16[bufferAddress] = segment;
            bufferAddress += 2;
            Memory.UInt16[bufferAddress] = i;
            bufferAddress += 2;
        }
        return (EmmStatus.EmmNoError, EmmMaxPhysicalPages);
    }

    /// <summary>
    /// Function 25 (58h): Get Mappable Physical Address Array.
    /// <para>
    /// Returns an array containing the segment address and physical page number for each mappable  <br/>
    /// physical page in the system. The contents of this array provide a cross reference between  <br/>
    /// physical page numbers and the actual segment addresses for each mappable page. <br/>
    /// </para>
    /// <para>
    /// Input: <br/>
    ///   - ES:DI points to an application-supplied memory area where the physical address array will be stored
    /// </para>
    /// <para>
    /// Output: <br/>
    ///   - Array entries are written to ES:DI in pairs (segment, page number) <br/>
    ///   - CX = number of entries in the array <br/>
    ///   - AH = status code (00h = success) <br/>
    /// </para>
    /// <para>
    /// Each entry in the array contains two values: <br/>
    ///   - A segment address of the mappable physical page <br/>
    ///   - The corresponding physical page number
    /// </para>
    /// </summary>
    [InterruptService(0x58, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Es | CpuValues.Di, Writes = CpuValues.Ah | CpuValues.Cx, Memory = ServiceMemoryEffect.Write, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetMappablePhysicalAddressArray() {
        (State.AH, State.CX) = GetMappablePhysicalAddressArray(
            MemoryUtils.ToPhysicalAddress(State.ES, State.DI));
    }

    /// <summary>Expanded memory hardware information; <paramref name="operation"/> (AL)
    /// selects the sub-function. The hardware configuration array is written at
    /// <paramref name="address"/>; only the unallocated-raw-pages sub-function returns
    /// register outputs.</summary>
    public (byte? ah, ushort? dx, ushort? bx) ReadExpandedMemoryHardwareInformation(
        byte operation, uint address) {
        switch (operation) {
            case EmmSubFunctionsCodes.GetHardwareConfigurationArray:
                uint data = address;
                // 1 page is 1K paragraphs (16KB)
                Memory.UInt16[data] = 0x0400;
                data += 2;
                // No alternate register sets
                Memory.UInt16[data] = 0x0000;
                data += 2;
                // Context save area size in bytes (following FreeDOS formula: (physicalPages + 1) * 4)
                // This represents the bytes needed to save the mapping context for all physical pages
                Memory.UInt16[data] = (ushort)((EmmMaxPhysicalPages + 1) * 4);
                data += 2;
                // No DMA channels
                Memory.UInt16[data] = 0x0000;
                data += 2;
                // Always 0 for LIM standard
                Memory.UInt16[data] = 0x0000;
                return (null, null, null);
            case EmmSubFunctionsCodes.GetUnallocatedRawPages:
                return (EmmStatus.EmmNoError, EmmMemory.TotalPages, GetFreePageCount());
            default:
                if (LoggerService.IsEnabled(LogLevel.Error)) {
                    LoggerService.LogError("{@MethodName}: EMS subfunction number {@SubFunction} not implemented",
                        nameof(ReadExpandedMemoryHardwareInformation), operation);
                }
                return (null, null, null);
        }
    }

    /// <summary>
    /// This function is for use by operating systems only.  This function can
    /// be disabled at any time by the operating system. <br/>
    /// Refer to Function 30 for a description of how an operating system does this.
    /// </summary>
    [InterruptService(0x59, Name = nameof(GetExpandedMemoryHardwareInformation), Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Es | CpuValues.Di, Writes = CpuValues.Ah | CpuValues.Bx | CpuValues.Dx, Memory = ServiceMemoryEffect.Write, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetExpandedMemoryHardwareInformation() {
        (byte? ah, ushort? dx, ushort? bx) =
            ReadExpandedMemoryHardwareInformation(State.AL, MemoryUtils.ToPhysicalAddress(State.ES, State.DI));
        if (ah is { } status) {
            State.AH = status;
        }
        if (dx is { } dxValue) {
            State.DX = dxValue;
        }
        if (bx is { } bxValue) {
            State.BX = bxValue;
        }
    }

    /// <summary>
    /// Returns the number of open EMM handles
    /// </summary>
    /// <returns>The number of open EMM handles</returns>
    public ushort GetAllocatedHandlePagesCount() => (ushort)EmmHandles.Sum(static x => x.Value.LogicalPages.Count);

    /// <summary>
    /// Gets the name of a handle.
    /// </summary>
    public string GetHandleName(ushort handle, uint address) {
        EmmHandles[handle].Name = Memory.GetZeroTerminatedString(address, 8);
        return EmmHandles[handle].Name;
    }

    /// <summary>
    /// Set the name of a handle.
    /// </summary>
    public void SetHandleName(ushort handle, string name) {
        EmmHandles[handle].Name = name;
    }

    public byte GetStatus(bool inputFlag) {
        return 0;
    }

    public bool TryReadFromControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }

    public bool TryWriteToControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }
}
