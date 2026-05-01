// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

#nullable disable

using System;
using System.Reflection;
using Avalonia.Data;

namespace Avalonia.Controls.Utils
{
    internal static class BindingCompatibilityHelper
    {
        public static object GetConverter(object binding)
        {
            return GetPropertyValue(binding, "Converter");
        }

        public static BindingMode GetMode(object binding)
        {
            object value = GetPropertyValue(binding, "Mode");
            return value is BindingMode bindingMode ? bindingMode : BindingMode.Default;
        }

        public static string GetPath(object binding)
        {
            object value = GetPropertyValue(binding, "Path");
            return value?.ToString();
        }

        public static string GetStringFormat(object binding)
        {
            return GetPropertyValue(binding, "StringFormat") as string;
        }

        public static void SetConverter(object binding, object converter)
        {
            SetPropertyValue(binding, "Converter", converter);
        }

        public static void SetMode(object binding, BindingMode mode)
        {
            SetPropertyValue(binding, "Mode", mode);
        }

        public static void UpdateSource(BindingExpressionBase bindingExpression)
        {
            MethodInfo updateSource = bindingExpression.GetType().GetMethod("UpdateSource", BindingFlags.Instance | BindingFlags.Public);
            if (updateSource != null)
            {
                updateSource.Invoke(bindingExpression, Array.Empty<object>());
            }
        }

        private static object GetPropertyValue(object instance, string propertyName)
        {
            if (instance == null)
            {
                return null;
            }

            PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(instance);
        }

        private static void SetPropertyValue(object instance, string propertyName, object value)
        {
            if (instance == null)
            {
                return;
            }

            PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value);
            }
        }
    }
}