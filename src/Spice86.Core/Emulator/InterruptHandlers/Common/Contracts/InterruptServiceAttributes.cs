namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using Spice86.Core.Emulator.ReverseEngineer.Contracts;

using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

/// <summary>
/// Reads the <see cref="InterruptServiceAttribute"/> declarations of a handler type,
/// walking the type hierarchy up to (not including) <see cref="object"/>, and resolves the
/// implementation binding each declaration names: the public instance method generated code
/// calls directly, with the register/flag binding of each of its parameters.
/// </summary>
public static class InterruptServiceAttributes {
    private const BindingFlags MethodFlags = BindingFlags.Instance |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<(Type Type, ServiceSelector? Selector), InterruptServiceDeclarations> Cache = new();

    /// <summary>Returns the service declarations of <paramref name="type"/>.</summary>
    /// <param name="type">The interrupt handler type whose attributes are read.</param>
    /// <param name="selector">The selector register the handler reads to choose a service, or
    /// <see langword="null"/> for a handler without one. The default input bindings of a
    /// selector-chosen service leave the selector register out.</param>
    /// <exception cref="InvalidOperationException">
    /// The type mixes the two attribute forms, declares two single contracts, uses the
    /// same selector value twice, combines <c>Unknown</c> with other properties,
    /// annotates a method that is not <c>void M()</c> or <c>void M(bool)</c>, or names an
    /// implementation no unique public instance method matches in parameter or return
    /// shape.
    /// </exception>
    public static InterruptServiceDeclarations Of(Type type, ServiceSelector? selector) =>
        Cache.GetOrAdd((type, selector), key => Read(key.Type, key.Selector));

    private static InterruptServiceDeclarations Read(Type type, ServiceSelector? selector) {
        List<(MethodInfo Method, InterruptServiceAttribute Attribute)> attributes = new();
        for (Type? current = type; current is not null && current != typeof(object);
            current = current.BaseType) {
            foreach (MethodInfo method in current.GetMethods(MethodFlags)) {
                foreach (InterruptServiceAttribute attribute in
                    method.GetCustomAttributes<InterruptServiceAttribute>()) {
                    attributes.Add((method, attribute));
                }
            }
        }

        List<(MethodInfo Method, InterruptServiceEntry Entry)> singles = attributes
            .Where(pair => pair.Attribute.SelectorValue is null)
            .Select(pair => (pair.Method, EntryOf(pair.Attribute, pair.Method, type, null)))
            .ToList();
        if (singles.Count > 1) {
            throw new InvalidOperationException(
                $"{type.Name}: more than one parameterless [InterruptService] " +
                $"({singles[0].Method.Name}, {singles[1].Method.Name})");
        }

        List<(MethodInfo Method, InterruptServiceEntry Entry)> services = attributes
            .Where(pair => pair.Attribute.SelectorValue is not null)
            .Select(pair => (pair.Method, EntryOf(pair.Attribute, pair.Method, type, selector)))
            .ToList();
        if (singles.Count == 1 && services.Count > 0) {
            throw new InvalidOperationException(
                $"{type.Name}: [InterruptService] cannot mix the parameterless form " +
                "with the selector form");
        }
        int duplicate = services
            .GroupBy(pair => pair.Entry.SelectorValue)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault(-1);
        if (duplicate != -1) {
            throw new InvalidOperationException(
                $"{type.Name}: selector value {duplicate} is declared more than once");
        }

        foreach (MethodInfo method in services.Select(pair => pair.Method)) {
            CheckShape(method, type.Name);
        }

        return new InterruptServiceDeclarations(
            singles.Count == 1 ? singles[0] : null,
            services);
    }

    private static InterruptServiceEntry EntryOf(InterruptServiceAttribute attribute,
        MethodInfo method, Type type, ServiceSelector? selector) {
        InterruptServiceEntry entry = attribute.ToEntry(method.Name, type.Name);
        if (attribute.Implementation is not { } implementationName) {
            if (attribute.Inputs is not null) {
                throw new InvalidOperationException(
                    $"{type.Name}.{entry.Name}: Inputs without an Implementation name no method parameters");
            }
            return entry;
        }
        IReadOnlyList<CpuValues> inputs = attribute.Inputs
            ?? ServiceCallSignature.Parameters(entry.Contract, selector).Select(parameter => parameter.Mask).ToArray();
        MethodInfo implementation = ResolveImplementation(type, entry, implementationName, inputs);
        return entry with { Implementation = implementation, Inputs = inputs };
    }

    /// <summary>The public instance method <paramref name="name"/> resolves to: each parameter
    /// type must be exactly the CLR type of its input binding, or an enum when the binding is a
    /// single integer register field (the numeric value is explicitly cast at the call site), and
    /// the return type must match the contract outputs of the entry (void, one scalar or a flat
    /// value tuple).
    /// </summary>
    private static MethodInfo ResolveImplementation(Type type, InterruptServiceEntry entry,
        string name, IReadOnlyList<CpuValues> inputs) {
        Type[] parameterTypes = inputs.Select(input => ParameterType(entry, input)).ToArray();
        List<MethodInfo> candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => candidate.Name == name
                && !candidate.IsGenericMethodDefinition
                && candidate.GetParameters().Length == parameterTypes.Length
                && candidate.GetParameters().Select(parameter => parameter.ParameterType)
                    .Zip(parameterTypes, (parameterType, bindingType) =>
                        parameterType == bindingType
                        || (parameterType.IsEnum && NumericRank(bindingType) > 0)
                        || WidensTo(bindingType, parameterType))
                    .All(match => match))
            .ToList();
        if (candidates.Count != 1) {
            throw new InvalidOperationException(
                $"{type.Name}.{entry.Name}: implementation {name}({string.Join(", ", parameterTypes.Select(t => t.Name))}) " +
                $"matched {candidates.Count} methods, exactly one required");
        }
        MethodInfo implementation = candidates[0];
        Type returnType = implementation.ReturnType;
        Type[] outputTypes = ServiceCallSignature.Outputs(entry.Contract)
            .Select(output => ClrTypeOf(entry, output.Type)).ToArray();
        bool returnMatches = outputTypes.Length switch {
            0 => returnType == typeof(void),
            1 => returnType == outputTypes[0],
            <= 7 => returnType.IsGenericType
                && returnType.Assembly == typeof(ValueTuple).Assembly
                && returnType.GetGenericTypeDefinition().Name
                    .StartsWith("ValueTuple`", StringComparison.Ordinal)
                && returnType.GetGenericArguments().SequenceEqual(outputTypes),
            _ => false
        };
        if (!returnMatches) {
            throw new InvalidOperationException(
                $"{type.Name}.{entry.Name}: implementation {name} returns {returnType.Name}, " +
                $"expected {(outputTypes.Length == 0 ? "void" : string.Join(", ", outputTypes.Select(t => t.Name)))}");
        }
        return implementation;
    }

    /// <summary>The CLR type the input binding must have: exactly one register field or flag,
    /// decomposed through <see cref="CpuValueParameters"/>, or <see cref="string"/> for the
    /// <c>DS:DX</c> binding of a service that takes a string argument.</summary>
    private static Type ParameterType(InterruptServiceEntry entry, CpuValues binding) {
        if (binding == (CpuValues.Ds | CpuValues.Dx)
            && entry.StringArgument is not ServiceStringArgument.None) {
            return typeof(string);
        }
        List<TypedParameter> parameters = CpuValueParameters.Decompose(binding);
        if (parameters.Count != 1 || parameters[0].Mask != binding) {
            throw new InvalidOperationException(
                $"{entry.Name}: input binding {binding} is not a single register field or flag");
        }
        return ClrTypeOf(entry, parameters[0].Type);
    }

    private static int NumericRank(Type type) => type switch {
        _ when type == typeof(byte) => 1,
        _ when type == typeof(ushort) => 2,
        _ when type == typeof(uint) => 3,
        _ => 0
    };

    private static bool WidensTo(Type bindingType, Type parameterType) {
        int from = NumericRank(bindingType);
        int to = NumericRank(parameterType);
        return from > 0 && to > from;
    }

    private static Type ClrTypeOf(InterruptServiceEntry entry, string type) => type switch {
        "byte" => typeof(byte),
        "ushort" => typeof(ushort),
        "uint" => typeof(uint),
        "bool" => typeof(bool),
        _ => throw new InvalidOperationException(
            $"{entry.Name}: no CLR type for the parameter type {type}")
    };

    private static void CheckShape(MethodInfo method, string typeName) {
        ParameterInfo[] parameters = method.GetParameters();
        bool valid = method.ReturnType == typeof(void) &&
            (parameters.Length == 0 ||
             (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool)));
        if (!valid) {
            throw new InvalidOperationException(
                $"{typeName}.{method.Name}: an interrupt service method must be " +
                "void M() or void M(bool calledFromVm)");
        }
    }
}
