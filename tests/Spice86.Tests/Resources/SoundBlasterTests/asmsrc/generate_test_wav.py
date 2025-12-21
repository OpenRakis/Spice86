#!/usr/bin/env python3
"""
Generate a test WAV file for Sound Blaster PCM testing.
Specifications:
- 11025 Hz sample rate
- Mono (1 channel)
- 8-bit unsigned PCM (0-255, 128 = silence)
- Simple 440Hz sine wave for easy validation
"""

import wave
import math
import struct

def generate_sine_wave_8bit_mono(filename, sample_rate=11025, duration_seconds=1.0, frequency=440.0):
    """
    Generate an 8-bit mono WAV file with a sine wave.
    
    Args:
        filename: Output WAV file path
        sample_rate: Sample rate in Hz (default 11025)
        duration_seconds: Duration in seconds (default 1.0)
        frequency: Sine wave frequency in Hz (default 440)
    """
    num_samples = int(sample_rate * duration_seconds)
    
    # Create WAV file
    with wave.open(filename, 'w') as wav_file:
        # Set parameters: channels, sample_width, framerate, nframes, comptype, compname
        # For 8-bit: sample_width = 1
        wav_file.setnchannels(1)  # Mono
        wav_file.setsampwidth(1)  # 8-bit = 1 byte per sample
        wav_file.setframerate(sample_rate)
        
        # Generate sine wave samples
        samples = bytearray()
        for i in range(num_samples):
            # Generate sine wave value (-1.0 to 1.0)
            t = i / sample_rate
            sine_value = math.sin(2 * math.pi * frequency * t)
            
            # Convert to 8-bit unsigned (0-255, 128 = silence)
            # Scale from [-1.0, 1.0] to [0, 255]
            sample_8bit = int((sine_value + 1.0) * 127.5)
            sample_8bit = max(0, min(255, sample_8bit))  # Clamp
            
            samples.append(sample_8bit)
        
        # Write all samples
        wav_file.writeframes(bytes(samples))
    
    print(f"Generated {filename}")
    print(f"  Sample rate: {sample_rate} Hz")
    print(f"  Duration: {duration_seconds} seconds")
    print(f"  Samples: {num_samples}")
    print(f"  Frequency: {frequency} Hz")
    print(f"  Format: 8-bit unsigned mono PCM")

if __name__ == "__main__":
    # Generate test WAV file
    output_file = "test_sine_440hz_11025_8bit_mono.wav"
    generate_sine_wave_8bit_mono(output_file, sample_rate=11025, duration_seconds=1.0, frequency=440.0)
