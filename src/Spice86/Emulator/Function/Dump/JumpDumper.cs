namespace Spice86.Emulator.Function.Dump;

using Memory;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public class JumpDumper {
    private JumpHandler _jumpHandler;
    public JumpDumper(JumpHandler jumpHandler) {
        _jumpHandler = jumpHandler;
    }

    public void Dump(string destinationFilePath) {
        using StreamWriter printWriter = new StreamWriter(destinationFilePath);
        string jsonString = JsonSerializer.Serialize(new JumpHandlerJson(_jumpHandler));
        printWriter.WriteLine(jsonString);
    }
}

class JumpHandlerJson {
    public IDictionary<uint, List<uint>> CallsFromTo { get; }
    public IDictionary<uint, List<uint>> JumpsFromTo { get; }
    public IDictionary<uint, List<uint>> RetsFromTo { get; }

    public JumpHandlerJson(JumpHandler jumpHandler) {
        CallsFromTo = ToJsonDictionary(jumpHandler.CallsFromTo);
        JumpsFromTo = ToJsonDictionary(jumpHandler.JumpsFromTo);
        RetsFromTo = ToJsonDictionary(jumpHandler.RetsFromTo);
    }
    private IDictionary<uint, List<uint>> ToJsonDictionary(IDictionary<SegmentedAddress, ISet<SegmentedAddress>> fromTo) {
        IDictionary<uint, List<uint>> res = new Dictionary<uint, List<uint>>();
        foreach (KeyValuePair<SegmentedAddress, ISet<SegmentedAddress>> entry in fromTo) {
            uint physicalFrom = entry.Key.ToPhysical();
            List<uint> toList = entry.Value.Select(address => address.ToPhysical()).OrderBy(x => x).ToList();
            res.Add(physicalFrom, toList);
        }
        return res;
    }
}