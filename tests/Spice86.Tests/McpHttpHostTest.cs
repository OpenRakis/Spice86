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

    [Fact]
    public void Stop_Completes_WhenCallerSynchronizationContextIsBlocked() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection spice86 = creator.Create();
        EmulatorMcpServices services = spice86.McpServices;
        McpHttpHost host = new(services.LoggerService);
        host.Start(services, 0);

        // Act: stop from a thread whose synchronization context drops posted callbacks,
        // like a UI thread blocked in a synchronous wait.
        bool stopped = false;
        Thread thread = new(() => {
            SynchronizationContext.SetSynchronizationContext(new BlockedSynchronizationContext());
            host.Stop();
            stopped = true;
        }) { IsBackground = true };
        thread.Start();
        bool joined = thread.Join(TimeSpan.FromSeconds(10));

        // Assert
        joined.Should().BeTrue("Stop must not depend on the caller's synchronization context");
        stopped.Should().BeTrue();
    }

    private sealed class BlockedSynchronizationContext : SynchronizationContext {
        public override void Post(SendOrPostCallback d, object? state) {
        }
    }
}
