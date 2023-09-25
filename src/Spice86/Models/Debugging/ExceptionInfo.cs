namespace Spice86.Models.Debugging;

using System.Reflection;

public record ExceptionInfo(string? TargetSite, string Message, string? StackTrace);