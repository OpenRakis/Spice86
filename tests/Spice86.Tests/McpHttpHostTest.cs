namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.Mcp;

using System.IO;
using System.Net;
using System.Net.Sockets;

using Xunit;

public class McpHttpHostTest {
    [Fact]
    public void Start_ThrowsIOException_WhenPortIsOccupied() {
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection spice86 = creator.Create();
        EmulatorMcpServices services = spice86.McpServices;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        McpHttpHost host = new(services.LoggerService);
        Action act = () => host.Start(services, port);

        act.Should().Throw<IOException>();
        host.Stop();
    }
}
