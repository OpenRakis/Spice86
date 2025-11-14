# Makefile for RTC/CMOS integration tests
# Compiles assembly test files to DOS .COM format using NASM

# Assembler
ASM = nasm
ASMFLAGS = -f bin

# Source files
SOURCES = cmos_ports.asm bios_int1a.asm dos_int21h.asm bios_int15h_83h.asm

# Target files
TARGETS = $(SOURCES:.asm=.com)

# Default target: build all
all: $(TARGETS)

# Rule to build .com files from .asm files
%.com: %.asm
	$(ASM) $(ASMFLAGS) -o $@ $<

# Clean target: remove all compiled files
clean:
	rm -f $(TARGETS)

# Rebuild target: clean and build
rebuild: clean all

# Help target
help:
	@echo "RTC Test Makefile"
	@echo "Usage:"
	@echo "  make          - Build all test programs"
	@echo "  make clean    - Remove all compiled .com files"
	@echo "  make rebuild  - Clean and rebuild all"
	@echo "  make help     - Show this help message"

.PHONY: all clean rebuild help
