namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Error payload returned by the HTTP API.
/// </summary>
public sealed record HttpApiErrorResponse(string Message);
