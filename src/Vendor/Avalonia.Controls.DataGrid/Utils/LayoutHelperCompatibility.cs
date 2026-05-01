// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

#nullable disable

using System;
using System.Reflection;
using Avalonia.Layout;

namespace Avalonia.Controls.Utils
{
    internal static class LayoutHelperCompatibility
    {
        public static Size ApplyLayoutConstraints(Layoutable control, Size size)
        {
            MethodInfo applyLayoutConstraints = typeof(LayoutHelper).GetMethod("ApplyLayoutConstraints", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Layoutable), typeof(Size) }, null);
            if (applyLayoutConstraints != null)
            {
                return (Size)applyLayoutConstraints.Invoke(null, new object[] { control, size });
            }

            return size;
        }

        public static Size RoundLayoutSizeUp(Size size, double dpiScale)
        {
            MethodInfo twoParameterOverload = typeof(LayoutHelper).GetMethod(nameof(LayoutHelper.RoundLayoutSizeUp), new[] { typeof(Size), typeof(double) });
            if (twoParameterOverload != null)
            {
                return (Size)twoParameterOverload.Invoke(null, new object[] { size, dpiScale });
            }

            MethodInfo threeParameterOverload = typeof(LayoutHelper).GetMethod(nameof(LayoutHelper.RoundLayoutSizeUp), new[] { typeof(Size), typeof(double), typeof(double) });
            if (threeParameterOverload != null)
            {
                return (Size)threeParameterOverload.Invoke(null, new object[] { size, dpiScale, dpiScale });
            }

            throw new MissingMethodException(typeof(LayoutHelper).FullName, nameof(LayoutHelper.RoundLayoutSizeUp));
        }
    }
}