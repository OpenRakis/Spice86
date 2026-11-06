namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

/// <summary>
/// Table between interrupt handler types that have typed service helpers and the name of the
/// <see cref="CSharpOverrideHelper"/> property holding the group, so generated code can
/// print <c>{propertyName}.{serviceName}(...)</c> for a service. Derived from the public
/// instance group properties of <see cref="CSharpOverrideHelper"/>: the generic base
/// argument of a group's type is the handler type it serves.
/// </summary>
public static class InterruptServiceGroups {
    private static readonly IReadOnlyDictionary<Type, string> PropertyNames = BuildPropertyNames();

    private static IReadOnlyDictionary<Type, string> BuildPropertyNames() {
        Dictionary<Type, string> names = new();
        foreach (PropertyInfo property in typeof(CSharpOverrideHelper)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            Type? handlerType = GroupHandlerType(property.PropertyType);
            if (handlerType is not null) {
                names[handlerType] = property.Name;
            }
        }
        return names;
    }

    private static Type? GroupHandlerType(Type groupType) =>
        groupType.BaseType is { IsGenericType: true } baseType
        && baseType.GetGenericTypeDefinition() == typeof(InterruptServiceGroup<>)
            ? baseType.GetGenericArguments()[0]
            : null;

    /// <summary>
    /// Returns the name of the <see cref="CSharpOverrideHelper"/> property that holds the
    /// typed service group of <paramref name="handlerType"/>.
    /// </summary>
    /// <param name="handlerType">The concrete handler type.</param>
    /// <param name="propertyName">The property name when the handler has a service group.</param>
    /// <returns><c>false</c> when the handler type has no typed service group.</returns>
    public static bool TryGetPropertyName(Type handlerType,
        [NotNullWhen(true)] out string? propertyName) {
        return PropertyNames.TryGetValue(handlerType, out propertyName);
    }
}
