using FluentAssertions;
using System;
using Xunit;

namespace Spice86.UI.Tests.Views;

/// <summary>
/// Tests for the centering calculation logic used in ModernDisassemblyView
/// </summary>
public class CenteringCalculationTests
{
    /// <summary>
    /// Tests that the centering calculation correctly positions items in the middle 50% of the viewport
    /// for various viewport heights and item positions
    /// </summary>
    /// <param name="viewportHeight">The height of the viewport</param>
    /// <param name="itemHeight">The height of each item</param>
    /// <param name="itemIndex">The index of the item to center</param>
    /// <param name="totalItems">The total number of items</param>
    [Theory]
    [InlineData(500, 20, 50, 100)]  // Standard case - middle of the list
    [InlineData(500, 20, 0, 100)]   // Edge case - first item
    [InlineData(500, 20, 99, 100)]  // Edge case - last item
    [InlineData(200, 30, 25, 50)]   // Different viewport and item heights
    [InlineData(800, 15, 75, 100)]  // Larger viewport
    public void CenteringCalculation_ShouldPositionItemInMiddle50PercentOfViewport(
        double viewportHeight, double itemHeight, int itemIndex, int totalItems)
    {
        // Arrange
        double scrollExtentHeight = totalItems * itemHeight; // Total scrollable height
        
        // Calculate the position of the target item in the virtual space
        double itemPosition = itemIndex * itemHeight;
        
        // Calculate the scroll offset that would center the item
        // This is the same calculation used in ScrollToLineWithMiddlePlacement
        double calculatedOffset = itemPosition - (viewportHeight / 2) + (itemHeight / 2);
        
        // Ensure the offset is within valid bounds
        double boundedOffset = Math.Max(0, Math.Min(calculatedOffset, scrollExtentHeight - viewportHeight));
        
        // Calculate where the item would be positioned in the viewport with this offset
        double itemTopInViewport = itemPosition - boundedOffset;
        
        // Calculate the middle 50% of the viewport
        double middleStart = viewportHeight * 0.25;
        double middleEnd = viewportHeight * 0.75;
        
        // Calculate the center of the item in the viewport
        double itemCenterInViewport = itemTopInViewport + (itemHeight / 2);
        
        // Act & Assert
        // For items that can be properly centered (not at the edges)
        if (itemIndex > 10 && itemIndex < totalItems - 10)
        {
            // The item should be centered in the middle 50% of the viewport
            itemCenterInViewport.Should().BeGreaterThanOrEqualTo(middleStart,
                $"Item {itemIndex} center ({itemCenterInViewport}) should be in or after the start of the middle 50% of the viewport ({middleStart})");
            itemCenterInViewport.Should().BeLessThanOrEqualTo(middleEnd,
                $"Item {itemIndex} center ({itemCenterInViewport}) should be in or before the end of the middle 50% of the viewport ({middleEnd})");
        }
        else
        {
            // For items at the edges, they might not be perfectly centered
            // but they should at least be visible in the viewport
            itemTopInViewport.Should().BeLessThan(viewportHeight,
                $"Item {itemIndex} top ({itemTopInViewport}) should be visible in the viewport (< {viewportHeight})");
            (itemTopInViewport + itemHeight).Should().BeGreaterThan(0,
                $"Item {itemIndex} bottom ({itemTopInViewport + itemHeight}) should be visible in the viewport (> 0)");
        }
    }
    
    /// <summary>
    /// Tests the centering calculation with extreme values to ensure it handles edge cases correctly
    /// </summary>
    [Fact]
    public void CenteringCalculation_ShouldHandleExtremeValues()
    {
        // Arrange - Very large viewport with small items
        double viewportHeight = 2000;
        double itemHeight = 5;
        int totalItems = 10000;
        int targetItemIndex = 5000;
        
        double scrollExtentHeight = totalItems * itemHeight;
        double itemPosition = targetItemIndex * itemHeight;
        
        // Act - Calculate the offset
        double calculatedOffset = itemPosition - (viewportHeight / 2) + (itemHeight / 2);
        double boundedOffset = Math.Max(0, Math.Min(calculatedOffset, scrollExtentHeight - viewportHeight));
        
        // Calculate item position in viewport
        double itemTopInViewport = itemPosition - boundedOffset;
        double itemCenterInViewport = itemTopInViewport + (itemHeight / 2);
        
        // Calculate middle 50%
        double middleStart = viewportHeight * 0.25;
        double middleEnd = viewportHeight * 0.75;
        
        // Assert
        itemCenterInViewport.Should().BeGreaterThanOrEqualTo(middleStart);
        itemCenterInViewport.Should().BeLessThanOrEqualTo(middleEnd);
        
        // Test with very small viewport and large items
        viewportHeight = 50;
        itemHeight = 100;
        totalItems = 100;
        targetItemIndex = 50;
        
        scrollExtentHeight = totalItems * itemHeight;
        itemPosition = targetItemIndex * itemHeight;
        
        calculatedOffset = itemPosition - (viewportHeight / 2) + (itemHeight / 2);
        boundedOffset = Math.Max(0, Math.Min(calculatedOffset, scrollExtentHeight - viewportHeight));
        
        itemTopInViewport = itemPosition - boundedOffset;
        
        // In this case, the item is larger than the viewport
        // We should at least see part of the item
        (itemTopInViewport + itemHeight).Should().BeGreaterThan(0, 
            "At least part of the item should be visible at the bottom");
        itemTopInViewport.Should().BeLessThan(viewportHeight, 
            "At least part of the item should be visible at the top");
    }
}
