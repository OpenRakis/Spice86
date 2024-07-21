namespace Spice86.Core.Emulator.ReverseEngineer;

using System.Linq;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;
using Timer = Spice86.Core.Emulator.Devices.Timer.Timer;

/// <summary>
/// Provides a set of properties and methods to facilitate the creation of C# overrides of machine code.
/// </summary>
public class CSharpOverrideHelper {
    /// <summary>
    /// The CPU registers and flags.
    /// </summary>
    protected readonly State _state;
    /// <summary>
    /// The CPU stack.
    /// </summary>
    protected readonly Stack _stack;
    /// <summary>
    /// The emulated CPU.
    /// </summary>
    protected readonly Cpu _cpu;
    /// <summary>
    /// The memory bus of the IBM PC.
    /// </summary>
    protected readonly IMemory _memory;
    /// <summary>
    /// The two programmable interrupt controllers.
    /// </summary>
    protected readonly DualPic _dualPic;
    /// <summary>
    /// The execution flow recorder.
    /// </summary>
    protected readonly ExecutionFlowRecorder _executionFlowRecorder;
    /// <summary>
    /// The class that stores callback instructions definitions.
    /// </summary>
    protected readonly CallbackHandler _callbackHandler;
    /// <summary>
    /// The IBM PC timer device.
    /// </summary>
    protected readonly Timer _timer;
    /// <summary>
    /// The class that manages software breakpoints.
    /// </summary>
    protected readonly MachineBreakpoints _machineBreakpoints;
    /// <summary>
    /// The service used for logging.
    /// </summary>
    protected readonly ILoggerService _loggerService;

    /// <summary>
    /// The Spice86 configuration
    /// </summary>
    protected Configuration Configuration { get; }

    /// <summary>
    /// The emulated CPU.
    /// </summary>
    public Cpu Cpu => _cpu;

    /// <summary>
    /// The emulator machine.
    /// </summary>
    public Machine Machine { get; }

    /// <summary>
    /// The memory bus of the IBM PC.
    /// </summary>
    public IMemory Memory => _memory;

    /// <summary>
    /// Gets the 8-bit indexer of the memory bus.
    /// </summary>
    public UInt8Indexer UInt8 => Memory.UInt8;

    /// <summary>
    /// Gets the 16-bit indexer of the memory bus.
    /// </summary>
    public UInt16Indexer UInt16 => Memory.UInt16;

    /// <summary>
    /// Gets the 32-bit indexer of the memory bus.
    /// </summary>
    public UInt32Indexer UInt32 => Memory.UInt32;

    /// <summary>
    /// Gets the stack of the CPU.
    /// </summary>
    public Stack Stack => _stack;

    /// <summary>
    /// Gets the state of the CPU.
    /// </summary>
    public State State => _state;

    /// <summary>
    /// Arithmetic-logic unit for 8 bit operations
    /// </summary>
    public Alu8 Alu8 { get; }

    /// <summary>
    /// Arithmetic-logic unit for 16 bit operations
    /// </summary>
    public Alu16 Alu16 { get; }

    /// <summary>
    /// Arithmetic-logic unit for 32 bit operations
    /// </summary>
    public Alu32 Alu32 { get; }

    /// <summary>
    /// Gets or sets the value of AX register.
    /// </summary>
    public ushort AX { get => State.AX; set => State.AX = value; }

    /// <summary>
    /// Gets or sets the value of AH register.
    /// </summary>
    public byte AH { get => State.AH; set => State.AH = value; }

    /// <summary>
    /// Gets or sets the value of AL register.
    /// </summary>
    public byte AL { get => State.AL; set => State.AL = value; }

    /// <summary>
    /// Gets or sets the value of BX register.
    /// </summary>
    public ushort BX { get => State.BX; set => State.BX = value; }

    /// <summary>
    /// Gets or sets the value of BH register.
    /// </summary>
    public byte BH { get => State.BH; set => State.BH = value; }

    /// <summary>
    /// Gets or sets the value of BL register.
    /// </summary>
    public byte BL { get => State.BL; set => State.BL = value; }

    /// <summary>
    /// Gets or sets the value of CX register.
    /// </summary>
    public ushort CX { get => State.CX; set => State.CX = value; }

    /// <summary>
    /// Gets or sets the value of CH register.
    /// </summary>
    public byte CH { get => State.CH; set => State.CH = value; }

    /// <summary>
    /// Gets or sets the value of CL register.
    /// </summary>
    public byte CL { get => State.CL; set => State.CL = value; }

    /// <summary>
    /// Gets or sets the value of DX register.
    /// </summary>
    public ushort DX { get => State.DX; set => State.DX = value; }

    /// <summary>
    /// Gets or sets the value of DH register.
    /// </summary>
    public byte DH { get => State.DH; set => State.DH = value; }

    /// <summary>
    /// Gets or sets the value of DL register.
    /// </summary>
    public byte DL { get => State.DL; set => State.DL = value; }

    /// <summary>
    /// Gets or sets the value of SP register.
    /// </summary>
    public ushort SP { get => State.SP; set => State.SP = value; }

    /// <summary>
    /// Gets or sets the value of BP register.
    /// </summary>
    public ushort BP { get => State.BP; set => State.BP = value; }

    /// <summary>
    /// Gets or sets the value of SI register.
    /// </summary>
    public ushort SI { get => State.SI; set => State.SI = value; }

    /// <summary>
    /// Gets or sets the value of DI register.
    /// </summary>
    public ushort DI { get => State.DI; set => State.DI = value; }

    /// <summary>
    /// Gets or sets the value of CS register.
    /// </summary>
    public ushort CS { get => State.CS; set => State.CS = value; }

    /// <summary>
    /// Gets or sets the value of DS register.
    /// </summary>
    public ushort DS { get => State.DS; set => State.DS = value; }

    /// <summary>
    /// Gets or sets the value of ES register.
    /// </summary>
    public ushort ES { get => State.ES; set => State.ES = value; }

    /// <summary>
    /// Gets or sets the value of FS register.
    /// </summary>
    public ushort FS { get => State.FS; set => State.FS = value; }

    /// <summary>
    /// Gets or sets the value of GS register.
    /// </summary>
    public ushort GS { get => State.GS; set => State.GS = value; }

    /// <summary>
    /// Gets or sets the value of SS register.
    /// </summary>
    public ushort SS { get => State.SS; set => State.SS = value; }

    /// <summary>
    /// Gets or sets the value of IP register.
    /// </summary>
    public ushort IP { get => State.IP; set => State.IP = value; }

    /// <summary>
    /// Gets or sets the value of the auxiliary flag.
    /// </summary>
    public bool AuxiliaryFlag { get => State.AuxiliaryFlag; set => State.AuxiliaryFlag = value; }

    /// <summary>
    /// Gets or sets the value of the carry flag.
    /// </summary>
    public bool CarryFlag { get => State.CarryFlag; set => State.CarryFlag = value; }

    /// <summary>
    /// Gets or sets the value of the direction flag.
    /// </summary>
    public bool DirectionFlag { get => State.DirectionFlag; set => State.DirectionFlag = value; }

    /// <summary>
    /// Gets or sets the value of the interrupt flag.
    /// </summary>
    public bool InterruptFlag { get => State.InterruptFlag; set => State.InterruptFlag = value; }

    /// <summary>
    /// Gets or sets the value of the overflow flag.
    /// </summary>
    public bool OverflowFlag { get => State.OverflowFlag; set => State.OverflowFlag = value; }

    /// <summary>
    /// Gets or sets the value of the parity flag.
    /// </summary>
    public bool ParityFlag { get => State.ParityFlag; set => State.ParityFlag = value; }

    /// <summary>
    /// Gets or sets the value of the sign flag.
    /// </summary>
    public bool SignFlag { get => State.SignFlag; set => State.SignFlag = value; }

    /// <summary>
    /// Gets or sets the value of the trap flag.
    /// </summary>
    public bool TrapFlag { get => State.TrapFlag; set => State.TrapFlag = value; }

    /// <summary>
    /// Gets or sets the value of the zero flag.
    /// </summary>
    public bool ZeroFlag { get => State.ZeroFlag; set => State.ZeroFlag = value; }

    /// <summary>
    /// Gets or sets the value of the flags register (32 bit value).
    /// </summary>
    public uint FlagRegister { get => State.Flags.FlagRegister; set => State.Flags.FlagRegister = value; }

    /// <summary>
    /// Gets or sets the value of the flags register (16 bit value).
    /// </summary>
    public ushort FlagRegister16 { get => State.Flags.FlagRegister16; set => State.Flags.FlagRegister = value; }

    /// <summary>
    /// Gets the offset value of the Direction Flag for 8 bit CPU instructions.
    /// </summary>
    public short Direction8 => State.Direction8;

    /// <summary>
    /// Gets the offset value of the Direction Flag for 16 bit CPU instructions.
    /// </summary>
    public short Direction16 => State.Direction16;

    /// <summary>
    /// Gets the offset value of the Direction Flag for 32 bit CPU instructions.
    /// </summary>
    public short Direction32 => State.Direction32;

    /// <summary>
    /// A dictionary that stores the function information of each function defined in memory.
    /// </summary>
    protected readonly IDictionary<SegmentedAddress, FunctionInformation> _functionInformations;

    /// <summary>
    /// Gets or sets the <see cref="JumpDispatcher"/>
    /// </summary>
    public JumpDispatcher JumpDispatcher { get; set; }

    /// <summary>
    /// Gets or sets whether we register self modifying code.
    /// </summary>
    /// <remarks>
    /// This is a shortcut to <see cref="ExecutionFlowRecorder.IsRegisterExecutableCodeModificationEnabled" />
    /// </remarks>
    public bool IsRegisterExecutableCodeModificationEnabled {
        get => _executionFlowRecorder.IsRegisterExecutableCodeModificationEnabled;
        set => _executionFlowRecorder.IsRegisterExecutableCodeModificationEnabled = value;
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="functionInformations">The dictionary of functions information. Each one can define an optional C# code override of the machine code.</param>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="memory">The memory bus</param>
    /// <param name="state">The CPU registers and flags</param>
    /// <param name="cpu">The emulated CPU</param>
    /// <param name="stack">The CPU stack</param>
    /// <param name="dualPic">The two programmable interrupt controllers</param>
    /// <param name="timer">The IBM PC Timer device</param>
    /// <param name="executionFlowRecorder">The class that records machine code flow</param>
    /// <param name="callbackHandler">The class that registers callback instructions definitions</param>
    /// <param name="machineBreakpoints">The class that manages software breakpoints</param>
    public CSharpOverrideHelper(IMemory memory, State state, Cpu cpu,
        Stack stack, DualPic dualPic, Timer timer,
        ExecutionFlowRecorder executionFlowRecorder, CallbackHandler callbackHandler, MachineBreakpoints machineBreakpoints,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILoggerService loggerService, Configuration configuration) {
        _cpu = cpu;
        _memory = memory;
        _state = state;
        _stack = stack;
        _dualPic = dualPic;
        _timer = timer;
        _executionFlowRecorder = executionFlowRecorder;
        _callbackHandler = callbackHandler;
        _machineBreakpoints = machineBreakpoints;
        _loggerService = loggerService;
        Configuration = configuration;
        _functionInformations = functionInformations;
        Machine = machine;
        JumpDispatcher = new();
        Alu8 = new(machine.Cpu.State);
        Alu16 = new(machine.Cpu.State);
        Alu32 = new(machine.Cpu.State);
    }

    /// <summary>
    /// Registers a function at the specified segmented address. <br/>
    /// </summary>
    /// <param name="segment">The segment part of the segmented address.</param>
    /// <param name="offset">The offset part of the segmented address.</param>
    /// <param name="name">The name of the function.</param>
    /// <remarks>
    /// Example of a valid function name: 'IncDialogueCount47A8_0x1ED_0xA1E8_0xC0B8'
    /// </remarks>
    public void DefineFunction(ushort segment, ushort offset, string name) {
        SegmentedAddress address = new(segment, offset);
        GetFunctionAtAddress(true, address);
        FunctionInformation functionInformation = new(address, name, null);
        _functionInformations.Add(address, functionInformation);
    }

    /// <summary>
    /// Registers a function at the specified segmented address. <br/>
    /// </summary>
    /// <param name="segment">The segment part of the segmented address.</param>
    /// <param name="offset">The offset part of the segmented address.</param>
    /// <param name="overrideFunc">The function to register.</param>
    /// <param name="failOnExisting">Whether to fail if a function is already defined at the specified address. Default is true.</param>
    /// <param name="name">The name of the function. If null, the name of the provided function will be parsed using the GhidraSymbolsDumper utility.</param>
    /// <exception cref="UnrecoverableException">Thrown when <paramref name="name"/> is null and the name of the provided function cannot be parsed.</exception>
    /// <remarks>
    /// Example of a valid function name: 'IncDialogueCount47A8_0x1ED_0xA1E8_0xC0B8'
    /// </remarks>
    public void DefineFunction(ushort segment,
        ushort offset,
        Func<int, Action> overrideFunc,
        bool failOnExisting = true,
        string? name = null) {
        SegmentedAddress address = new(segment, offset);
        FunctionInformation? existing = GetFunctionAtAddress(failOnExisting, address);
        if (existing?.HasOverride is true) {
            // Do not overwrite existing code with override
            return;
        }

        string functionName;
        if (name != null) {
            functionName = name;
        } else {
            string methodName = overrideFunc.Method.Name;
            FunctionInformation? parsedFunctionInformation = GhidraSymbolsDumper.NameToFunctionInformation(_loggerService, methodName);
            if (parsedFunctionInformation == null) {
                throw new UnrecoverableException("Cannot parse " + methodName +
                    " into a spice86 function name as format is not correct.");
            }

            functionName = parsedFunctionInformation.Name;
        }

        _functionInformations[address] = (new(address, functionName, overrideFunc));
    }

    /// <summary>
    /// Gets the function information for the function at the specified address.
    /// </summary>
    /// <param name="failOnExisting">A flag indicating whether to throw an exception if a function already exists at the specified address.</param>
    /// <param name="address">The address of the function to retrieve.</param>
    /// <returns>The function information for the function at the specified address, or null if no function exists at that address.</returns>
    /// <exception cref="UnrecoverableException">Thrown if a function already exists at the specified address and failOnExisting is true.</exception>
    public FunctionInformation? GetFunctionAtAddress(bool failOnExisting, SegmentedAddress address) {
        if (_functionInformations.TryGetValue(address, out FunctionInformation? existingFunctionInformation)) {
            if (!failOnExisting) {
                return existingFunctionInformation;
            }

            string error =
                $"There is already a function overriden at address {address} named {existingFunctionInformation.Name}. Please check your mappings for duplicates.";
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(
                    "There is already a function defined at address {Address} named {ExistingFunctionInformationName} but you are trying to redefine it. Please check your mappings for duplicates",
                    address, existingFunctionInformation.Name);
            }

            throw new UnrecoverableException(error);
        }

        return null;
    }

    /// <summary>
    /// Returns an <see cref="Action"/> that sets CS and IP to the provided values.
    /// </summary>
    /// <param name="cs">The value for the CS register.</param>
    /// <param name="ip">The value for the IP register.</param>
    /// <returns>The <see cref="Action"/> that will mutate CS and IP when invoked.</returns>
    public Action FarJump(ushort cs, ushort ip) {
        return () => {
            State.CS = cs;
            State.IP = ip;
        };
    }

    /// <summary>
    /// Returns an <see cref="Action"/> than makes the CPU perform a <see cref="Cpu.FarRet"/> instruction when invoked.
    /// </summary>
    /// <returns>returns an <see cref="Action"/>than makes the CPU perform a <see cref="Cpu.FarRet"/> instruction when invoked.</returns>
    public Action FarRet(ushort numberOfBytesToPop = 0) {
        return () => Cpu.FarRet(numberOfBytesToPop);
    }

    /// <summary>
    /// Returns an <see cref="Action"/> than makes the CPU perform an IRET instruction when invoked.
    /// </summary>
    /// <returns>returns an <see cref="Action"/> that runs the <see cref="InterruptRet"/> method from the CPU when invoked.</returns>
    public Action InterruptRet() {
        return () => Cpu.InterruptRet();
    }

    /// <summary>
    /// Returns an <see cref="Action"/> that will modify the IP register to the provided value.
    /// </summary>
    /// <param name="ip">The target value for the IP register.</param>
    /// <returns>The action that will modify the IP register when invoked.</returns>
    public Action NearJump(ushort ip) {
        return () => State.IP = ip;
    }

    /// <summary>
    /// Returns an action that performs a near return.
    /// </summary>
    /// <param name="numberOfBytesToPop">The number of bytes to remove from the stack.</param>
    /// <returns>An action that performs a near return.</returns>
    public Action NearRet(ushort numberOfBytesToPop = 0) {
        return () => Cpu.NearRet(numberOfBytesToPop);
    }

    /// <summary>
    /// Performs a near call.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="function">The function to call.</param>
    public void NearCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, function, () => {
            Stack.Push16(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    /// <summary>
    /// Performs a far call.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the call.</param>
    /// <param name="function">The function to call.</param>
    public void FarCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, function, () => {
            Stack.Push16(expectedReturnCs);
            Stack.Push16(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    /// <summary>
    /// Call the given callback number
    /// </summary>
    /// <param name="callbackNumber">The callback identifier.</param>
    public void Callback(byte callbackNumber) {
        _callbackHandler.RunFromOverriden(callbackNumber);
    }

    /// <summary>
    /// Performs an interrupt call by executing the given function and returning to the specified return address.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the interrupt call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the interrupt call.</param>
    /// <param name="function">The function to execute as part of the interrupt call.</param>
    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, function, () => {
            Stack.Push16(State.Flags.FlagRegister16);
            InterruptFlag = false;
            Stack.Push16(expectedReturnCs);
            Stack.Push16(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    /// <summary>
    /// Performs an interrupt call.
    /// </summary>
    /// <param name="expectedReturnCs">The excepted value of the CS register after the interrupt call.</param>
    /// <param name="expectedReturnIp">The excepted value of the IP register after the interrupt call.</param>
    /// <param name="vectorNumber">The vector number to call for the interrupt.</param>
    /// <exception cref="UnrecoverableException">If the interrupt vector number is not recognized.</exception>
    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, byte vectorNumber) {
        SegmentedAddress target = new(Cpu.InterruptVectorTable[vectorNumber]);
        Func<int, Action>? function = SearchFunctionOverride(target);
        if (function is null) {
            throw FailAsUntested($"Could not find an override at address {target}");
        }

        InterruptCall(expectedReturnCs, expectedReturnIp, function);
    }

    /// <summary>
    /// Returns the C# function override, or <c>null</c> if not found.
    /// </summary>
    /// <param name="target">The <see cref="SegmentedAddress"/> where the function is defined.</param>
    /// <returns>The C# function override, or <c>null</c> if not found.</returns>
    public Func<int, Action>? SearchFunctionOverride(SegmentedAddress target) {
        if (!_functionInformations.TryGetValue(target,
                out FunctionInformation? functionInformation)) {
            return null;
        }
        return functionInformation.FunctionOverride;
    }

    private void ExecuteCallEnsuringSameStack(ushort expectedReturnCs, ushort expectedReturnIp,
        Func<int, Action> function, Action action) {
        uint expectedStackAddress = State.StackPhysicalAddress;
        State.CS = expectedReturnCs;
        State.IP = expectedReturnIp;
        ExecuteCall(function, action);
        ushort actualReturnCs = State.CS;
        ushort actualReturnIp = State.IP;
        uint actualStackAddress = State.StackPhysicalAddress;
        // Do not return to the caller until we are sure we are at the right place
        while (actualReturnCs != expectedReturnCs ||
               actualReturnIp != expectedReturnIp) {
            SegmentedAddress expectedReturn = new SegmentedAddress(expectedReturnCs, expectedReturnIp);
            SegmentedAddress actualReturn = new SegmentedAddress(actualReturnCs, actualReturnIp);
            string message =
                "The original code is trying to jump via call stack modification. Expected to return at: " +
                expectedReturn + " but actually returning to: " + actualReturn + " Stack address before: " +
                expectedStackAddress + " Stack address after: " + actualStackAddress;
            if (!_functionInformations.TryGetValue(actualReturn, out FunctionInformation? actualTarget)) {
                throw FailAsUntested(message);
            }

            message += " Found " + actualTarget.Name + " there.";
            if (actualTarget.FunctionOverride != null) {
                message += " Calling it.";
                _loggerService.Warning("{Message}", message);
                ExecuteCall(actualTarget.FunctionOverride, () => actualTarget.FunctionOverride.Invoke(0).Invoke());
                actualStackAddress = State.StackPhysicalAddress;
                actualReturnCs = State.CS;
                actualReturnIp = State.IP;
            } else {
                throw FailAsUntested(message);
            }
        }
    }

    /// <summary>
    /// Executes the given action with the specified function as starting point in the jump dispatcher.
    /// </summary>
    /// <param name="function">The function to set as starting point in the jump dispatcher.</param>
    /// <param name="action">The action to execute.</param>
    /// <remarks>
    /// This method temporarily replaces the current jump dispatcher with a new instance that has the specified function
    /// as its starting point. After the action has been executed, the original jump dispatcher is restored.
    /// </remarks>
    private void ExecuteCall(Func<int, Action> function, Action action) {
        JumpDispatcher currentJumpDispatcher = JumpDispatcher;
        // Ensure the jump dispatcher has the function we are calling as starting point
        JumpDispatcher = JumpDispatcher.CreateNew(function);
        action.Invoke();
        JumpDispatcher = currentJumpDispatcher;
    }

    /// <summary>
    /// Overrides the machine code at the specified segment and offset with a new implementation provided by the <paramref name="renamedOverride"/> function.
    /// </summary>
    /// <param name="segment">The segment of the instruction to override.</param>
    /// <param name="offset">The offset of the instruction to override.</param>
    /// <param name="renamedOverride">An action that provides the new implementation to use for the instruction.</param>
    public void OverrideInstruction(ushort segment, ushort offset, Func<Action> renamedOverride) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.EXECUTION,
            MemoryUtils.ToPhysicalAddress(
                segment,
                offset),
            _ => renamedOverride.Invoke().Invoke()
            , false);
        _machineBreakpoints.ToggleBreakPoint(breakPoint, true);
    }

    /// <summary>
    /// Executes the specified action on top of the instruction at the specified segment and offset.
    /// </summary>
    /// <param name="segment">The segment of the instruction to execute the action on.</param>
    /// <param name="offset">The offset of the instruction to execute the action on.</param>
    /// <param name="action">The action to execute on top of the instruction.</param>
    public void DoOnTopOfInstruction(ushort segment, ushort offset, Action action) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.EXECUTION,
            MemoryUtils.ToPhysicalAddress(
                segment,
                offset),
            _ => action.Invoke()
            , false);
        _machineBreakpoints.ToggleBreakPoint(breakPoint, true);
    }

    /// <summary>
    /// Executes the specified action when the byte at the specified segment and offset is written to.
    /// </summary>
    /// <param name="segment">The segment of the memory location to watch.</param>
    /// <param name="offset">The offset of the memory location to watch.</param>
    /// <param name="action">The action to execute when the memory location is written to.</param>
    public void DoOnMemoryWrite(ushort segment, ushort offset, Action action) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.WRITE,
            MemoryUtils.ToPhysicalAddress(segment, offset),
            _ => action.Invoke()
            , false);
        _machineBreakpoints.ToggleBreakPoint(breakPoint, true);
    }

    /// <summary>
    /// Checks if the vtable contains the expected segment and offset values.
    /// </summary>
    /// <param name="segmentRegisterIndex">The index of the segment register to use.</param>
    /// <param name="offset">The offset of the vtable entry to check.</param>
    /// <param name="expectedSegment">The expected segment value.</param>
    /// <param name="expectedOffset">The expected offset value.</param>
    /// <exception cref="UnrecoverableException">Thrown when the found segment or offset value doesn't match the expected values.</exception>
    public void CheckVtableContainsExpected(
        int segmentRegisterIndex,
        ushort offset,
        ushort expectedSegment,
        ushort expectedOffset) {
        uint address = MemoryUtils.ToPhysicalAddress(State.SegmentRegisters.UInt16[segmentRegisterIndex], offset);
        (ushort foundSegment, ushort foundOffset) = Memory.SegmentedAddress[address];
        if (foundOffset != expectedOffset || foundSegment != expectedSegment) {
            throw FailAsUntested(
                $"Call table value changed, we would not call the method the game is calling. Expected: {new SegmentedAddress(expectedSegment, expectedOffset)} found: {new SegmentedAddress(foundSegment, foundOffset)}");
        }
    }

    /// <summary>
    /// Defines an executable area in memory by registering a memory write break point for every byte in the specified address range.
    /// </summary>
    /// <param name="startAddress">The start address of the executable area.</param>
    /// <param name="endAddress">The end address of the executable area.</param>
    public void DefineExecutableArea(uint startAddress, uint endAddress) {
        for (uint address = startAddress; address <= endAddress; address++) {
            _executionFlowRecorder.RegisterExecutableByteModificationBreakPoint(Memory, State, Machine.MachineBreakpoints, address);
        }
    }

    /// <summary>
    /// Call this in your override when you re-implement a function with a branch that seems to be never
    /// reached.
    /// <param name="message">The error message for the <see cref="UnrecoverableException"/></param>
    /// <returns>An new instance of <see cref="UnrecoverableException"/> that you should throw.</returns>
    /// </summary>
    public UnrecoverableException FailAsUntested(string message) {
        string error =
            $"Untested code reached, please tell us how to reach this state. Here is the message: {message}. Here is the Machine stack: {State}";
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("{Error}", error);
        }

        return new UnrecoverableException(error);
    }

    /// <summary>
    /// Throws an exception if the given value is not one of the possible values.
    /// </summary>
    /// <param name="value">The value to check for support.</param>
    /// <param name="possibleValues">The list of possible values that are supported.</param>
    /// <exception cref="UnrecoverableException">Thrown if the value is not in the list of supported values.</exception>
    public void FailIfValueIsNot(uint value, params uint[] possibleValues) {
        if (!possibleValues.Contains(value)) {
            throw FailAsUntested($"Value {value} not in list of supported values");
        }
    }

    /// <summary>
    /// Runs any pending interrupt requests. <br/>
    /// As long as the target programs triggers interrupts, you must call this often enough so interrupts can run.
    /// </summary>
    /// <param name="expectedReturnCs">The excepted value of the CS register after the interruption is done.</param>
    /// <param name="expectedReturnIp">The excepted value of the IP register after the interruption is done</param>
    public void CheckExternalEvents(ushort expectedReturnCs, ushort expectedReturnIp) {
        if (!State.IsRunning) {
            Exit();
        }
        State.IncCycles();
        _timer.Tick();
        if (!InterruptFlag) {
            return;
        }
        byte? vectorNumber = _dualPic.ComputeVectorNumber();
        if (vectorNumber != null) {
            InterruptCall(expectedReturnCs, expectedReturnIp, vectorNumber.Value);
        }
    }

    /// <summary>
    /// Call this to perform an interrupt request.
    /// </summary>
    /// <param name="vectorNumber">The vector number to call</param>
    public void Interrupt(byte vectorNumber) {
        _callbackHandler.RunFromOverriden(vectorNumber);
    }
    
    /// <summary>
    /// Defines C# functions for provided interrupt handlers so that when overriden code generates an interrupt, an override for it is found and executed.
    /// Does not currently handle mouse code which has more than a callback + iret.
    /// </summary>
    public void SetProvidedInterruptHandlersAsOverridden() {
        InterruptVectorTable ivt = new InterruptVectorTable(Memory);
        for (byte i = 0; i < 0xFF; i++) {
            SegmentedAddress handlerAddress = ivt[i];
            if (handlerAddress.Segment == 0 && handlerAddress.Offset == 0) {
                continue;
            }
            int callback = i;
            DefineFunction(handlerAddress.Segment, handlerAddress.Offset, (offset) => {
                    _callbackHandler.RunFromOverriden(callback);
                    return InterruptRet();
                }, false, $"provided_interrupt_handler_{ConvertUtils.ToHex(i)}");
        }
    }

    /// <summary>
    /// Halt the program.
    /// </summary>
    /// <returns>An <see cref="Action"/> that exits the program.</returns>
    public Action Hlt() => Exit;

    /// <summary>
    /// Exit the program.
    /// </summary>
    /// <exception cref="HaltRequestedException">The exception throw in order to exit the program.</exception>
    protected void Exit() {
        _loggerService.Verbose("Program requested exit. Terminating now");
        throw new HaltRequestedException();
    }
}