namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Declares an interrupt service contract on the method implementing the service.
/// Use the parameterless form on a handler without a selector register: the
/// attribute declares the handler's single contract. Use the form with a selector
/// value on a handler whose services are chosen by a selector register. The
/// service name is the method name.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InterruptServiceAttribute : Attribute {
    /// <summary>The <see cref="Al"/> value of a declaration that is not an AL sub-selector
    /// variant.</summary>
    public const int NoAl = -1;

    /// <summary>
    /// Declares the single contract of a handler without a selector register.
    /// </summary>
    public InterruptServiceAttribute() {
    }

    /// <summary>
    /// Declares a service chosen by the handler's selector register.
    /// </summary>
    /// <param name="selectorValue">The selector register value that invokes this service.</param>
    public InterruptServiceAttribute(int selectorValue) {
        SelectorValue = selectorValue;
    }

    /// <summary>The selector register value, or <see langword="null"/> for the single-contract form.</summary>
    public int? SelectorValue { get; }

    /// <summary>The AL sub-selector value this declaration is a variant of
    /// <see cref="SelectorValue"/>, or <see cref="NoAl"/> (the default) for the base service
    /// declaration. A variant refines the contract of the same dispatch entry when generated
    /// code proves AL constant; runtime dispatch stays on the selector register alone.</summary>
    public int Al { get; init; } = NoAl;

    /// <summary>Whether the service implementation reads the interrupt frame pushed by the
    /// <c>int</c> (for example the caller CS:IP an EXEC service stores in the child PSP).
    /// A generated typed call then keeps the frame-pushing <c>ServiceCall</c> form.</summary>
    public bool ReadsFrame { get; init; }

    /// <summary>The service name, or <see langword="null"/> to name the service after the
    /// annotated method.</summary>
    public string? Name { get; init; }

    /// <summary>The register and flag set read by the service.</summary>
    public CpuValues Reads { get; init; }

    /// <summary>
    /// The register and flag set written by the service (whose value after the
    /// service can differ from the caller's).
    /// </summary>
    public CpuValues Writes { get; init; }

    /// <summary>How the service hands control back: whether it returns to the caller, runs a
    /// child program, or terminates the current process.</summary>
    public ServiceTransferKind Transfer { get; init; }

    /// <summary>The effect the service has on emulated memory.</summary>
    public ServiceMemoryEffect Memory { get; init; }

    /// <summary>Whether the service performs I/O port accesses.</summary>
    public bool Io { get; init; }

    /// <summary>The kind of string the service reads at <c>DS:DX</c>, when it takes one.</summary>
    public ServiceStringArgument StringArgument { get; init; }

    /// <summary>The name of the existing public instance method on the handler that generated
    /// code calls directly when the vector stayed bound to the built-in handler. Null for a
    /// service that keeps ordinary interrupt dispatch.</summary>
    public string? Implementation { get; init; }

    /// <summary>The register/flag binding of each implementation parameter in declaration
    /// order. <see langword="null"/> for the <see cref="ReverseEngineer.Contracts.ServiceCallSignature.Parameters"/>
    /// order; an explicit empty list binds a parameterless implementation.</summary>
    public CpuValues[]? Inputs { get; init; }

    /// <summary>
    /// Whether the service's effects are not bounded. The contract is then the
    /// <see cref="ServiceContract.Unknown"/> singleton; combining it with the
    /// other properties is an error.
    /// </summary>
    public bool Unknown { get; init; }

    /// <summary>Builds the declared service entry, named after the annotated method, without
    /// its implementation binding: <see cref="InterruptServiceAttributes"/> resolves that
    /// once it knows the handler's selector register.</summary>
    /// <param name="methodName">The name of the annotated method.</param>
    internal InterruptServiceEntry ToEntry(string methodName, string declaringTypeName) {
        if (SelectorValue is null && Al != NoAl) {
            throw new InvalidOperationException(
                $"{declaringTypeName}.{methodName}: an AL sub-selector variant needs the selector form");
        }
        if (Unknown) {
            if (Reads != CpuValues.None || Writes != CpuValues.None ||
                Memory != ServiceMemoryEffect.None || Io ||
                Transfer != ServiceTransferKind.Returns) {
                throw new InvalidOperationException(
                    $"{declaringTypeName}.{methodName}: [InterruptService(Unknown = true)] cannot be combined " +
                    "with Reads, Writes, Memory, Io or Transfer");
            }
            if (Implementation is not null || Inputs is not null) {
                throw new InvalidOperationException(
                    $"{declaringTypeName}.{methodName}: [InterruptService(Unknown = true)] cannot be combined " +
                    "with Implementation or Inputs");
            }
            return new InterruptServiceEntry(SelectorValue ?? 0, Name ?? methodName,
                ServiceContract.Unknown, StringArgument, null, [], Al);
        }
        return new InterruptServiceEntry(SelectorValue ?? 0, Name ?? methodName,
            new ServiceContract(new CallEffect(Reads, Writes), Memory, Io, Transfer),
            StringArgument, null, [], Al) { ReadsFrame = ReadsFrame };
    }
}
