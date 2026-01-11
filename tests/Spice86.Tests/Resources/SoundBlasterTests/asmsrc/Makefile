# Makefile for Sound Blaster DMA Tests
# Assembles ASM files to binary format for testing

ASM_FILES = sb_dsp_test.asm sb_dma_8bit_single.asm sb_dma_8bit_autoinit.asm sb_dma_16bit_single.asm \
            opl_simple_tone.asm opl_rhythm_mode.asm adlib_gold_stereo.asm
BIN_FILES = $(ASM_FILES:.asm=.bin)

all: $(BIN_FILES)

%.bin: %.asm
	nasm -f bin -o $@ $<

clean:
	rm -f *.bin

.PHONY: all clean
