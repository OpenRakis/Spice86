; Speculative CFG test fixture T5: direct call entry on speculative path.
;
; Discovery takes path A (fallthrough). Speculative path B contains a call near
; to a subroutine that was never called during discovery. The subroutine is pure
; code (no SMC) and returns normally.
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: JNZ not taken -> fallthrough writes 0xDD
;   0x01 = speculative path: JNZ taken -> calls subroutine -> writes return value
;
; Expected memory layout:
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = 0xDD (discovery) or 0x42 (speculative: subroutine result)
;   [0x0402] = 0xAA (done marker)

use16

start:
    mov byte [0x0400], 0x01

    mov al, byte [0x0500]
    test al, al
    jnz call_path

    ; Discovery path: selector=0 -> JNZ not taken
    mov byte [0x0401], 0xDD
    jmp done

call_path:
    ; Speculative path: selector=1 -> calls subroutine
    call subroutine
    mov byte [0x0401], al
    jmp done

subroutine:
    ; Pure code subroutine, never called during discovery.
    ; Returns 0x42 in AL.
    mov al, 0x42
    ret

done:
    mov byte [0x0402], 0xAA
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
