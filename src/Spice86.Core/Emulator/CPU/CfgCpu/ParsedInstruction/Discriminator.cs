namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Collections.Immutable;

/// <summary>
/// Base class for objects that have a discriminator value based on a list of bytes.
/// The bytes are used to compare 2 discriminators.
/// If at the same position bytes are equal, the discriminators are considered equals.
/// If there are null bytes they are not considered for the equality comparison of the position
/// If the length of the discriminator value differs, discriminators are different
/// </summary>
public class Discriminator : IComparable<Discriminator> {
    public Discriminator(ImmutableList<byte?> discriminatorValue) {
        DiscriminatorValue = discriminatorValue;
    }

    /// <summary>
    /// Value of the discriminator
    /// </summary>
    public ImmutableList<byte?> DiscriminatorValue { get; private set; }

    public void NullifyDiscriminator() {
        DiscriminatorValue = GenerateNullBytes(DiscriminatorValue.Count);
    }
    
    private static ImmutableList<byte?> GenerateNullBytes(int size) {
        List<byte?> res = new List<byte?>();
        for (int i = 0; i < size; i++) {
            res.Add(null);
        }
        return ImmutableList.CreateRange(res);
    }

    /// <inheritdoc/>
    public int CompareTo(Discriminator? other) {
        if (other == null) {
            return 1;
        }

        int count = DiscriminatorValue.Count;
        if (count != other.DiscriminatorValue.Count) {
            return count.CompareTo(other.DiscriminatorValue.Count);
        }
        // Size is equal, let's compare the elements of the discriminators
        for (int i = 0; i < count; i++) {
            byte? thisByte = DiscriminatorValue[i];
            byte? otherByte = other.DiscriminatorValue[i];
            if (thisByte != otherByte) {
                // null is equals here
                if (thisByte == null || otherByte == null) {
                    continue;
                }
                
                return thisByte.Value.CompareTo(otherByte.Value);
            }
        }
        // Equals
        return 0;
    }
    
    /// <summary>
    /// Checks that the given span of bytes is equivalent to the discriminator.
    /// Equivalence means that they have the same length and their content is identical at each position or null
    /// </summary>
    /// <param name="bytes">Span of bytes to compare with the discriminator</param>
    /// <returns>true if they are equivalent, false otherwise</returns>
    public bool SpanEquivalent(Span<byte> bytes) {
        if (DiscriminatorValue.Count != bytes.Length) {
            return false;
        }

        for (int i = 0; i < DiscriminatorValue.Count; i++) {
            if (Differs(i, bytes[i])) {
                return false;
            }
        }

        return true;
    }
    
    /// <summary>
    /// Checks that the given list of bytes is equivalent to the discriminator.
    /// Equivalence means that they have the same length and their content is identical at each position or null
    /// </summary>
    /// <param name="bytes">List of bytes to compare with the discriminator</param>
    /// <returns>true if they are equivalent, false otherwise</returns>
    public bool ListEquivalent(IList<byte?> bytes) {
        if (DiscriminatorValue.Count != bytes.Count) {
            return false;
        }

        for (int i = 0; i < DiscriminatorValue.Count; i++) {
            if (Differs(i, bytes[i])) {
                return false;
            }
        }

        return true;
    }

    private bool Differs(int i, byte? b) {
        // Null is considered the same as us
        if (b is null) {
            return false;
        }

        byte? d = DiscriminatorValue[i];
        // Null is considered the same regardless of other
        if (d is null) {
            return false;
        }

        if (d != b) {
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) {
        if (ReferenceEquals(null, obj)) {
            return false;
        }

        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj.GetType() != this.GetType()) {
            return false;
        }

        return ListEquivalent(((Discriminator)obj).DiscriminatorValue);
    }

    /// <inheritdoc/>
    public override int GetHashCode() {
        // Hashcode cannot depend on the discriminator value because 2 values can be equals if they have null bytes 
        return 1;
    }

    /// <inheritdoc/>
    public override string ToString() {
        return string.Join(", ", DiscriminatorValue);
    }
}