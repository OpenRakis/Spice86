namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// API root payload.
/// </summary>
public sealed record HttpApiInfoResponse(string Name, string Version, IReadOnlyList<string> Endpoints);
