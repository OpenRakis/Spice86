namespace Spice86.Core.Emulator.StateSerialization;

/// <summary>
/// This class is responsible for serializing and deserializing  the emulator state to a directory.
/// </summary>
public class EmulatorStateSerializer {
    public EmulatorStateSerializer(
        EmulatorStateSerializationFolder emulatorStateSerializationFolder,
        EmulationStateDataReader emulationStateDataReader,
        EmulationStateDataWriter emulationStateDataWriter) {
        EmulationStateDataReader = emulationStateDataReader;
        EmulationStateDataWriter = emulationStateDataWriter;
        EmulatorStateSerializationFolder = emulatorStateSerializationFolder;
    }

    public EmulationStateDataReader EmulationStateDataReader { get; }
    public EmulationStateDataWriter EmulationStateDataWriter { get; }
    public EmulatorStateSerializationFolder EmulatorStateSerializationFolder { get; }
}