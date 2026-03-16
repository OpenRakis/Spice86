namespace Spice86.Tests.CpuTests.SingleStepTests;

/// <summary>
/// Thrown when test json parsing fails, should never happen but nothing is perfect :)
/// </summary>
public class InvalidTestException : Exception {
    public InvalidTestException() : base() { }
    public InvalidTestException(string message) : base(message) { }
    public InvalidTestException(string message, Exception innerException) : base(message, innerException) { }
}
