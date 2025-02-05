namespace Spice86.Core.Emulator.VM;

public interface ICyclesLimiter
{
    public const int RealModeCpuCylesPerMs = 3000;

    public int TargetCpuCylesPerMs { get; set; }

    public void IncreaseCycles();

    public void DecreaseCycles();
}
