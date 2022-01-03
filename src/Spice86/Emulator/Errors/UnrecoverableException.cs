namespace Spice86.Emulator.Errors;

using System;


[Serializable]
public class UnrecoverableException : Exception
{
    public UnrecoverableException() { }
    public UnrecoverableException(string message) : base(message) { }
    public UnrecoverableException(string message, Exception inner) : base(message, inner) { }
    protected UnrecoverableException(
      System.Runtime.Serialization.SerializationInfo info,
      System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
