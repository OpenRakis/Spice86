namespace Spice86.Models.Debugging;
public record ExceptionInfo(string? TargetSite, string Message, string? StackTrace);