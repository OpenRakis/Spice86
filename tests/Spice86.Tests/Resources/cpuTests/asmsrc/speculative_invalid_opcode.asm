; Speculative CFG test fixture T8: decode-into-data / invalid opcode hard-stop.
;
; A conditional branch where one arm would decode into invalid opcodes (data region).
; The explorer must hard-stop at the invalid opcode and NOT speculate that path.
; The generated source must still have FailAsUntested for that arm.
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: JNZ not taken -> fallthrough writes 0xDD
;   0x01 = data path: JNZ taken -> jumps to address containing invalid opcodes (0xFF 0xFF)
;
; Expected memory layout:
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = 0xDD (discovery path result)
;   [0x0402] = 0xEE (done marker)

use16

start:
    mov byte [0x0400], 0x01

    mov al, byte [0x0500]
    test al, al
    jnz data_region

    ; Discovery path: selector=0 -> JNZ not taken
    mov byte [0x0401], 0xDD
    jmp done

data_region:
    ; This is a data region that happens to be the JNZ target.
    ; The explorer will decode 0xFF 0xFF as an invalid opcode and hard-stop.
    db 0xFF, 0xFF, 0xFF, 0xFF

done:
    mov byte [0x0402], 0xEE
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
