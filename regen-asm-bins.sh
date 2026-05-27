#!/usr/bin/env bash
# Regenerates all committed assembly test binaries for Spice86.
# Requires: fasm, nasm
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
RESOURCES="$REPO_ROOT/tests/Spice86.Tests/Resources"

errors=0
built=0

build() {
    local dir="$1"
    shift
    echo "[BUILD] ($dir) $*"
    if (cd "$dir" && eval "$@"); then
        built=$((built + 1))
    else
        echo "[ERROR] Failed in $dir: $*" >&2
        errors=$((errors + 1))
    fi
}

# Extract the assembler command (fasm or nasm) from the first 30 lines of an ASM file.
# Matches common embedded patterns:
#   ; Build: fasm <in> <out>
#   ; build: nasm -f bin <in> -o <out>
#   ; NASM syntax: nasm ...
#   ; NASM syntax, assembled with: nasm ...
#   ; Assemble:  nasm ...
#   ; To assemble: nasm ...
#   ; assembled with: nasm ...
extract_build_cmd() {
    local asmfile="$1"
    head -30 "$asmfile" | grep -Eo '(fasm|nasm) [^ ].*' | head -1 || true
}

# --- Files with embedded build comments ---
# Process all ASM files except those in asmsrc/ subdirs, NativeDosTests (Watcom,
# no committed binaries), and exec_test.asm (documentation-only, no binary).
while IFS= read -r asmfile; do
    if [[ "$asmfile" == */NativeDosTests/* ]]; then
        continue
    fi
    if [[ "$asmfile" == */DosExecTests/exec_test.asm ]]; then
        continue
    fi
    if [[ "$asmfile" == */asmsrc/* ]]; then
        continue
    fi

    dir="$(dirname "$asmfile")"
    cmd="$(extract_build_cmd "$asmfile")"
    if [[ -n "$cmd" ]]; then
        build "$dir" "$cmd"
    fi
done < <(find "$RESOURCES" -name "*.asm" -not -path "*/bin/*" -not -path "*/obj/*" -type f)

# --- DosExecTests: files with no embedded build comment ---
build "$RESOURCES/DosExecTests" "nasm -f bin tsr_hook.asm     -o tsr_hook.com"
build "$RESOURCES/DosExecTests" "nasm -f bin dos_child.asm    -o child.com"
build "$RESOURCES/DosExecTests" "nasm -f bin overlay_driver.asm -o overlay_driver.bin"

# --- VgaTests ---
build "$RESOURCES/VgaTests" "nasm -f bin scroll_up_clears_full_row.asm -o scroll_up_clears_full_row.com"

# --- DosReallocTest: all *.asm -> *.com ---
for asmfile in "$RESOURCES/DosReallocTest"/*.asm; do
    stem="$(basename "$asmfile" .asm)"
    build "$RESOURCES/DosReallocTest" "nasm -f bin ${stem}.asm -o ${stem}.com"
done

# tsr22h.asm: build comment references a renamed file; use correct command
build "$RESOURCES/DosTsrTests" "nasm -f bin tsr22h.asm -o tsr22h.com"

# --- RtcTests: Makefile-driven (nasm) ---
build "$RESOURCES/RtcTests" "make --no-print-directory"

# --- cpuTests/asmsrc/*.asm -> ../stem.bin via fasm ---
# Only process files that have a corresponding committed binary in the parent dir.
# add_code.asm has no binary and is excluded.
for asmfile in "$RESOURCES/cpuTests/asmsrc"/*.asm; do
    # test386.asm is a directory with its own Makefile - handled below
    [[ -d "$asmfile" ]] && continue
    stem="$(basename "$asmfile" .asm)"
    if [[ ! -f "$RESOURCES/cpuTests/${stem}.bin" ]]; then
        continue
    fi
    build "$RESOURCES/cpuTests/asmsrc" "fasm ${stem}.asm ../${stem}.bin"
done

# cpuTests/asmsrc/test386.asm/ is a directory with its own nasm Makefile.
# It produces test386.bin which belongs in cpuTests/.
# We invoke nasm directly (without -l) to avoid regenerating the .lst file.
build "$RESOURCES/cpuTests/asmsrc/test386.asm" \
    "nasm -i./src/ -f bin src/test386.asm -w-all -o ../../test386.bin"

# --- emsTests/asmsrc/*.asm -> ../stem.bin via fasm ---
for asmfile in "$RESOURCES/emsTests/asmsrc"/*.asm; do
    stem="$(basename "$asmfile" .asm)"
    if [[ ! -f "$RESOURCES/emsTests/${stem}.bin" ]]; then
        continue
    fi
    build "$RESOURCES/emsTests/asmsrc" "fasm ${stem}.asm ../${stem}.bin"
done

# --- Summary ---
echo ""
echo "Built: $built  Errors: $errors"
if [[ $errors -gt 0 ]]; then
    exit 1
fi
