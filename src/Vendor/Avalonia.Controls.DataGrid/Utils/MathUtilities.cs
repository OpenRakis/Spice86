// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

using System;

namespace Avalonia.Utilities;

internal static class MathUtilities
{
    private const double Epsilon = 0.000001;

    public static bool AreClose(double left, double right)
    {
        if (left == right)
        {
            return true;
        }

        double delta = left - right;
        return delta < Epsilon && delta > -Epsilon;
    }

    public static bool GreaterThan(double left, double right)
    {
        return left > right && !AreClose(left, right);
    }

    public static bool GreaterThanOrClose(double left, double right)
    {
        return left > right || AreClose(left, right);
    }

    public static bool IsZero(double value)
    {
        return value < Epsilon && value > -Epsilon;
    }

    public static bool LessThan(double left, double right)
    {
        return left < right && !AreClose(left, right);
    }

    public static bool LessThanOrClose(double left, double right)
    {
        return left < right || AreClose(left, right);
    }
}