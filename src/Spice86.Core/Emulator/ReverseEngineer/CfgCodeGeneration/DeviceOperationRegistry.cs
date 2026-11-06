namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;

using System.Linq;
using System.Reflection;

/// <summary>
/// The device operations <see cref="PortSequenceStepAttribute"/> declares on the methods of the
/// Spice86.Core assembly, validated once and indexed by device type and first step. The accessible
/// <c>CSharpOverrideHelper</c> properties whose type is an <see cref="IIOPortHandler"/>
/// implementation resolve each declaration's receiver and give the printer the argument types of
/// every device call, PIT and OPL included.
/// </summary>
internal static class DeviceOperationRegistry {
    /// <summary>One validated device operation: the helper property it is reached through, the
    /// device method to call and its ordered steps.</summary>
    internal sealed record Declaration(PropertyInfo Property, MethodInfo Method,
        IReadOnlyList<PortSequenceStepAttribute> Steps);

    private static readonly PropertyInfo[] Receivers;
    private static readonly Dictionary<(Type Device, ushort Port, PortDirection Direction),
        List<Declaration>> ByStart;

    static DeviceOperationRegistry() {
        Receivers = [.. typeof(CSharpOverrideHelper).GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0
                && property.GetMethod is { } getter
                && (getter.IsPublic || getter.IsFamily || getter.IsFamilyOrAssembly)
                && typeof(IIOPortHandler).IsAssignableFrom(property.PropertyType))];
        ByStart = [];
        foreach (Type type in typeof(PortSequenceStepAttribute).Assembly.GetTypes()) {
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                PortSequenceStepAttribute[] steps = [.. method
                    .GetCustomAttributes<PortSequenceStepAttribute>()
                    .OrderBy(step => step.Order)];
                if (steps.Length == 0) {
                    continue;
                }
                Declaration declaration = Validate(method, steps);
                (Type Device, ushort Port, PortDirection Direction) key =
                    (type, steps[0].Port, steps[0].Direction);
                if (!ByStart.TryGetValue(key, out List<Declaration>? bucket)) {
                    bucket = [];
                    ByStart[key] = bucket;
                }
                RejectShapeOverlap(method, steps, bucket);
                bucket.Add(declaration);
            }
        }
    }

    /// <summary>The declarations whose sequence starts with a <paramref name="direction"/> access
    /// of device <paramref name="deviceType"/> on <paramref name="port"/>.</summary>
    internal static IReadOnlyList<Declaration> Find(Type deviceType, ushort port,
        PortDirection direction) {
        return ByStart.TryGetValue((deviceType, port, direction), out List<Declaration>? found) ? found : [];
    }

    /// <summary>The device type the helper property <paramref name="propertyName"/> exposes, or
    /// <see langword="null"/> when the property is not a device receiver.</summary>
    internal static Type? ReceiverType(string? propertyName) =>
        Receivers.FirstOrDefault(property => property.Name == propertyName)?.PropertyType;

    private static Declaration Validate(MethodInfo method, PortSequenceStepAttribute[] steps) {
        string Name() => $"{method.DeclaringType?.Name}.{method.Name}";
        InvalidOperationException Error(string message, int order) =>
            new($"{Name()}: {message} (step {order}).");
        if (!method.IsPublic || method.IsStatic || method.IsGenericMethodDefinition) {
            throw Error("a device operation must be a public, non-generic instance method", -1);
        }
        if (steps.Length < 2) {
            throw Error("a port sequence declares at least two steps", steps[0].Order);
        }
        for (int i = 0; i < steps.Length; i++) {
            if (steps[i].Order != i) {
                throw Error("step orders must be unique, contiguous and start at zero",
                    steps[i].Order);
            }
        }
        ParameterInfo[] parameters = method.GetParameters();
        foreach (PortSequenceStepAttribute step in steps.Where(step => step.Direction == PortDirection.Out)) {
            ParameterInfo? bound = parameters.FirstOrDefault(
                parameter => parameter.Name == step.ParameterName);
            if (bound is null || bound.ParameterType != typeof(byte)
                || bound.ParameterType.IsByRef) {
                throw Error(
                    $"a write step binds an existing by-value byte parameter, not '{step.ParameterName}'",
                    step.Order);
            }
            if (steps.Count(other => other.ParameterName == step.ParameterName) != 1) {
                throw Error($"parameter '{step.ParameterName}' is bound twice", step.Order);
            }
        }
        if (parameters.Length != steps.Count(step => step.Direction == PortDirection.Out)) {
            throw Error("every parameter must be bound by exactly one write step", -1);
        }
        int reads = steps.Count(step => step.Direction == PortDirection.In);
        if (reads == 0) {
            if (method.ReturnType != typeof(void)) {
                throw Error("an all-write sequence returns void", -1);
            }
        } else if (reads != 1 || steps[^1].Direction != PortDirection.In) {
            throw Error("a sequence has at most one read step, at the end",
                steps.First(step => step.Direction == PortDirection.In).Order);
        } else if (method.ReturnType != typeof(byte)) {
            throw Error("a sequence ending on a read returns byte", -1);
        }
        PropertyInfo[] receivers = [.. Receivers.Where(
            property => property.PropertyType == method.DeclaringType)];
        if (receivers.Length != 1) {
            throw Error(
                $"{Name()} needs exactly one accessible CSharpOverrideHelper property of its declaring type, found {receivers.Length}",
                -1);
        }
        return new Declaration(receivers[0], method, steps);
    }

    private static void RejectShapeOverlap(MethodInfo method, PortSequenceStepAttribute[] steps,
        List<Declaration> bucket) {
        foreach (Declaration existing in bucket) {
            int shared = Math.Min(steps.Length, existing.Steps.Count);
            bool samePrefix = Enumerable.Range(0, shared).All(i =>
                steps[i].Port == existing.Steps[i].Port
                && steps[i].Direction == existing.Steps[i].Direction);
            if (samePrefix) {
                throw new InvalidOperationException(
                    $"{method.DeclaringType?.Name}.{method.Name} duplicates or prefixes the sequence of {existing.Method.DeclaringType?.Name}.{existing.Method.Name}.");
            }
        }
    }
}
