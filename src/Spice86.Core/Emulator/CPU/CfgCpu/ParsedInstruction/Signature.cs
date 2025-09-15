namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Collections.Immutable;

/// <summary>
/// Base class for objects that have a signature value based on a list of bytes.
/// The bytes are used to compare 2 signatures.
/// If at the same position bytes are equal, the signatures are considered equals.
/// If there are null bytes they are not considered for the equality comparison of the position
/// If the length of the signature value differs, signatures are different
/// </summary>
public class Signature : IComparable<Signature> {
    public Signature(ImmutableList<byte?> signatureValue) {
        SignatureValue = signatureValue;
    }

    /// <summary>
    /// Value of the signature
    /// </summary>
    public ImmutableList<byte?> SignatureValue { get; private set; }

    public void NullifySignature() {
        SignatureValue = GenerateNullBytes(SignatureValue.Count);
    }
    
    private static ImmutableList<byte?> GenerateNullBytes(int size) {
        List<byte?> res = new List<byte?>();
        for (int i = 0; i < size; i++) {
            res.Add(null);
        }
        return ImmutableList.CreateRange(res);
    }

    /// <inheritdoc/>
    public int CompareTo(Signature? other) {
        if (other == null) {
            return 1;
        }

        int count = SignatureValue.Count;
        if (count != other.SignatureValue.Count) {
            return count.CompareTo(other.SignatureValue.Count);
        }
        // Size is equal, let's compare the elements of the signatures
        for (int i = 0; i < count; i++) {
            byte? thisByte = SignatureValue[i];
            byte? otherByte = other.SignatureValue[i];
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
    /// Checks that the given span of bytes is equivalent to the signature.
    /// Equivalence means that they have the same length and their content is identical at each position or null
    /// </summary>
    /// <param name="bytes">Span of bytes to compare with the signature</param>
    /// <returns>true if they are equivalent, false otherwise</returns>
    public bool ListEquivalent(IList<byte> bytes) {
        if (SignatureValue.Count != bytes.Count) {
            return false;
        }

        for (int i = 0; i < SignatureValue.Count; i++) {
            if (Differs(i, bytes[i])) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks that the given list of bytes is equivalent to the signature.
    /// Equivalence means that they have the same length and their content is identical at each position or null
    /// </summary>
    /// <param name="bytes">List of bytes to compare with the signature</param>
    /// <returns>true if they are equivalent, false otherwise</returns>
    public bool ListEquivalent(IList<byte?> bytes) {
        if (SignatureValue.Count != bytes.Count) {
            return false;
        }

        for (int i = 0; i < SignatureValue.Count; i++) {
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

        byte? d = SignatureValue[i];
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

        return ListEquivalent(((Signature)obj).SignatureValue);
    }

    /// <inheritdoc/>
    public override int GetHashCode() {
        // Hashcode cannot depend on the signature value because 2 values can be equals if they have null bytes 
        return 1;
    }

    /// <inheritdoc/>
    public override string ToString() {
        return string.Join(", ", SignatureValue);
    }
}