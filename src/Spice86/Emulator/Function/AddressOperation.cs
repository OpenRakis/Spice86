namespace Spice86.Emulator.Function;

using System;
using System.Collections.Generic;

/// <summary>
/// Describes an operation done on a value stored at an address. <br /> Operation can be 8/16/32bit
/// read / write
/// </summary>
public class AddressOperation : IComparable<AddressOperation> {
    private static readonly IComparer<AddressOperation> _naturalOrderComparator = new NaturalOrderComparator();

    private readonly OperandSize _operandSize;

    private readonly ValueOperation _valueOperation;

    public AddressOperation(ValueOperation valueOperation, OperandSize operandSize) {
        this._valueOperation = valueOperation;
        this._operandSize = operandSize;
    }

    public int CompareTo(AddressOperation? other) {
        return _naturalOrderComparator.Compare(this, other);
    }

    public override bool Equals(object? obj) {
        if (this == obj) {
            return true;
        }
        if (obj is not AddressOperation other) {
            return false;
        }
        return _operandSize == other._operandSize
            && _valueOperation == other._valueOperation;
    }

    public override int GetHashCode() {
        return _operandSize.Name.Ordinal() << 2 | _valueOperation.Ordinal();
    }

    public OperandSize GetOperandSize() {
        return _operandSize;
    }

    public ValueOperation GetValueOperation() {
        return _valueOperation;
    }

    private class NaturalOrderComparator : IComparer<AddressOperation> {

        public int Compare(AddressOperation? x, AddressOperation? y) {
            int? resNullable = x?._operandSize.Name.CompareTo(y?._operandSize.Name);

            // from Comparator.java, thenTo method
            resNullable = (resNullable != 0) ? resNullable : x?._valueOperation.CompareTo(y?._valueOperation);

            if (resNullable is null) {
                if (x is null && y is null) {
                    return 0;
                } else if (x is null && y is not null) {
                    return -1;
                } else {
                    return 1;
                }
            } else {
                return resNullable.Value;
            }
        }
    }
}