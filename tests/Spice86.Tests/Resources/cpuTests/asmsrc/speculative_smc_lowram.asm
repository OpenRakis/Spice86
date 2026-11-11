; Speculative guard merge fixture: the same mid-run self-modification shape as
; speculative_smc_guard, but inside a .com program so the guarded code lives in
; low RAM (DOS loads the program at segment 016Dh in this environment).
;
; The store loads DS with the literal load segment before writing: the alias
; analysis then proves the write's segment (a known-RAM range below 0A0000h)
; equal to the code segment and overlapping the guarded bytes, so only the
; disjointness condition can refuse merging the two guards around it. With the
; merge refused, the guard of the patched instruction fires and the run fails
; as untested, like speculative_smc_guard.
;
; Selector byte at absolute 0x0500 (read and written through segment 0):
;   0x00 = observed arm: JNZ not taken -> writes 0xDD
;   0x01 = unobserved arm: patches the ModRM byte of the next instruction ->
;   its guard fires
;
; Expected memory layout (observed arm):
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = 0xDD
;   [0x0402] = 0xAA (done marker)

org 100h
use16

start:
    mov ax, 0
    mov ds, ax
    mov byte [0x0400], 0x01
    mov al, byte [0x0500]
    test al, al
    jnz smc_path
    mov byte [0x0401], 0xDD
    jmp done

smc_path:
    mov ax, 016Eh
    mov ds, ax
    mov byte [smc_target + 1], 0EEh
smc_target:
    mov byte [0x0401], 0x00
    jmp done

done:
    mov ax, 0
    mov ds, ax
    mov byte [0x0402], 0xAA
    hlt
