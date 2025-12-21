#!/usr/bin/env python3
"""
Extract raw PCM data from WAV file for embedding in ASM program.
"""

import wave

def extract_pcm_data(wav_filename, output_filename):
    """Extract raw PCM data from WAV file."""
    with wave.open(wav_filename, 'r') as wav_file:
        # Get parameters
        nchannels = wav_file.getnchannels()
        sampwidth = wav_file.getsampwidth()
        framerate = wav_file.getframerate()
        nframes = wav_file.getnframes()
        
        print(f"WAV file info:")
        print(f"  Channels: {nchannels}")
        print(f"  Sample width: {sampwidth} bytes")
        print(f"  Frame rate: {framerate} Hz")
        print(f"  Number of frames: {nframes}")
        
        # Read all frames
        raw_data = wav_file.readframes(nframes)
        
        # Write raw PCM data
        with open(output_filename, 'wb') as f:
            f.write(raw_data)
        
        print(f"Extracted {len(raw_data)} bytes of PCM data to {output_filename}")
        return len(raw_data)

if __name__ == "__main__":
    wav_file = "test_sine_440hz_11025_8bit_mono.wav"
    raw_file = "test_sine_440hz_11025_8bit_mono.raw"
    extract_pcm_data(wav_file, raw_file)
