namespace Spice86.ViewModels;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

internal partial class CfgCpuGraphViewModel : ViewModelBase, IEmulatorDebugger {
    public CfgCpuGraphViewModel(IUIDispatcherTimer uIDispatcherTimer) {
        
    }

    public void VisitCpu(Cpu cpu) {
        throw new NotImplementedException();
    }

    public void VisitCpuState(State state) {
        throw new NotImplementedException();
    }

    public void VisitDacPalette(ArgbPalette argbPalette) {
        throw new NotImplementedException();
    }

    public void VisitDacRegisters(DacRegisters dacRegisters) {
        throw new NotImplementedException();
    }

    public void VisitExternalMidiDevice(Midi midi) {
        throw new NotImplementedException();
    }

    public void VisitMainMemory(IMemory memory) {
        throw new NotImplementedException();
    }

    public void VisitSoundDevice(CfgCpu cfgCpu) {
        throw new NotImplementedException();
    }

    public void VisitVgaCard(VgaCard vgaCard) {
        throw new NotImplementedException();
    }

    public void VisitVgaRenderer(IVgaRenderer vgaRenderer) {
        throw new NotImplementedException();
    }

    public void VisitVideoState(IVideoState videoState) {
        throw new NotImplementedException();
    }

    public void VistExecutorCfgNodeVisitor(ExecutorCfgNodeVisitor executorCfgNodeVisitor) {
        throw new NotImplementedException();
    }
}
