namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// API root payload.
/// </summary>
/// <param name="Name">Display name of the API.</param>
/// <param name="Version">API version string.</param>
/// <param name="Endpoints">List of available endpoint paths.</param>
public sealed record HttpApiInfoResponse(string Name, string Version, IReadOnlyList<string> Endpoints);
