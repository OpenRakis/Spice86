namespace Spice86.Tests.Emulator.Gdb;

using System.IO.Pipes;
using System.Text;

/// <summary>
/// Entry point for the GDB client process that runs separately and communicates via named pipes.
/// This is invoked when the test assembly is run with --gdb-client argument.
/// </summary>
public static class GdbClientProcessMain {
    /// <summary>
    /// Main entry point for the GDB client process.
    /// Args: --gdb-client {host} {port} {pipeName}
    /// </summary>
    public static async Task<int> RunGdbClientAsync(string[] args) {
        if (args.Length != 4 || args[0] != "--gdb-client") {
            return 1;
        }

        string host = args[1];
        int port = int.Parse(args[2]);
        string pipeName = args[3];

        try {
            // Connect to the named pipe
            using NamedPipeClientStream pipeClient = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipeClient.ConnectAsync(5000);

            // Connect to GDB server
            using GdbClient gdbClient = new();
            await gdbClient.ConnectAsync(host, port);

            // Communication loop
            while (pipeClient.IsConnected) {
                // Read command from pipe
                byte[] lengthBuffer = new byte[4];
                int bytesRead = await pipeClient.ReadAsync(lengthBuffer, 0, 4);
                if (bytesRead != 4) break;

                int commandLength = BitConverter.ToInt32(lengthBuffer, 0);
                byte[] commandBytes = new byte[commandLength];
                
                int totalRead = 0;
                while (totalRead < commandLength) {
                    bytesRead = await pipeClient.ReadAsync(commandBytes, totalRead, commandLength - totalRead);
                    if (bytesRead == 0) break;
                    totalRead += bytesRead;
                }

                if (totalRead != commandLength) break;

                string command = Encoding.UTF8.GetString(commandBytes);

                // Send command to GDB server and get response
                string response;
                try {
                    response = await gdbClient.SendCommandAsync(command);
                } catch (Exception ex) {
                    response = $"ERROR: {ex.Message}";
                }

                // Send response back through pipe
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                byte[] responseLengthBytes = BitConverter.GetBytes(responseBytes.Length);
                
                await pipeClient.WriteAsync(responseLengthBytes, 0, responseLengthBytes.Length);
                await pipeClient.WriteAsync(responseBytes, 0, responseBytes.Length);
                await pipeClient.FlushAsync();
            }

            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine($"GDB client process error: {ex}");
            return 1;
        }
    }
}
