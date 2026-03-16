namespace Spice86.Core.Emulator.Http;

/// <summary>
/// Endpoint configuration for the built-in HTTP API.
/// </summary>
public static class HttpApiEndpoint {
    /// <summary>Loopback address on which the HTTP API listens.</summary>
    public const string Host = "127.0.0.1";

    /// <summary>Default TCP port used by the HTTP API server.</summary>
    public const int DefaultPort = 10001;

    /// <summary>Maximum number of bytes that can be returned in a single range request.</summary>
    public const int MaxRangeLength = 65536;

    /// <summary>Returns the full base URL for the given port.</summary>
    /// <param name="port">TCP port number.</param>
    /// <returns>Base URL string, e.g. <c>http://127.0.0.1:10001</c>.</returns>
    public static string BaseUrl(int port) => $"http://{Host}:{port}";
}
