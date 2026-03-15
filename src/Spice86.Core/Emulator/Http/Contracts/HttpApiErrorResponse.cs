namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Error payload returned by the HTTP API.
/// </summary>
/// <param name="Message">Human-readable description of the error.</param>
public sealed record HttpApiErrorResponse(string Message);
