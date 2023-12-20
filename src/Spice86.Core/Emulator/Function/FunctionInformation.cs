namespace Spice86.Core.Emulator.Function;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Centralizes all the known information about a machine code function.
/// </summary>
public record FunctionInformation : IComparable<FunctionInformation> {
    private readonly SegmentedAddress _address;

    private ISet<FunctionInformation>? _callers;

    /// <summary>
    /// Gets the C# override of the machine code.
    /// </summary>
    public Func<int, Action>? FunctionOverride { get; }

    private Dictionary<FunctionReturn, HashSet<SegmentedAddress>>? _returns;

    private Dictionary<FunctionReturn, HashSet<SegmentedAddress>>? _unalignedReturns;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="address">The address of the function in memory.</param>
    /// <param name="name">The name of the function.</param>
    public FunctionInformation(SegmentedAddress address, string name) : this(address, name, null) {
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="address">The address of the function in memory.</param>
    /// <param name="name">The name of the function.</param>
    /// <param name="functionOverride">The C# function override of machine code.</param>
    public FunctionInformation(SegmentedAddress address, string name, Func<int, Action>? functionOverride) {
        _address = address;
        Name = name;
        FunctionOverride = functionOverride;
    }

    /// <summary>
    /// Gets or creates the dictionary of returns for the function.
    /// </summary>
    public Dictionary<FunctionReturn, HashSet<SegmentedAddress>> Returns {
        get {
            _returns ??= new();
            return _returns;
        }
    }

    /// <summary>
    /// Gets or creates the dictionary of unaligned returns for the function.
    /// </summary>
    public Dictionary<FunctionReturn, HashSet<SegmentedAddress>> UnalignedReturns {
        get {
            _unalignedReturns ??= new();
            return _unalignedReturns;
        }
    }
    
    /// <summary>
    /// Adds the specified function return and target to the <see cref="Returns"/> dictionary property.
    /// </summary>
    /// <param name="functionReturn">The function return to add to the Returns dictionary.</param>
    /// <param name="target">The target address to add to the Returns dictionary.</param>
    public void AddReturn(FunctionReturn functionReturn, SegmentedAddress? target) {
        AddReturn(Returns, functionReturn, target);
    }

    /// <summary>
    /// Adds the specified function return and target to the <see cref="UnalignedReturns"/> dictionary property.
    /// </summary>
    /// <param name="functionReturn">The function return to add to the UnalignedReturns dictionary.</param>
    /// <param name="target">The target address to add to the UnalignedReturns dictionary.</param>
    public void AddUnalignedReturn(FunctionReturn functionReturn, SegmentedAddress? target) {
        AddReturn(UnalignedReturns, functionReturn, target);
    }

    /// <summary>
    /// Invokes the C# override of the machine code for the function.
    /// </summary>
    public void CallOverride() {
        if (HasOverride) {
            Action? retHandler = FunctionOverride?.Invoke(0);
            // The override returns what to do when going back to emu mode, so let's do it!
            retHandler?.Invoke();
        }
    }

    /// <inheritdoc />
    public int CompareTo(FunctionInformation? other) {
        return Address.CompareTo(other?.Address);
    }

    /// <summary>
    /// Adds the caller to the <see cref="Callers"/> if not null, and increments <see cref="CalledCount"/>
    /// </summary>
    /// <param name="caller">The caller to the function</param>
    public void Enter(FunctionInformation? caller) {
        if (caller != null) {
            Callers.Add(caller);
        }

        CalledCount++;
    }

    /// <summary>
    /// The <see cref="SegmentedAddress"/> of the function in memory.
    /// </summary>
    public SegmentedAddress Address => _address;

    /// <summary>
    /// The number of times the function was called. <br/>
    /// <remarks>
    /// Incremented each time <see cref="Enter"/> is invoked.
    /// </remarks>
    /// </summary>
    public int CalledCount { get; private set; }

    /// <summary>
    /// Contains all the callers to the function, registered by <see cref="Enter"/>
    /// </summary>
    public ISet<FunctionInformation> Callers {
        get {
            _callers ??= new HashSet<FunctionInformation>();
            return _callers;
        }
    }

    /// <summary>
    /// Returns the hash code of the function's <see cref="Address"/>
    /// </summary>
    /// <returns>The hash code of the function's <see cref="Address"/></returns>
    public override int GetHashCode() {
        return _address.GetHashCode();
    }

    /// <summary>
    /// The name of the function.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets whether the machine code was overriden by C# code.
    /// </summary>
    public bool HasOverride => FunctionOverride != null;

    /// <inheritdoc />
    public override string ToString() {
        return $"{Name}_{ConvertUtils.ToCSharpStringWithPhysical(_address)}";
    }

    private static void AddReturn(Dictionary<FunctionReturn, HashSet<SegmentedAddress>> returnsMap, FunctionReturn functionReturn, SegmentedAddress? target) {
        if (target == null) {
            return;
        }
        returnsMap.TryGetValue(functionReturn, out HashSet<SegmentedAddress>? addresses);
        if (addresses == null) {
            addresses = new HashSet<SegmentedAddress>();
            returnsMap.Add(functionReturn, addresses);
        }
        addresses.Add(target.Value);
    }
}