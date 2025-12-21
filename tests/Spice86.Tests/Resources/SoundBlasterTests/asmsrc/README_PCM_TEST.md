# Sound Blaster PCM Test Resources

This directory contains test resources for Sound Blaster PCM playback validation.

## Test Files

### sb_pcm_8bit_11025_mono.asm
ASM program that plays an 11025 Hz, mono, 8-bit PCM audio file via Sound Blaster.
- **Sample Rate**: 11025 Hz
- **Format**: 8-bit unsigned mono PCM
- **Test Signal**: 440Hz sine wave (1 second duration)
- **Size**: 11025 samples (11025 bytes)

### sb_pcm_8bit_11025_mono.bin
Compiled binary from the ASM program above. Ready to run in Spice86 tests.

### test_sine_440hz_11025_8bit_mono.wav
Reference WAV file containing the test audio:
- **Sample Rate**: 11025 Hz
- **Format**: 8-bit unsigned mono PCM
- **Content**: 440Hz sine wave
- **Duration**: 1 second

### test_sine_440hz_11025_8bit_mono.raw
Raw PCM data (no header) extracted from the WAV file. This is embedded in the ASM program via `incbin`.

## Building the Test Program

### Prerequisites
- NASM assembler (version 2.16+ recommended)
- Python 3 (for generating test WAV files)

### Build Steps

1. **Generate the test WAV file** (if you need to regenerate it):
   ```bash
   python3 generate_test_wav.py
   ```
   This creates `test_sine_440hz_11025_8bit_mono.wav`.

2. **Extract raw PCM data** (if needed):
   ```bash
   python3 extract_pcm.py
   ```
   This creates `test_sine_440hz_11025_8bit_mono.raw` from the WAV file.

3. **Assemble the ASM program**:
   ```bash
   nasm -f bin -o sb_pcm_8bit_11025_mono.bin sb_pcm_8bit_11025_mono.asm
   ```
   This creates the binary `sb_pcm_8bit_11025_mono.bin`.

## Test Purpose

This is an **end-to-end integration test** for the Sound Blaster PCM audio pipeline:

1. ASM program loads PCM data into memory
2. Configures Sound Blaster DSP for 11025 Hz playback
3. Sets up DMA transfer
4. Starts playback via DSP command 0x14 (8-bit single-cycle DMA)
5. Waits for DMA completion (IRQ signaling)
6. Reports success/failure via port 0x999

The test validates:
- DSP command handling (time constant, speaker control, DMA commands)
- DMA controller integration
- Audio playback timing
- IRQ signaling

## Expected Behavior

When run in Spice86:
- The program should play the 440Hz tone for 1 second
- DMA transfer should complete without errors
- IRQ should be signaled when playback completes
- Test should write 0x00 to port 0x999 on success

## Integration with C# Tests

This test can be used with `SbPcmAsmIntegrationTests.cs`:

```csharp
[Fact]
public void Test_SB_PCM_8bit_11025Hz_Mono() {
    string asmBinary = Path.Combine("Resources", "SoundBlasterTests", "sb_pcm_8bit_11025_mono.bin");
    string referenceWav = Path.Combine("Resources", "SoundBlasterTests", "test_sine_440hz_11025_8bit_mono.wav");
    
    // Run test and compare output with reference WAV
    WavComparisonResult result = RunPcmTestAndCompareWav(
        asmBinary,
        referenceWav,
        expectedSampleRate: 11025,
        expectedDurationMs: 1000);
    
    result.RmsError.Should().BeLessThan(0.01, "8-bit mono PCM should match reference");
}
```

## Technical Details

### Sound Blaster DSP Commands Used
- **0x40**: Set time constant (sample rate)
- **0x14**: 8-bit single-cycle DMA output
- **0xD1**: Turn speaker on
- **0xD3**: Turn speaker off

### DMA Configuration
- **Channel**: 1 (standard for Sound Blaster)
- **Mode**: Single transfer, read from memory
- **Transfer Size**: 11025 bytes

### Time Constant Calculation
For sample rate of 11025 Hz:
```
TC = 256 - (1000000 / sample_rate)
TC = 256 - (1000000 / 11025)
TC = 256 - 90.7
TC ≈ 165 (0xA5)
```

## DOSBox Staging Reference

This test is designed to mirror DOSBox Staging behavior. To generate a golden reference:

1. Run the program in DOSBox Staging with audio capture enabled
2. Capture output WAV file
3. Compare Spice86 output with DOSBox output using WAV comparison metrics

## Troubleshooting

### NASM Assembly Errors
- Ensure `test_sine_440hz_11025_8bit_mono.raw` exists in the same directory
- Check NASM version: `nasm -v` (2.16+ recommended)

### DMA Transfer Issues
- Verify DMA channel 1 is not masked
- Check physical address calculation (segment * 16 + offset)
- Ensure DMA count is set correctly (size - 1)

### Audio Output Issues
- Confirm Sound Blaster is properly initialized in Spice86
- Check mixer configuration (volume levels)
- Verify sample rate conversion in mixer

## Related Documentation

- [SB_PCM_INTEGRATION_TEST_PLAN.md](../../../docs/audio/SB_PCM_INTEGRATION_TEST_PLAN.md) - Complete test plan
- [AUDIO_PORT_PLAN.md](../../../../AUDIO_PORT_PLAN.md) - Overall audio porting plan
- DOSBox Staging: https://github.com/dosbox-staging/dosbox-staging

## License

This test code is part of the Spice86 project and follows the same license.
