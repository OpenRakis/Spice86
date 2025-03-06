using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

using Xunit;

namespace Spice86.Tests.Utils;

public class MemoryUtilsTests
{
    [Fact]
    public void CanConvertAllPhysicalAddressesToSegmentedAdressesInRealModeAddressSpace() {
        for (uint i = 0; i <= A20Gate.EndOfHighMemoryArea; i++) {
            var segmentedAddress = MemoryUtils.ToSegmentedAddress(i);
            var physicalAddressFromSegmentedAddressClass = segmentedAddress.ToPhysical();
            Assert.Equal(i, physicalAddressFromSegmentedAddressClass);
        }
    }
}
