namespace Spice86.Tests.Video;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Memory;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
///     Runs the 256-color renderer test suite with the scalar (non-SIMD) implementation.
///     Also hosts <see cref="VideoMemory"/> span tests that are renderer-independent.
/// </summary>
public sealed class VgaRenderer256ColorTests : VgaRenderer256ColorTestsBase {
    protected override bool IsSupported => true;

    protected override IVgaRenderer256Color Create256ColorRenderer() => new VgaRenderer256ColorScalar();
}
