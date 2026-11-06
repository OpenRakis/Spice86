namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

/// <summary>
/// Reads the <see cref="InterruptServiceAttribute"/> declarations of a handler type,
/// walking the type hierarchy up to (not including) <see cref="object"/>.
/// </summary>
public static class InterruptServiceAttributes {
    private const BindingFlags MethodFlags = BindingFlags.Instance |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<Type, InterruptServiceDeclarations> Cache = new();

    /// <summary>Returns the service declarations of <paramref name="type"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The type mixes the two attribute forms, declares two single contracts, uses the
    /// same selector value twice, combines <c>Unknown</c> with other properties, or
    /// annotates a method that is not <c>void M()</c> or <c>void M(bool)</c>.
    /// </exception>
    public static InterruptServiceDeclarations Of(Type type) => Cache.GetOrAdd(type, Read);

    private static InterruptServiceDeclarations Read(Type type) {
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
            .Select(pair => (pair.Method, pair.Attribute.ToEntry(
                pair.Method.Name, type.Name)))
            .ToList();
        if (singles.Count > 1) {
            throw new InvalidOperationException(
                $"{type.Name}: more than one parameterless [InterruptService] " +
                $"({singles[0].Method.Name}, {singles[1].Method.Name})");
        }

        List<(MethodInfo Method, InterruptServiceEntry Entry)> services = attributes
            .Where(pair => pair.Attribute.SelectorValue is not null)
            .Select(pair => (pair.Method, pair.Attribute.ToEntry(
                pair.Method.Name, type.Name)))
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
