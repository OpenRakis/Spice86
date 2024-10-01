namespace Spice86.Messages;

public record StatusMessage(DateTime Time, object Origin, string Message);