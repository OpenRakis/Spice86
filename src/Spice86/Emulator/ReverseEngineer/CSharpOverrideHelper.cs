namespace Spice86.Emulator.ReverseEngineer;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spice86.Emulator.Machine;
using Spice86.Emulator.Cpu;
using Spice86.Emulator.Memory;
using Spice86.Emulator.Function;
using Spice86.Emulator.Errors;
using Spice86.Emulator.Machine.Breakpoint;
using Spice86.Emulator.Callback;
using Spice86.Utils;

public class CSharpOverrideHelper
{
    private static readonly ILogger _logger = Log.Logger.ForContext<CSharpOverrideHelper>();
    private readonly Dictionary<SegmentedAddress, FunctionInformation> functionInformations;
    private readonly string _prefix;
    protected Machine _machine;
    protected State _state;
    protected Cpu _cpu;
    protected Memory _memory;
    protected Stack _stack;
    public CSharpOverrideHelper(Dictionary<SegmentedAddress, FunctionInformation> functionInformations, string prefix, Machine machine)
    {
        this.functionInformations = functionInformations;
        this._prefix = prefix;
        this._machine = machine;
        this._cpu = machine.GetCpu();
        this._memory = machine.GetMemory();
        this._state = _cpu.GetState();
        this._stack = _cpu.GetStack();
    }

    public virtual void SetProvidedInterruptHandlersAsOverridden()
    {
        CallbackHandler callbackHandler = _machine.GetCallbackHandler();
        Dictionary<int, SegmentedAddress> callbackAddresses = callbackHandler.GetCallbackAddresses();
        foreach (var callbackAddressEnty in callbackAddresses)
        {
            int callbackNumber = callbackAddressEnty.Key;
            SegmentedAddress callbackAddress = callbackAddressEnty.Value;
            DefineFunction(callbackAddress.GetSegment(), callbackAddress.GetOffset(), $"provided_interrupt_handler_{ConvertUtils.ToHex(callbackNumber)}",
                new Func<Action>(() =>
            {
                callbackHandler.Run(callbackNumber);
                return InterruptRet();
            }));
        }
    }

    public virtual Action NearRet()
    {
        return () => _cpu.NearRet(0);
    }

    public virtual Action FarRet()
    {
        return () => _cpu.FarRet(0);
    }

    public virtual Action InterruptRet()
    {
        return () => _cpu.InterruptRet();
    }

    public virtual Action NearJump(int ip)
    {
        return () => _state.SetIP(ip);
    }

    public virtual Action FarJump(int cs, int ip)
    {
        return () =>
        {
            _state.SetCS(cs);
            _state.SetIP(ip);
        };
    }

    public virtual void DefineFunction(int segment, int offset, string suffix)
    {
        this.DefineFunction(segment, offset, suffix, null);
    }

    public virtual void DefineFunction(int segment, int offset, string suffix, Func<Action> overrideRenamed)
    {
        SegmentedAddress address = new(segment, offset);
        var name = $"{_prefix}.{suffix}";
        if (functionInformations.TryGetValue(address, out var existingFunctionInformation))
        {
            string error = $"There is already a function defined at address {address} named {existingFunctionInformation.GetName()} but you are trying to redefine it as {name}. Please check your mappings for duplicates.";
            _logger.Error("There is already a function defined at address {@Address} named {@ExistingFunctionInformationName} but you are trying to redefine it as {@Name}. Please check your mappings for duplicates.", address, existingFunctionInformation.GetName(), name);
            throw new UnrecoverableException(error);
        }

        var runnable = new CheckedRunnable(overrideRenamed);

        var supplier = new CheckedSupplier<ICheckedRunnable>(runnable);

        FunctionInformation functionInformation = new(address, name, supplier);
        functionInformations.Add(address, functionInformation);
    }

    public virtual void DefineStaticAddress(int segment, int offset, string name)
    {
        DefineStaticAddress(segment, offset, name, false);
    }

    public virtual void DefineStaticAddress(int segment, int offset, string name, bool whiteListOnlyThisSegment)
    {
        SegmentedAddress address = new(segment, offset);
        int physicalAddress = address.ToPhysical();
        StaticAddressesRecorder recorder = _cpu.GetStaticAddressesRecorder();
        if (recorder.GetNames().TryGetValue(physicalAddress, out var existing))
        {
            string error = $"There is already a static address defined at address {address} named {existing} but you are trying to redefine it as {name}. Please check your mappings for duplicates.";
            _logger.Error("There is already a static address defined at address {@Address} named {@Existing} but you are trying to redefine it as {@Name}. Please check your mappings for duplicates.", address, existing, name);
            throw new UnrecoverableException(error);
        }

        recorder.AddName(physicalAddress, name);
        if (whiteListOnlyThisSegment)
        {
            recorder.AddSegmentTowhiteList(address);
        }
    }

    public virtual void OverrideInstruction(int segment, int offset, Func<Action> renamedOverride)
    {
        BreakPoint breakPoint = new(
            BreakPointType.EXECUTION,
            MemoryUtils.ToPhysicalAddress(
                segment,
                offset),
            (b) => renamedOverride.Invoke()
        , false);
        _machine.GetMachineBreakpoints().ToggleBreakPoint(breakPoint, true);
    }

    protected virtual void CheckVtableContainsExpected(int segmentRegisterIndex, int offset, int expectedSegment, int expectedOffset)
    {
        int address = MemoryUtils.ToPhysicalAddress(_state.GetSegmentRegisters().GetRegister(segmentRegisterIndex), offset);
        int foundOffset = _memory.GetUint16(address);
        int foundSegment = _memory.GetUint16(address + 2);
        if (foundOffset != expectedOffset || foundSegment != expectedSegment)
        {
            this.FailAsUntested($"Call table value changed, we would not call the method the game is calling. Expected: {new SegmentedAddress(expectedSegment, expectedOffset)} found: {new SegmentedAddress(foundSegment, foundOffset)}");
        }
    }

    /// <summary>
    /// Call this in your override when you re-implement a function with a branch that seems never reached. 
    /// @param message
    /// </summary>
    protected void FailAsUntested(String message)
    {
        var dumpedCallStack = _machine.DumpCallStack();
        var error = $"Untested code reached, please tell us how to reach this state.Here is the message: {message} Here is the call stack: {dumpedCallStack}";
        _logger.Error("Untested code reached, please tell us how to reach this state.Here is the message: {@Message} Here is the call stack: {@DumpedCallStack}", message, _machine.DumpCallStack());
        throw new UnrecoverableException(error);
    }
}
