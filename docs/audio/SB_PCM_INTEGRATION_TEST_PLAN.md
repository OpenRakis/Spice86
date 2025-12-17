# Sound Blaster PCM Integration Test Plan

## Overview
Comprehensive plan for ASM-based integration tests that play PCM audio through the Sound Blaster and validate output against WAV files recorded from DOSBox Staging. This ensures perfect PCM playback parity.

## Test Pipeline Architecture

```
ASM Program → DSP Commands → DMA Transfer → DAC Playback → Mixer → WAV Output
     ↓              ↓              ↓             ↓            ↓         ↓
  Compiled      Port Writes    Memory Copy   Digital to   Volume    Compare with
   .bin         0x22C/0x22E    via DMA      Analog Conv  Control   DOSBox WAV
```

## Test Categories

### 1. Basic PCM Playback Tests

#### Test 1.1: 8-bit Mono PCM Single-Cycle
**Objective**: Validate basic 8-bit mono PCM playback
**ASM Program**: `sb_pcm_8bit_mono_single.asm`
**Test Data**: Simple sine wave (440Hz, 1 second)
**DSP Commands**:
- 0x40 - Set time constant (sampling rate)
- 0x14 - 8-bit DMA single-cycle output
- 0x48 - Set DMA block size

**Expected Output**: `sb_pcm_8bit_mono_single_output.wav`
**Golden Reference**: `sb_pcm_8bit_mono_single_dosbox.wav` (from DOSBox Staging)

**Validation**:
- Sample rate: 22050 Hz
- RMS error < 1%
- Peak error < 5%
- Duration matches (±5ms)

#### Test 1.2: 8-bit Mono PCM Auto-Init
**Objective**: Validate 8-bit auto-init mode with continuous playback
**ASM Program**: `sb_pcm_8bit_mono_autoinit.asm`
**Test Data**: Repeating pattern (3 cycles)
**DSP Commands**:
- 0x40 - Set time constant
- 0x1C - 8-bit DMA auto-init output
- 0x48 - Set DMA block size

**Expected Output**: `sb_pcm_8bit_mono_autoinit_output.wav`
**Validation**:
- Verify 3 complete cycles
- IRQ signaling between cycles
- No gaps or clicks between buffers

#### Test 1.3: 8-bit Stereo PCM (SB Pro)
**Objective**: Validate 8-bit stereo playback (SB Pro feature)
**ASM Program**: `sb_pcm_8bit_stereo.asm`
**Test Data**: Left=440Hz, Right=880Hz (2 seconds)
**DSP Commands**:
- 0x40 - Set time constant
- 0x48 - Set DMA block size
- 0xA0 - Set input mode (stereo)
- 0x14 - 8-bit DMA output

**Expected Output**: `sb_pcm_8bit_stereo_output.wav`
**Validation**:
- Verify left/right channel separation
- Correct frequency in each channel
- RMS error < 1% per channel

### 2. High-Quality PCM Tests (SB16)

#### Test 2.1: 16-bit Mono PCM
**Objective**: Validate 16-bit mono PCM playback (SB16)
**ASM Program**: `sb_pcm_16bit_mono.asm`
**Test Data**: High-quality sine wave (440Hz, 44100 Hz sample rate)
**DSP Commands**:
- 0x41 - Set sampling rate (high byte)
- 0x42 - Set sampling rate (low byte)
- 0xB0 - 16-bit DMA single-cycle output (mode byte)
- 0xB6 - 16-bit DMA single-cycle output (command)

**Expected Output**: `sb_pcm_16bit_mono_output.wav`
**Golden Reference**: `sb_pcm_16bit_mono_dosbox.wav`

**Validation**:
- Sample rate: 44100 Hz
- Bit depth: 16-bit
- RMS error < 0.5% (higher quality threshold)
- Peak error < 2%

#### Test 2.2: 16-bit Stereo PCM
**Objective**: Validate 16-bit stereo playback (SB16)
**ASM Program**: `sb_pcm_16bit_stereo.asm`
**Test Data**: Music sample (2 seconds, 44100 Hz stereo)
**DSP Commands**:
- 0x41/0x42 - Set sampling rate
- 0xB0 - 16-bit DMA output mode (stereo)
- 0xB6 - 16-bit DMA output command

**Expected Output**: `sb_pcm_16bit_stereo_output.wav`
**Validation**:
- Perfect stereo separation
- RMS error < 0.5% per channel
- No phase issues

#### Test 2.3: 16-bit Auto-Init Mode
**Objective**: Validate 16-bit auto-init continuous playback
**ASM Program**: `sb_pcm_16bit_autoinit.asm`
**Test Data**: Continuous tone (5 cycles)
**DSP Commands**:
- 0x41/0x42 - Set sampling rate
- 0xB0 - 16-bit DMA auto-init mode
- 0xBE - 16-bit DMA auto-init output command

**Expected Output**: `sb_pcm_16bit_autoinit_output.wav`
**Validation**:
- Seamless buffer transitions
- IRQ timing verification
- No audio artifacts at boundaries

### 3. Sample Rate Tests

#### Test 3.1: Variable Sample Rates (8-bit)
**Objective**: Test multiple sample rates with 8-bit PCM
**Sample Rates**: 8000, 11025, 22050, 44100 Hz
**ASM Programs**: `sb_pcm_rate_8000.asm`, `sb_pcm_rate_11025.asm`, etc.
**Test Data**: 440Hz sine wave at each rate

**Validation**:
- Verify actual output sample rate matches requested
- Frequency accuracy (440Hz ±1Hz)
- RMS error < 1%

#### Test 3.2: Variable Sample Rates (16-bit)
**Objective**: Test multiple sample rates with 16-bit PCM
**Sample Rates**: 22050, 32000, 44100, 48000 Hz
**ASM Programs**: Similar pattern as 8-bit tests

**Validation**:
- Higher quality threshold (RMS < 0.5%)
- Verify resampling quality if needed

### 4. DMA Transfer Tests

#### Test 4.1: Small Buffer (< 1KB)
**Objective**: Test short DMA transfers
**Buffer Size**: 256 bytes
**ASM Program**: `sb_pcm_small_buffer.asm`

**Validation**:
- Complete transfer without truncation
- IRQ timing correct for small buffer

#### Test 4.2: Large Buffer (> 64KB)
**Objective**: Test page boundary handling
**Buffer Size**: 65536 bytes (crosses 64KB boundary)
**ASM Program**: `sb_pcm_large_buffer.asm`

**Validation**:
- DMA page register handling
- No corruption at boundaries
- Complete audio playback

#### Test 4.3: Unaligned Buffer
**Objective**: Test non-aligned DMA addresses
**Buffer Alignment**: Offset +1, +7, +13 from aligned
**ASM Programs**: `sb_pcm_unaligned_*.asm`

**Validation**:
- Correct data transfer despite alignment
- No audio glitches

### 5. Mixer Integration Tests

#### Test 5.1: PCM Volume Control
**Objective**: Test mixer PCM volume (voice volume register)
**ASM Program**: `sb_pcm_with_volume.asm`
**Test Pattern**: Play same sample at 0%, 50%, 100% volume

**Expected Output**: Three segments at different volumes
**Validation**:
- Volume levels proportional to setting
- No distortion at high volume
- Complete silence at 0%

#### Test 5.2: PCM + FM Simultaneous
**Objective**: Test PCM playback with concurrent OPL FM synthesis
**ASM Program**: `sb_pcm_and_fm.asm`
**Test Pattern**: PCM drum loop + FM bass line

**Expected Output**: Mixed audio with both PCM and FM
**Validation**:
- Both sources audible
- Correct mixing levels
- No interference or clicks

#### Test 5.3: Master Volume Effect
**Objective**: Test master volume on PCM output
**ASM Program**: `sb_pcm_master_volume.asm`

**Validation**:
- Master volume affects PCM output
- Proportional attenuation

### 6. Edge Cases and Error Handling

#### Test 6.1: DSP Reset During Playback
**Objective**: Test DSP reset behavior
**ASM Program**: `sb_pcm_reset_during_play.asm`

**Validation**:
- Clean stop without hang
- Can restart playback after reset

#### Test 6.2: Invalid Sample Rates
**Objective**: Test handling of out-of-range sample rates
**ASM Program**: `sb_pcm_invalid_rate.asm`
**Test Rates**: 0 Hz, 1 Hz, 100000 Hz

**Validation**:
- Graceful handling (no crash)
- Clamp to valid range or error

#### Test 6.3: Zero-Length Transfer
**Objective**: Test DMA with zero-length block
**ASM Program**: `sb_pcm_zero_length.asm`

**Validation**:
- No crash or hang
- IRQ signaling behavior

## Test Implementation Structure

### C# Test Class Structure
```csharp
public class SbPcmAsmIntegrationTests {
    // Test 1: Basic 8-bit tests
    [Fact]
    public void Test_SB_PCM_8bit_Mono_Single_Cycle()
    
    [Fact]
    public void Test_SB_PCM_8bit_Mono_AutoInit()
    
    [Fact]
    public void Test_SB_PCM_8bit_Stereo()
    
    // Test 2: 16-bit SB16 tests
    [Fact]
    public void Test_SB_PCM_16bit_Mono()
    
    [Fact]
    public void Test_SB_PCM_16bit_Stereo()
    
    [Fact]
    public void Test_SB_PCM_16bit_AutoInit()
    
    // Test 3: Sample rate tests
    [Theory]
    [InlineData(8000)]
    [InlineData(11025)]
    [InlineData(22050)]
    [InlineData(44100)]
    public void Test_SB_PCM_Variable_Sample_Rates_8bit(int sampleRate)
    
    // Test 4: DMA tests
    [Fact]
    public void Test_SB_PCM_Small_Buffer()
    
    [Fact]
    public void Test_SB_PCM_Large_Buffer()
    
    // Test 5: Mixer integration
    [Fact]
    public void Test_SB_PCM_Volume_Control()
    
    [Fact]
    public void Test_SB_PCM_And_FM_Simultaneous()
    
    // Test 6: Edge cases
    [Fact]
    public void Test_SB_PCM_DSP_Reset_During_Playback()
}
```

### Helper Methods
```csharp
private WavComparisonResult RunPcmTestAndCompareWav(
    string asmBinaryPath,
    string goldenWavPath,
    int expectedSampleRate,
    int expectedDurationMs);

private void GenerateTestPcmData(
    byte[] buffer,
    int sampleRate,
    int frequency,
    bool stereo,
    bool sixteenBit);

private AudioComparisonResult CompareWavFiles(
    string actualWavPath,
    string goldenWavPath,
    double rmsThreshold = 0.01);
```

## ASM Program Templates

### Template 1: Basic 8-bit PCM Playback
```nasm
; sb_pcm_8bit_mono_single.asm
use16
org 0x100

start:
    ; Setup segments
    mov ax, cs
    mov ds, ax
    mov es, ax
    
    ; Reset DSP (port 0x226)
    mov dx, 0x226
    mov al, 1
    out dx, al
    ; Delay
    mov cx, 100
.reset_delay:
    loop .reset_delay
    mov al, 0
    out dx, al
    
    ; Setup DMA channel 1 for playback
    ; ... (DMA setup code)
    
    ; Set DSP time constant for 22050 Hz
    ; Time constant = 256 - (1000000 / sample_rate)
    mov dx, 0x22C        ; DSP write port
    call wait_dsp_write
    mov al, 0x40         ; Set time constant command
    out dx, al
    call wait_dsp_write
    mov al, 167          ; Time constant for 22050 Hz
    out dx, al
    
    ; Set block size
    call wait_dsp_write
    mov al, 0x48         ; Set block size
    out dx, al
    call wait_dsp_write
    mov al, 0xFF         ; Low byte (256 bytes - 1)
    out dx, al
    call wait_dsp_write
    mov al, 0x03         ; High byte (1024 bytes total)
    out dx, al
    
    ; Start single-cycle DMA playback
    call wait_dsp_write
    mov al, 0x14         ; 8-bit DMA single-cycle
    out dx, al
    
    ; Wait for completion (IRQ or polling)
    ; ... (wait code)
    
    ; Report success
    mov dx, 0x999
    mov al, 0x00
    out dx, al
    hlt

wait_dsp_write:
    ; Wait for DSP write buffer ready
    push cx
    push ax
    mov cx, 0xFFFF
.wait:
    mov dx, 0x22C
    in al, dx
    test al, 0x80
    jz .ready
    loop .wait
.ready:
    pop ax
    pop cx
    ret

test_pcm_data:
    ; Include PCM test data here
    ; 1024 bytes of 440Hz sine wave
    incbin "test_sine_440hz_22050_8bit.raw"
```

### Template 2: 16-bit Stereo PCM (SB16)
```nasm
; sb_pcm_16bit_stereo.asm
; Similar structure but with SB16 commands
; Uses 0x41/0x42 for sample rate
; Uses 0xB0/0xB6 for 16-bit output
```

## Golden Reference Generation from DOSBox Staging

### Step 1: Create DOSBox Test Program
Create a DOS program that plays the same test PCM data:
```
test.exe → plays test_sine_440hz_22050_8bit.raw
```

### Step 2: Configure DOSBox for Capture
```ini
[mixer]
rate=48000
capture=wav

[sblaster]
sbtype=sb16
```

### Step 3: Run and Capture
```bash
dosbox-staging test.exe
# Audio is captured to capture_*.wav
```

### Step 4: Place in Test Resources
```
tests/Spice86.Tests/Resources/SbPcmGoldenReferences/
  ├── sb_pcm_8bit_mono_single_dosbox.wav
  ├── sb_pcm_8bit_stereo_dosbox.wav
  ├── sb_pcm_16bit_mono_dosbox.wav
  ├── sb_pcm_16bit_stereo_dosbox.wav
  └── ... (more golden references)
```

## Validation Metrics

### Audio Comparison Metrics
1. **RMS Error**: Root Mean Square difference between samples
   - Threshold: < 1% for 8-bit, < 0.5% for 16-bit
   
2. **Peak Error**: Maximum absolute difference
   - Threshold: < 5% for 8-bit, < 2% for 16-bit
   
3. **Frequency Accuracy**: FFT analysis of tone tests
   - Threshold: ±1 Hz for primary frequency
   
4. **Duration Match**: Length comparison
   - Threshold: ±5ms or ±0.1%
   
5. **Channel Correlation**: Cross-correlation for stereo
   - Threshold: > 0.98 correlation coefficient

### Statistical Tests
- **Signal-to-Noise Ratio (SNR)**: > 40 dB for 8-bit, > 80 dB for 16-bit
- **Total Harmonic Distortion (THD)**: < 1%
- **Spectral Flatness**: For white noise tests

## Test Data Sets

### Test Audio Files to Generate
1. **Sine waves**: 440Hz, 880Hz, 1760Hz at various sample rates
2. **Square wave**: 440Hz for distortion testing
3. **White noise**: Full spectrum test
4. **Sweep**: 20Hz to 20kHz frequency sweep
5. **Music sample**: Real-world complex audio (2-3 seconds)
6. **Silence**: All zeros (edge case)
7. **Full scale**: Maximum amplitude (clipping test)

### Data Format
- Raw PCM (no header)
- 8-bit: unsigned 0-255 (128 = silence)
- 16-bit: signed -32768 to 32767 (0 = silence)
- Stereo: interleaved L/R samples

## Implementation Phases

### Phase 1: Infrastructure (Week 1)
- [ ] Create `SbPcmAsmIntegrationTests.cs` class
- [ ] Implement WAV comparison helpers
- [ ] Create test data generator
- [ ] Setup golden reference directory structure

### Phase 2: Basic Tests (Week 1-2)
- [ ] Implement 8-bit mono single-cycle test
- [ ] Implement 8-bit mono auto-init test
- [ ] Generate corresponding ASM programs
- [ ] Capture DOSBox golden references

### Phase 3: Advanced Tests (Week 2-3)
- [ ] Implement 8-bit stereo test
- [ ] Implement 16-bit tests (mono/stereo/auto-init)
- [ ] Implement sample rate variation tests
- [ ] Generate all ASM programs

### Phase 4: Integration Tests (Week 3-4)
- [ ] Implement DMA transfer tests
- [ ] Implement mixer integration tests
- [ ] Implement edge case tests
- [ ] Full validation suite

### Phase 5: Documentation (Week 4)
- [ ] Document all tests
- [ ] Create usage guide
- [ ] Add troubleshooting section
- [ ] Update main TDD documentation

## Expected Outcomes

### Success Criteria
- All tests pass with < 1% RMS error
- Complete coverage of SB PCM playback modes
- Direct DOSBox Staging parity validation
- Automated regression testing capability

### Deliverables
1. 20+ ASM integration tests (all passing)
2. Complete set of golden reference WAV files
3. Test data generation tools
4. Comprehensive documentation
5. CI/CD integration

## Dependencies

### Tools Required
- NASM assembler (for ASM compilation)
- DOSBox Staging (for golden reference generation)
- Audio editing tools (for test data creation - Audacity)
- FFT library (for frequency analysis - already in .NET)

### Spice86 Components Tested
- Sound Blaster DSP emulation
- DMA controller
- DAC (Digital to Analog Converter)
- Mixer (volume control, channel mixing)
- IRQ handling

## Notes

### DOSBox Staging Differences
Document any intentional differences from DOSBox:
- Timing variations (acceptable within tolerance)
- Resampling algorithm differences
- Mixer implementation details

### Known Limitations
- Test execution time (long WAV files take time)
- Golden reference file sizes (WAV files can be large)
- Platform-specific floating point differences (acceptable)

### Future Extensions
- ADPCM compression tests
- SB Pro stereo with mixer control
- Creative DSP MIDI playback
- Environmental audio effects (reverb/chorus)
