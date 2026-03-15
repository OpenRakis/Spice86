namespace Spice86.Core.Emulator.Http;

/// <summary>
/// Endpoint configuration for the built-in HTTP API.
/// </summary>
public static class HttpApiEndpoint {
    public const string Host = "127.0.0.1";
    public const int DefaultPort = 10001;
    public static string BaseUrl(int port) => $"http://{Host}:{port}";
}
