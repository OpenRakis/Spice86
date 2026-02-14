// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Defines how stereo channels map to output lines.
/// </summary>
public struct StereoLine {
    public LineIndex Left;
    public LineIndex Right;

    public static readonly StereoLine StereoMap = new() { Left = LineIndex.Left, Right = LineIndex.Right };
    public static readonly StereoLine ReverseMap = new() { Left = LineIndex.Right, Right = LineIndex.Left };

    public readonly bool Equals(StereoLine other) {
        return Left == other.Left && Right == other.Right;
    }

    public override readonly bool Equals(object? obj) {
        return obj is StereoLine other && Equals(other);
    }

    public override readonly int GetHashCode() {
        return HashCode.Combine(Left, Right);
    }

    public static bool operator ==(StereoLine left, StereoLine right) {
        return left.Equals(right);
    }

    public static bool operator !=(StereoLine left, StereoLine right) {
        return !left.Equals(right);
    }
}
