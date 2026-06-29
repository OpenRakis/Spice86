; Speculative CFG test fixture T2: mid-block SMC inside a speculative run triggers the guard.
;
; Discovery takes path A (fallthrough). Speculative path B contains two speculative
; instructions in the same block: the first rewrites a byte of the SECOND instruction's
; encoding (self-modifying code), the second is the instruction that gets modified.
;
; The write uses a CS segment override so it targets the code segment (CS=F000) rather
; than the data segment (DS=0): the program writes its data markers via DS:[0x04xx] which
; land in low RAM, so the SMC write must explicitly address CS to hit the code bytes.
;
; A single block-entry guard cannot catch this: at block entry the bytes still match what
; the explorer decoded, so the guard passes; the first instruction then mutates the second
; one, and an entry-only guard never re-checks. Only a guard emitted before each speculative
; instruction detects the divergence before the modified instruction executes.
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: JNZ not taken -> fallthrough writes 0xDD
;   0x01 = smc path: JNZ taken -> rewrites the next instruction's ModRM byte -> guard fires
;
; Expected memory layout (discovery path, selector=0):
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = 0xDD (discovery path result)
;   [0x0402] = 0xAA (done marker)
;
; When run with selector=1 via the generated override, the per-instruction guard fires and
; the program falls back to FailAsUntested behavior.

use16

start:
    mov byte [0x0400], 0x01

    mov al, byte [0x0500]
    test al, al
    jnz smc_path

    ; Discovery path: selector=0 -> JNZ not taken
    mov byte [0x0401], 0xDD
    jmp done

smc_path:
    ; Self-modifying code: write over the ModRM byte (smc_target + 1) of the next instruction
    ; in this same block. The CS override makes the write target the code segment (CS=F000),
    ; changing the bytes the speculative explorer decoded at explore time.
    mov byte [cs:smc_target + 1], 0xEE
smc_target:
    mov byte [0x0401], 0x00   ; ModRM byte at smc_target+1 is rewritten to 0xEE at runtime
    jmp done

done:
    mov byte [0x0402], 0xAA
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
