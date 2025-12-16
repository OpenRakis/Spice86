# Makefile for RTC/CMOS integration tests
# Compiles assembly test files to DOS .COM format using NASM
#
# Prerequisites:
#   - Install NASM (Netwide Assembler): https://www.nasm.us/
#   - Linux/Mac: sudo apt-get install nasm  (Debian/Ubuntu) or brew install nasm (macOS)
#   - Windows: Download from https://www.nasm.us/pub/nasm/releasebuilds/ and add to PATH
#
# Usage:
#   make          - Build all .asm files to .com files
#   make clean    - Remove all compiled .com files
#   make rebuild  - Clean and rebuild all files
#   make help     - Show detailed usage information

# Assembler
ASM = nasm
ASMFLAGS = -f bin

# Source files
SOURCES = cmos_ports.asm bios_int1a.asm dos_int21h.asm bios_int15h_83h.asm bios_int70_wait.asm

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
