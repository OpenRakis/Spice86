namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Linq;

public class SignatureReducer {
    private readonly InstructionReplacerRegistry _replacerRegistry;

    public SignatureReducer(InstructionReplacerRegistry replacerRegistry) {
        _replacerRegistry = replacerRegistry;
    }

    private static Dictionary<Type, List<CfgInstruction>> GroupByType(List<CfgInstruction> instructions) {
        IEnumerable<IGrouping<Type, CfgInstruction>> grouped =
            instructions.GroupBy(i => i.GetType());
        return grouped.ToDictionary(
            g => g.Key,
            g => g.ToList()
        );
    }

    public CfgInstruction? ReduceToOne(CfgInstruction instruction1, CfgInstruction instruction2) {
        IList<CfgInstruction> reducedInstructions = ReduceAll([instruction1, instruction2]);
        if (reducedInstructions.Count == 1) {
            return reducedInstructions[0];
        }
        return null;
    }

    public IList<CfgInstruction> ReduceAll(List<CfgInstruction> instructions) {
        IDictionary<Type, List<CfgInstruction>> groupByType =
            GroupByType(instructions);
        List<CfgInstruction> res = new();
        foreach (KeyValuePair<Type, List<CfgInstruction>> entry in groupByType) {
            res.AddRange(ReduceAllWithSameType(entry.Value));
        }

        return res;
    }

    private List<CfgInstruction> ReduceAllWithSameType(IList<CfgInstruction> instructions) {
        // Group by a signature composed of only final fields.
        // Each list here is reducible since diverging fields are non final.
        IDictionary<Signature, List<CfgInstruction>> groupedBySignatureFinal =
            GroupBySignatureWithOnlyFinal(instructions);
        List<CfgInstruction> res = new();
        foreach (KeyValuePair<Signature, List<CfgInstruction>> entry in groupedBySignatureFinal) {
            res.Add(ReduceAllWithSameSignatureFinal(entry.Value));
        }
        return res;
    }


    private static Dictionary<Signature, List<CfgInstruction>> GroupBySignatureWithOnlyFinal(
        IList<CfgInstruction> instructions) {
        IEnumerable<IGrouping<Signature, CfgInstruction>> grouped =
            instructions.GroupBy(i => i.SignatureFinal);
        return grouped.ToDictionary(
            g => g.Key,
            g => g.ToList()
        );
    }

    private CfgInstruction ReduceAllWithSameSignatureFinal(IList<CfgInstruction> instructions) {
        // The instructions in input all have the final fields and the same grammar
        // So they are all functionally the same except for some value fields that are changing.
        // We keep the first one and get rid of the other, and we determine in the one we keep which fields are changing.
        CfgInstruction res = instructions.First();
        ReduceNonFinalFields(res, instructions);
        // unlink other instructions and make the graph point to new instruction. Make sure other instructions are not in the caches.
        ReplaceWithReference(res, instructions);
        return res;
    }

    private void ReduceNonFinalFields(CfgInstruction reference, IList<CfgInstruction> instructions) {
        // Reference instruction is the result.
        // Goal is to check in the list which fields differ, and if so make signature of result null and tell it to use the value
        for (int i = 0; i < reference.FieldsInOrder.Count; i++) {
            FieldWithValue referenceField = reference.FieldsInOrder[i];
            bool same = IsFieldValueSame(referenceField, instructions, i);
            // If value is the same across the whole list of instructions it means we can keep using it.
            if (!same) {
                // Can't use direct value, need to refer to what is in memory since it may have changed between now and parsing time.
                referenceField.UseValue = false;
                referenceField.NullifySignature();
            }
        }
    }


    private bool IsFieldValueSame(FieldWithValue reference, IList<CfgInstruction> instructions, int fieldIndex) {
        foreach (CfgInstruction instruction in instructions) {
            FieldWithValue instructionField = instruction.FieldsInOrder[fieldIndex];
            if (!instructionField.IsValueAndPositionEquals(reference)) {
                return false;
            }
        }

        return true;
    }

    private void ReplaceWithReference(CfgInstruction reference, IList<CfgInstruction> instructions) {
        foreach (CfgInstruction instruction in instructions) {
            if (instruction == reference) {
                continue;
            }

            _replacerRegistry.ReplaceInstruction(instruction, reference);
        }
    }

}