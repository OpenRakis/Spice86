namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing;

public class InvalidTestException : Exception {
    public InvalidTestException() { }
    public InvalidTestException(string message) : base(message) { }
    public InvalidTestException(string message, Exception inner) : base(message, inner) { }
}