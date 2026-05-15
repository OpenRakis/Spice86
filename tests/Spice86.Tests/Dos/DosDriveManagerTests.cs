namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Tests.Utility;

using System.Collections;
using System.Collections.Generic;

using Xunit;

public class DosDriveManagerTests {
    [Fact]
    public void DefaultDriveManagerEnumeration() {
        using TempFile tempFile = new("dos_drive_manager");

        // Arrange
        DosDriveManager dosDriveManager = DosTestHelpers.CreateDriveManager(tempFile.Path);

        // Act
        // These internally rely on ICollection<...>.Count and ICollection<...>.CopyTo() implementations.
        List<KeyValuePair<char, DosDriveBase>> mountedDriveEntries = [.. dosDriveManager];
        List<char> mountedDriveLetters = [.. dosDriveManager.Keys];
        List<DosDriveBase> mountedDrives = [.. dosDriveManager.Values];

        // Assert
        dosDriveManager.Count.Should().Be(3);
        mountedDriveEntries.Should().AllSatisfy(static entry => {
            entry.Value.Should().NotBeNull();
            entry.Key.Should().Be(entry.Value.DriveLetter);
        });
        mountedDriveLetters.Should().BeEquivalentTo(mountedDriveEntries
            .Select(static entry => entry.Key));
        mountedDrives.Should().BeEquivalentTo(mountedDriveEntries
            .Select(static entry => entry.Value));

        mountedDriveEntries.Should().BeEquivalentTo(
            dosDriveManager.As<IEnumerable<KeyValuePair<char, DosDriveBase>>>());
        mountedDriveLetters.Should().BeEquivalentTo(dosDriveManager.Keys);
        mountedDrives.Should().BeEquivalentTo(dosDriveManager.Values);
    }

    [Fact]
    public void MountMemoryDriveEnumeration() {
        using TempFile tempFile = new("dos_drive_manager");

        // Arrange
        DosDriveManager dosDriveManager = DosTestHelpers.CreateDriveManager(tempFile.Path);
        dosDriveManager.MountMemoryDrive(new MemoryDrive() { DriveLetter = 'Z' });

        // Act
        // These internally rely on ICollection<...>.Count and ICollection<...>.CopyTo() implementations.
        List<KeyValuePair<char, DosDriveBase>> mountedDriveEntries = [.. dosDriveManager];
        List<char> mountedDriveLetters = [.. dosDriveManager.Keys];
        List<DosDriveBase> mountedDrives = [.. dosDriveManager.Values];

        // Assert
        dosDriveManager.Count.Should().Be(4);
        dosDriveManager['Z'].Should().BeOfType<MemoryDrive>();
        mountedDriveEntries.Should().AllSatisfy(static entry => {
            entry.Value.Should().NotBeNull();
            entry.Key.Should().Be(entry.Value.DriveLetter);
        });
        mountedDriveLetters.Should().BeEquivalentTo(mountedDriveEntries
            .Select(static entry => entry.Key));
        mountedDrives.Should().BeEquivalentTo(mountedDriveEntries
            .Select(static entry => entry.Value));

        mountedDriveEntries.Should().BeEquivalentTo(
            dosDriveManager.As<IEnumerable<KeyValuePair<char, DosDriveBase>>>());
        mountedDriveLetters.Should().BeEquivalentTo(dosDriveManager.Keys);
        mountedDrives.Should().BeEquivalentTo(dosDriveManager.Values);
    }

    [Fact]
    public void MountMemoryDriveManualEnumeration() {
        using TempFile tempFile = new("dos_drive_manager");

        // Arrange
        DosDriveManager dosDriveManager = DosTestHelpers.CreateDriveManager(tempFile.Path);
        dosDriveManager.MountMemoryDrive(new MemoryDrive() { DriveLetter = 'Z' });

            // Act
            DosDriveBase aDrive = dosDriveManager['A'];
            DosDriveBase bDrive = dosDriveManager['B'];
            DosDriveBase cDrive = dosDriveManager['C'];
            DosDriveBase zDrive = dosDriveManager['Z'];

            using DosDriveManager.Enumerator kvpEnumerator = dosDriveManager.GetEnumerator();
            using DosDriveManager.Enumerator kvpEnumeratorExplicit = (DosDriveManager.Enumerator)
                ((IEnumerable<KeyValuePair<char, DosDriveBase>>)dosDriveManager).GetEnumerator();
            using DosDriveManager.Enumerator dictEntryEnumerator = (DosDriveManager.Enumerator)
                ((IEnumerable)dosDriveManager).GetEnumerator();
            using DosDriveManager.DriveLetterCollection.Enumerator keyEnumerator = dosDriveManager.Keys.GetEnumerator();
            using DosDriveManager.DriveCollection.Enumerator valueEnumerator = dosDriveManager.Values.GetEnumerator();

            // Simplifies common assertions for expected valid enumeration across all enumerators.
            void ShouldEnumerateAndCheck(char expectedDriveLetter, DosDriveBase expectedDrive) {
                KeyValuePair<char, DosDriveBase> kvpExpected = new(expectedDriveLetter, expectedDrive);
                DictionaryEntry dictEntryExpected = new(expectedDriveLetter, expectedDrive);

                // Enumerate all enumerators to the next entry.
                kvpEnumerator.MoveNext().Should().BeTrue();
                kvpEnumeratorExplicit.MoveNext().Should().BeTrue();
                dictEntryEnumerator.MoveNext().Should().BeTrue();
                keyEnumerator.MoveNext().Should().BeTrue();
                valueEnumerator.MoveNext().Should().BeTrue();

                // Check "current" properties.
                kvpEnumerator.Current.Should().Be(kvpExpected);
                ((IEnumerator)kvpEnumerator).Current.Should().Be(kvpExpected);
                ((IDictionaryEnumerator)kvpEnumerator).Key.Should().Be(expectedDriveLetter);
                ((IDictionaryEnumerator)kvpEnumerator).Value.Should().Be(expectedDrive);

                kvpEnumeratorExplicit.Current.Should().Be(kvpExpected);
                ((IEnumerator)kvpEnumeratorExplicit).Current.Should().Be(kvpExpected);
                ((IDictionaryEnumerator)kvpEnumeratorExplicit).Key.Should().Be(expectedDriveLetter);
                ((IDictionaryEnumerator)kvpEnumeratorExplicit).Value.Should().Be(expectedDrive);

                dictEntryEnumerator.Current.Should().Be(kvpExpected);
                ((IEnumerator)dictEntryEnumerator).Current.Should().Be(dictEntryExpected);
                ((IDictionaryEnumerator)dictEntryEnumerator).Key.Should().Be(expectedDriveLetter);
                ((IDictionaryEnumerator)dictEntryEnumerator).Value.Should().Be(expectedDrive);

                keyEnumerator.Current.Should().Be(expectedDriveLetter);
                ((IEnumerator)keyEnumerator).Current.Should().Be(expectedDriveLetter);

                valueEnumerator.Current.Should().Be(expectedDrive);
                ((IEnumerator)valueEnumerator).Current.Should().Be(expectedDrive);
            }

            // Assert
            dosDriveManager.Count.Should().Be(4);

            ShouldEnumerateAndCheck('A', aDrive);
            ShouldEnumerateAndCheck('B', bDrive);
            ShouldEnumerateAndCheck('C', cDrive);
            ShouldEnumerateAndCheck('Z', zDrive);

            kvpEnumerator.MoveNext().Should().BeFalse();
            kvpEnumeratorExplicit.MoveNext().Should().BeFalse();
            dictEntryEnumerator.MoveNext().Should().BeFalse();
            keyEnumerator.MoveNext().Should().BeFalse();
            valueEnumerator.MoveNext().Should().BeFalse();

            kvpEnumerator.MoveNext().Should().BeFalse();
            kvpEnumeratorExplicit.MoveNext().Should().BeFalse();
            dictEntryEnumerator.MoveNext().Should().BeFalse();
            keyEnumerator.MoveNext().Should().BeFalse();
            valueEnumerator.MoveNext().Should().BeFalse();
    }

    [Fact]
    public void MountMemoryDriveManualEnumerationForEach() {
        using TempFile tempFile = new("dos_drive_manager");

        // Arrange
        DosDriveManager dosDriveManager = DosTestHelpers.CreateDriveManager(tempFile.Path);
        dosDriveManager.MountMemoryDrive(new MemoryDrive() { DriveLetter = 'Z' });

        // Act
        DosDriveBase aDrive = dosDriveManager['A'];
        DosDriveBase bDrive = dosDriveManager['B'];
        DosDriveBase cDrive = dosDriveManager['C'];
        DosDriveBase zDrive = dosDriveManager['Z'];

        // Assert
        dosDriveManager.Count.Should().Be(4);

        int index = 0;
        foreach (KeyValuePair<char, DosDriveBase> kvp in dosDriveManager) {
            index.Should().BeInRange(0, 3);
            KeyValuePair<char, DosDriveBase> expected = index switch {
                0 => new('A', aDrive),
                1 => new('B', bDrive),
                2 => new('C', cDrive),
                3 => new('Z', zDrive),
                _ => default
            };
            kvp.Should().Be(expected);
            index++;
        }
        index.Should().Be(4);

        index = 0;
        foreach (char driveLetter in dosDriveManager.Keys) {
            index.Should().BeInRange(0, 3);
            char expected = index switch {
                0 => 'A',
                1 => 'B',
                2 => 'C',
                3 => 'Z',
                _ => default
            };
            driveLetter.Should().Be(expected);
            index++;
        }
        index.Should().Be(4);

        index = 0;
        foreach (DosDriveBase drive in dosDriveManager.Values) {
            index.Should().BeInRange(0, 3);
            DosDriveBase? expected = index switch {
                0 => aDrive,
                1 => bDrive,
                2 => cDrive,
                3 => zDrive,
                _ => default
            };
            drive.Should().Be(expected);
            index++;
        }
        index.Should().Be(4);
    }
}
