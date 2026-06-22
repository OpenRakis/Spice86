; Speculative CFG test fixture T9: discard-on-divergence (live reconciliation).
;
; Discovery explores a speculative path B. At runtime, path B is taken but the
; memory has been modified (via ConfigureMachine). The VerifySpeculativeEntryOrFail
; guard fires (memory changed), and the program falls back to FailAsUntested behavior.
; The interpreter then takes over and completes the program.
;
; This tests the live reconciliation path where a speculative decode is proven wrong.
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: JNZ not taken -> writes 0xDD
;   0x01 = speculative path: JNZ taken -> writes 0xEE
;
; For the discard scenario, the test modifies memory at the speculative target
; between discovery and the generated code run so the guard fires.
;
; Expected memory layout (discovery path):
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = 0xDD (discovery path result)
;   [0x0402] = 0xAA (done marker)

use16

start:
    mov byte [0x0400], 0x01

    mov al, byte [0x0500]
    test al, al
    jnz alt_path

    ; Discovery path: selector=0 -> JNZ not taken
    mov byte [0x0401], 0xDD
    jmp done

alt_path:
    ; Speculative path: selector=1 -> JNZ taken
    mov byte [0x0401], 0xEE
    jmp done

done:
    mov byte [0x0402], 0xAA
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
