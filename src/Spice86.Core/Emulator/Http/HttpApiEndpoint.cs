namespace Spice86.Core.Emulator.Http;

/// <summary>
/// Fixed endpoint configuration for the built-in HTTP API.
/// </summary>
public static class HttpApiEndpoint {
    public const string Host = "127.0.0.1";
    public const int Port = 10001;
    public static string BaseUrl => $"http://{Host}:{Port}";
}
