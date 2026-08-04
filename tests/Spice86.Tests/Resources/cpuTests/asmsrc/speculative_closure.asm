; Speculative CFG test fixture T4: recursive closure (multi-block unobserved arm).
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: JNZ not taken -> fallthrough writes 0xDD
;   0x01 = speculative path: JNZ taken -> enters a 3-iteration loop
;
; Expected memory layout:
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = 0xDD (discovery) or 0x03 (loop counter after 3 iterations)
;   [0x0402] = 0xBB (done marker)

use16

start:
    mov byte [0x0400], 0x01

    mov al, byte [0x0500]
    test al, al
    jnz loop_path

    ; Discovery path: selector=0 -> JNZ not taken
    mov byte [0x0401], 0xDD
    jmp done

loop_path:
    ; Speculative path: multi-block loop, 3 iterations
    mov cx, 3
    mov byte [0x0401], 0x00

loop_body:
    inc byte [0x0401]
    dec cx
    jnz loop_body
    jmp done

done:
    mov byte [0x0402], 0xBB
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
