namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.CPU;

public struct MouseDriverSavedRegisters {
    private ushort _ax;
    private ushort _bx;
    private ushort _cx;
    private ushort _dx;
    private ushort _si;
    private ushort _di;
    private ushort _ds;
    private ushort _es;
    private ushort _sp;
    private ushort _bp;
    public bool Locked { get; private set; }

    public void Restore(State state) {
        state.ES = _es;
        state.DS = _ds;
        state.DI = _di;
        state.SI = _si;
        state.BP = _bp;
        // state.SP = _sp;
        state.BX = _bx;
        state.DX = _dx;
        state.CX = _cx;
        state.AX = _ax;
        // state.InterruptFlag = true;
    }

    public void Save(State state) {
        // state.InterruptFlag = false;
        _ax = state.AX;
        _cx = state.CX;
        _dx = state.DX;
        _bx = state.BX;
        // _sp = state.SP;
        _bp = state.BP;
        _si = state.SI;
        _di = state.DI;
        _ds = state.DS;
        _es = state.ES;
    }

    public void Release() {
        Locked = false;
    }

    public void Lock() {
        Locked = true;
    }
}