; Speculative CFG test fixture: unconditional direct transfers into data.
;
; Four arms the discovery run never takes end in a near jump, a near call, a far jump and a far
; call to the same data region. The explorer decodes each transfer, finds invalid opcodes at the
; target, poisons it and leaves the transfer without a successor. Code generation must still emit
; the four transfers, failing each one as untested, instead of aborting.
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: no JNZ is taken
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
    jnz near_jump_arm
    test al, al
    jnz near_call_arm
    test al, al
    jnz far_jump_arm
    test al, al
    jnz far_call_arm

    ; Discovery path: selector=0 -> no arm taken
    mov byte [0x0401], 0xDD
    jmp done

near_jump_arm:
    jmp data_region

near_call_arm:
    call data_region
    jmp done

far_jump_arm:
    jmp 0F000h:data_region

far_call_arm:
    call 0F000h:data_region
    jmp done

data_region:
    ; Not code: 0xFF 0xFF decodes as an invalid opcode.
    db 0xFF, 0xFF, 0xFF, 0xFF

done:
    mov byte [0x0402], 0xEE
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
