namespace Spice86.Core.CLI.RuntimeOptions;

/// <summary>
/// Options that control the lifecycle and endpoint selection of the GDB remote debugging server.
/// </summary>
/// <param name="Port">TCP port to listen on. A value of <c>0</c> means the server is disabled.</param>
public sealed record class GdbServerOptions(int Port);