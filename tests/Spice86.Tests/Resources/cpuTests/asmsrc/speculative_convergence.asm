; Speculative CFG test fixture T6: convergence onto observed code (no duplicate).
;
; Two paths (A and B) both jump to the same target C. Discovery observes path A -> C.
; Path B is speculative. The generated code for path B must emit a goto to C's label.
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: first JNZ not taken -> path A taken (second JNZ taken) -> mergepoint
;   0x01 = speculative path: first JNZ taken -> path B -> mergepoint
;
; Expected memory layout:
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = 0xAA (path A marker) or 0xBB (path B marker)
;   [0x0402] = 0xCC (mergepoint marker - always reached)
;   [0x0403] = 0xFF (done marker)

use16

start:
    mov byte [0x0400], 0x01

    mov al, byte [0x0500]
    test al, al
    jnz path_b

    ; Discovery path: selector=0 -> first JNZ not taken -> path A
path_a:
    mov byte [0x0401], 0xAA
    jmp mergepoint

path_b:
    ; Speculative path: selector=1 -> first JNZ taken
    mov byte [0x0401], 0xBB
    jmp mergepoint

mergepoint:
    ; Both paths converge here. Discovery observed this block.
    mov byte [0x0402], 0xCC
    mov byte [0x0403], 0xFF
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
