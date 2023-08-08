namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Linq;

public class DiscriminatorReducer {
    private readonly IList<IInstructionReplacer<CfgInstruction>> _instructionReplacers;

    public DiscriminatorReducer(IList<IInstructionReplacer<CfgInstruction>> instructionReplacers) {
        _instructionReplacers = instructionReplacers;
    }

    public IList<CfgInstruction> ReduceAll(List<CfgInstruction> instructions) {
        IDictionary<Discriminator, List<CfgInstruction>> groupedByDiscriminator =
            GroupByDiscriminator(instructions);
        List<CfgInstruction> res = new();
        foreach (var entry in groupedByDiscriminator) {
            res.Add(ReduceAllWithSameDiscriminator(entry.Value));
        }

        return res;
    }

    private CfgInstruction ReduceAllWithSameDiscriminator(IList<CfgInstruction> instructions) {
        // The instructions in input all have the same discriminator
        // So they are all functionally the same except for some fields that are changing.
        // We keep the first one and get rid of the other.
        CfgInstruction res = instructions.First();
        ReduceFieldValues(res, instructions);
        // unlink other instructions and make the graph point to new instruction. Make sure other instructions are not in the caches.
        ReplaceWithReference(res, instructions);
        return res;
    }

    private void ReduceFieldValues(CfgInstruction reference, IList<CfgInstruction> instructions) {
        for (int i = 0; i < reference.FieldsInOrder.Count; i++) {
            FieldWithValue referenceField = reference.FieldsInOrder[i];
            bool same = IsFieldValueSame(referenceField, instructions, i);
            // If value is the same across the whole list of instructions it means we can keep using it.
            if (!same) {
                // Can't use direct value, need to refer to what is in memory since it may have changed between now and parsing time.
                referenceField.UseValue = false;
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

            foreach (var instructionReplacer in _instructionReplacers) {
                instructionReplacer.ReplaceInstruction(instruction, reference);
            }
        }
    }

    private IDictionary<Discriminator, List<CfgInstruction>> GroupByDiscriminator(
        List<CfgInstruction> instructions) {
        IEnumerable<IGrouping<Discriminator, CfgInstruction>> grouped =
            instructions.GroupBy(i => i.Discriminator);
        return grouped.ToDictionary(
            g => g.Key, 
            g => g.ToList()
        );
    }
}