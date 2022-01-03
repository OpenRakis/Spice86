namespace Spice86;

using System;

public static class EnumExtenions
{
    public static int Ordinal(this Enum instance)
    {
        return Array.IndexOf(Enum.GetValues(instance.GetType()), instance);
    }
}
