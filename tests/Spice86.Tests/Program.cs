// Entry point for Spice86.Tests assembly
// Handles --gdb-client mode for separate process GDB testing

if (args.Length > 0 && args[0] == "--gdb-client") {
    return await Spice86.Tests.Emulator.Gdb.GdbClientProcessMain.RunGdbClientAsync(args);
}

// Normal test execution
return 0;
