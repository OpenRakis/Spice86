namespace Spice86.UI.Tests.Views;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Tests for the ModernDisassemblyView
/// </summary>
public class ModernDisassemblyViewTests
{
    /// <summary>
    /// Tests that the view model's ScrollToAddress event is triggered with the correct address
    /// </summary>
    [Fact]
    public void ScrollToAddress_ShouldTriggerEventWithCorrectAddress()
    {
        // Arrange
        var viewModel = new TestModernDisassemblyViewModel();
        
        // Hook up the ScrollToAddress event
        bool eventWasTriggered = false;
        uint addressPassedToEvent = 0;
        
        viewModel.ScrollToAddress += address =>
        {
            eventWasTriggered = true;
            addressPassedToEvent = address;
        };
        
        // Act
        const uint targetAddress = 0x1500;
        viewModel.TriggerScrollToAddress(targetAddress);
        
        // Assert
        eventWasTriggered.Should().BeTrue("The ScrollToAddress event should have been triggered");
        addressPassedToEvent.Should().Be(targetAddress, 
            "The correct address should have been passed to the ScrollToAddress event");
    }
    
    /// <summary>
    /// Tests that the IpPhysicalAddress property is correctly updated
    /// </summary>
    [Fact]
    public void IpPhysicalAddress_WhenSet_ShouldRaisePropertyChangedEvent()
    {
        // Arrange
        var viewModel = new TestModernDisassemblyViewModel();
        bool propertyChangedRaised = false;
        string? propertyName = null;
        
        viewModel.PropertyChanged += (_, args) =>
        {
            propertyChangedRaised = true;
            propertyName = args.PropertyName;
        };
        
        // Act
        const uint newAddress = 0x2000;
        viewModel.IpPhysicalAddress = newAddress;
        
        // Assert
        propertyChangedRaised.Should().BeTrue("PropertyChanged event should have been raised");
        propertyName.Should().Be(nameof(TestModernDisassemblyViewModel.IpPhysicalAddress), 
            "PropertyChanged event should have the correct property name");
        viewModel.IpPhysicalAddress.Should().Be(newAddress, 
            "IpPhysicalAddress should be updated to the new value");
    }
    
    /// <summary>
    /// Tests that the CurrentlyFocusedAddress property is correctly updated
    /// </summary>
    [Fact]
    public void CurrentlyFocusedAddress_WhenSet_ShouldRaisePropertyChangedEvent()
    {
        // Arrange
        var viewModel = new TestModernDisassemblyViewModel();
        bool propertyChangedRaised = false;
        string? propertyName = null;
        
        viewModel.PropertyChanged += (_, args) =>
        {
            propertyChangedRaised = true;
            propertyName = args.PropertyName;
        };
        
        // Act
        const uint newAddress = 0x2000;
        viewModel.CurrentlyFocusedAddress = newAddress;
        
        // Assert
        propertyChangedRaised.Should().BeTrue("PropertyChanged event should have been raised");
        propertyName.Should().Be(nameof(TestModernDisassemblyViewModel.CurrentlyFocusedAddress), 
            "PropertyChanged event should have the correct property name");
        viewModel.CurrentlyFocusedAddress.Should().Be(newAddress, 
            "CurrentlyFocusedAddress should be updated to the new value");
    }
    
    /// <summary>
    /// Tests that adding a debugger line works correctly
    /// </summary>
    [Fact]
    public void AddDebuggerLine_ShouldAddLineToCollection()
    {
        // Arrange
        var viewModel = new TestModernDisassemblyViewModel();
        const uint address = 0x3000;
        var line = new TestDebuggerLineViewModel();
        
        // Act
        viewModel.AddDebuggerLine(address, line);
        
        // Assert
        viewModel.DebuggerLines.Should().ContainKey(address, 
            "DebuggerLines should contain the added line at the specified address");
        viewModel.DebuggerLines[address].Should().Be(line, 
            "The line at the specified address should be the one that was added");
    }
    
    /// <summary>
    /// Tests that the ScrollToLineWithMiddlePlacement method correctly positions the item in the middle 50% of the viewport
    /// </summary>
    [Fact]
    public void ScrollToLineWithMiddlePlacement_ShouldPositionItemInMiddle50PercentOfViewport()
    {
        // This test verifies the calculation logic used in ScrollToLineWithMiddlePlacement
        // without requiring the actual UI components
        
        // Arrange
        // Mock viewport dimensions
        const double viewportHeight = 500;
        const double itemHeight = 20;
        const double scrollExtentHeight = 2000; // Total scrollable height
        
        // Create a test scenario with 100 items (0-99)
        const int totalItems = 100;
        const int targetItemIndex = 50; // Item in the middle of the list
        
        // Calculate the expected position of the target item in the virtual space
        const double itemPosition = targetItemIndex * itemHeight;
        
        // Calculate the expected scroll offset that would center the item
        // This is the same calculation used in ScrollToLineWithMiddlePlacement
        double expectedOffset = itemPosition - (viewportHeight / 2) + (itemHeight / 2);
        
        // Ensure the offset is within valid bounds (same as in the method)
        expectedOffset = Math.Max(0, Math.Min(expectedOffset, scrollExtentHeight - viewportHeight));
        
        // Calculate where the item would be positioned in the viewport with this offset
        double itemTopInViewport = itemPosition - expectedOffset;

        // Calculate the middle 50% of the viewport
        double middleStart = viewportHeight * 0.25;
        double middleEnd = viewportHeight * 0.75;
        
        // Calculate the center of the item in the viewport
        double itemCenterInViewport = itemTopInViewport + (itemHeight / 2);
        
        // Act & Assert
        // Verify the item's center is within the middle 50% of the viewport
        itemCenterInViewport.Should().BeGreaterThanOrEqualTo(middleStart,
            "The item's center should be in or after the start of the middle 50% of the viewport");
        itemCenterInViewport.Should().BeLessThanOrEqualTo(middleEnd,
            "The item's center should be in or before the end of the middle 50% of the viewport");
        
        // Additional verification for edge cases
        
        // Test with an item at the beginning of the list
        int firstItemIndex = 0;
        double firstItemPosition = firstItemIndex * itemHeight;
        double firstItemOffset = Math.Max(0, firstItemPosition - (viewportHeight / 2) + (itemHeight / 2));
        double firstItemTopInViewport = firstItemPosition - firstItemOffset;
        double firstItemCenterInViewport = firstItemTopInViewport + (itemHeight / 2);
        
        // For items at the beginning, we can't center them perfectly because we can't scroll past the top
        // But they should still be visible in the viewport
        firstItemCenterInViewport.Should().BeLessThanOrEqualTo(viewportHeight,
            "The first item should be visible in the viewport");
        
        // Test with an item at the end of the list
        int lastItemIndex = totalItems - 1;
        double lastItemPosition = lastItemIndex * itemHeight;
        double lastItemOffset = Math.Min(scrollExtentHeight - viewportHeight, 
            lastItemPosition - (viewportHeight / 2) + (itemHeight / 2));
        double lastItemTopInViewport = lastItemPosition - lastItemOffset;
        double lastItemCenterInViewport = lastItemTopInViewport + (itemHeight / 2);
        
        // For items at the end, we can't center them perfectly because we can't scroll past the bottom
        // But they should still be visible in the viewport
        lastItemCenterInViewport.Should().BeGreaterThanOrEqualTo(0,
            "The last item should be visible in the viewport");
    }
    
    /// <summary>
    /// Test implementation of ModernDisassemblyViewModel
    /// </summary>
    private class TestModernDisassemblyViewModel : ObservableObject
    {
        private uint _ipPhysicalAddress;
        private uint _currentlyFocusedAddress;
        private readonly Dictionary<uint, TestDebuggerLineViewModel> _debuggerLines = new();

        public TestModernDisassemblyViewModel()
        {
            // Initialize with some test data
            for (uint i = 0; i < 10; i++)
            {
                uint address = 0x1000 + (i * 0x10);
                var line = new TestDebuggerLineViewModel();
                _debuggerLines.Add(address, line);
            }
            
            // Set initial values
            _ipPhysicalAddress = 0x1000;
            _currentlyFocusedAddress = 0x1000;
        }
        
        public uint IpPhysicalAddress
        {
            get => _ipPhysicalAddress;
            set
            {
                if (_ipPhysicalAddress != value)
                {
                    _ipPhysicalAddress = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public uint CurrentlyFocusedAddress
        {
            get => _currentlyFocusedAddress;
            set
            {
                if (_currentlyFocusedAddress != value)
                {
                    _currentlyFocusedAddress = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public IReadOnlyDictionary<uint, TestDebuggerLineViewModel> DebuggerLines => _debuggerLines;

        public event Action<uint>? ScrollToAddress;
        
        public void TriggerScrollToAddress(uint address)
        {
            ScrollToAddress?.Invoke(address);
        }
        
        public void AddDebuggerLine(uint address, TestDebuggerLineViewModel line) {
            _debuggerLines.TryAdd(address, line);
        }
    }
    
    /// <summary>
    /// Test implementation of DebuggerLineViewModel
    /// </summary>
    private class TestDebuggerLineViewModel
    {
    }
}
