namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// View model for the CPU registers panel.
/// </summary>
public partial class RegistersViewModel : ObservableObject, IRegistersViewModel {
    private readonly State _state;

    /// <summary>
    /// Gets the general purpose registers.
    /// </summary>
    public ObservableCollection<RegisterViewModel> GeneralRegisters { get; } = [];

    /// <summary>
    /// Gets the segment registers.
    /// </summary>
    public ObservableCollection<RegisterViewModel> SegmentRegisters { get; } = [];

    /// <summary>
    /// Gets the pointer registers.
    /// </summary>
    public ObservableCollection<RegisterViewModel> PointerRegisters { get; } = [];
    
    /// <summary>
    /// Gets the eflag register.
    /// </summary>
    public RegisterViewModel EFlagRegister { get; private set; }

    /// <summary>
    /// Gets the CPU flags.
    /// </summary>
    public ObservableCollection<FlagViewModel> Flags { get; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistersViewModel"/> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    public RegistersViewModel(State state) {
        _state = state;
        InitializeRegisters();
    }

    [MemberNotNull(nameof(EFlagRegister))]
    private void InitializeRegisters() {
        // General purpose registers (32-bit versions as they were in DOS era)
        GeneralRegisters.Add(new RegisterViewModel("EAX", _state, s => s.EAX, 32));
        GeneralRegisters.Add(new RegisterViewModel("EBX", _state, s => s.EBX, 32));
        GeneralRegisters.Add(new RegisterViewModel("ECX", _state, s => s.ECX, 32));
        GeneralRegisters.Add(new RegisterViewModel("EDX", _state, s => s.EDX, 32));

        // Segment registers
        SegmentRegisters.Add(new RegisterViewModel("CS", _state, s => s.CS, 16));
        SegmentRegisters.Add(new RegisterViewModel("DS", _state, s => s.DS, 16));
        SegmentRegisters.Add(new RegisterViewModel("ES", _state, s => s.ES, 16));
        SegmentRegisters.Add(new RegisterViewModel("SS", _state, s => s.SS, 16));
        SegmentRegisters.Add(new RegisterViewModel("FS", _state, s => s.FS, 16));
        SegmentRegisters.Add(new RegisterViewModel("GS", _state, s => s.GS, 16));

        // Pointer registers
        PointerRegisters.Add(new RegisterViewModel("EIP", _state, s => s.IP, 32));
        PointerRegisters.Add(new RegisterViewModel("ESP", _state, s => s.ESP, 32));
        PointerRegisters.Add(new RegisterViewModel("EBP", _state, s => s.EBP, 32));
        PointerRegisters.Add(new RegisterViewModel("ESI", _state, s => s.ESI, 32));
        PointerRegisters.Add(new RegisterViewModel("EDI", _state, s => s.EDI, 32));

        EFlagRegister = new RegisterViewModel("FLAG", _state, s => s.Flags.FlagRegister, 32);

        // Flags
        Flags.Add(new FlagViewModel("CF", _state, s => s.CarryFlag));
        Flags.Add(new FlagViewModel("PF", _state, s => s.ParityFlag));
        Flags.Add(new FlagViewModel("AF", _state, s => s.AuxiliaryFlag));
        Flags.Add(new FlagViewModel("ZF", _state, s => s.ZeroFlag));
        Flags.Add(new FlagViewModel("SF", _state, s => s.SignFlag));
        Flags.Add(new FlagViewModel("TF", _state, s => s.TrapFlag));
        Flags.Add(new FlagViewModel("IF", _state, s => s.InterruptFlag));
        Flags.Add(new FlagViewModel("DF", _state, s => s.DirectionFlag));
        Flags.Add(new FlagViewModel("OF", _state, s => s.OverflowFlag));
    }

    /// <summary>
    /// Updates all register values from the CPU state.
    /// </summary>
    public void Update() {
        foreach (RegisterViewModel register in GeneralRegisters) {
            register.Update();
        }

        foreach (RegisterViewModel register in SegmentRegisters) {
            register.Update();
        }

        foreach (RegisterViewModel register in PointerRegisters) {
            register.Update();
        }
        
        EFlagRegister.Update();

        foreach (FlagViewModel flag in Flags) {
            flag.Update();
        }
    }
}