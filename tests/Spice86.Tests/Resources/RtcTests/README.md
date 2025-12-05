# RTC/CMOS Integration Tests

This directory contains assembly language test programs for validating the RTC (Real-Time Clock) and CMOS functionality in Spice86.

## Test Programs

### 1. cmos_ports.com
Tests direct access to CMOS registers via I/O ports 0x70 (address) and 0x71 (data).

**Tests performed (7 total):**
- Reads seconds register (0x00)
- Reads minutes register (0x02)
- Reads hours register (0x04)
- Reads day of week register (0x06)
- Reads day of month register (0x07)
- Reads month register (0x08)
- Reads year register (0x09)

Each test validates that the returned value is in proper BCD format (both nibbles 0-9).

### 2. bios_int1a.com
Tests BIOS INT 1A time services (functions 00h-05h).

**Tests performed (6 total):**
- INT 1A, AH=00h: Get System Clock Counter
- INT 1A, AH=01h: Set System Clock Counter
- INT 1A, AH=02h: Read RTC Time
- INT 1A, AH=03h: Set RTC Time (stub)
- INT 1A, AH=04h: Read RTC Date
- INT 1A, AH=05h: Set RTC Date (stub)

### 3. dos_int21h.com
Tests DOS INT 21H date/time services (functions 2Ah-2Dh).

**Tests performed (11 total):**
- INT 21H, AH=2Ah: Get System Date
- INT 21H, AH=2Bh: Set System Date (valid date)
- INT 21H, AH=2Bh: Set System Date (invalid year - before 1980)
- INT 21H, AH=2Bh: Set System Date (invalid month)
- INT 21H, AH=2Bh: Set System Date (invalid day)
- INT 21H, AH=2Ch: Get System Time
- INT 21H, AH=2Dh: Set System Time (valid time)
- INT 21H, AH=2Dh: Set System Time (invalid hour)
- INT 21H, AH=2Dh: Set System Time (invalid minutes)
- INT 21H, AH=2Dh: Set System Time (invalid seconds)
- INT 21H, AH=2Dh: Set System Time (invalid hundredths)

### 4. bios_int15h_83h.com
Tests BIOS INT 15h, AH=83h - Event Wait Interval function.

**Tests performed (5 total):**
- Set a wait event (AL=00h)
- Detect already-active wait (should return error AH=80h)
- Cancel a wait event (AL=01h)
- Set a new wait after canceling (should succeed)
- Cancel the second wait

### 5. bios_int70_wait.com
Tests BIOS INT 15h, AH=83h RTC configuration and INT 70h setup.

**Tests performed (7 total):**
- Set up a wait with INT 15h, AH=83h and user flag address
- Verify RTC wait flag is set in BIOS data area (offset 0xA0)
- Verify CMOS Status Register B has periodic interrupt enabled (bit 6)
- Verify user wait timeout is stored in BIOS data area (offset 0x9C)
- Cancel the wait with AL=01h
- Verify RTC wait flag is cleared after cancel
- Verify CMOS Status Register B has periodic interrupt disabled after cancel

## Test Protocol

Each test program uses a simple I/O port protocol to report results:

- **Port 0x999** (RESULT_PORT): Test result (0x00 = success, 0xFF = failure)
- **Port 0x998** (DETAILS_PORT): Test progress counter (increments with each test)

The test framework (`RtcIntegrationTests.cs`) monitors these ports to determine test success/failure.

## Building the Tests

The test programs are written in x86 assembly (16-bit real mode) and compiled to DOS .COM format using NASM.

### Prerequisites
- NASM assembler (version 2.0 or later)

### Compilation

To compile all test programs:

```bash
cd tests/Spice86.Tests/Resources/RtcTests
nasm -f bin -o cmos_ports.com cmos_ports.asm
nasm -f bin -o bios_int1a.com bios_int1a.asm
nasm -f bin -o dos_int21h.com dos_int21h.asm
nasm -f bin -o bios_int15h_83h.com bios_int15h_83h.asm
nasm -f bin -o bios_int70_wait.com bios_int70_wait.asm
```

Or compile all at once:
```bash
for file in *.asm; do nasm -f bin -o "${file%.asm}.com" "$file"; done
```

The compiled .COM files are automatically copied to the test output directory during build due to the project configuration:

```xml
<ItemGroup>
    <None Update="Resources\**">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
</ItemGroup>
```

## Running the Tests

The tests are integrated into the xUnit test suite and run automatically with:

```bash
dotnet test --filter "FullyQualifiedName~Rtc"
```

Or run all tests:
```bash
dotnet test
```

## Notes

- All test programs use 16-bit real mode x86 assembly
- Tests are designed to run in the Spice86 DOS environment
- BCD validation ensures CMOS registers return proper Binary Coded Decimal values
- Error handling tests verify that invalid inputs are properly rejected
