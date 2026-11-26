; Build: fasm idiom_flag_idioms.asm ../idiom_flag_idioms.bin
; a known flag (stc) folds into its conditional branch; a data-dependent compare
; (input read through CS so the value is opaque) keeps its flag tested in the branch.
use16

start:
    mov byte [0x0600], 0x01
    stc
    jc carry_taken
    mov byte [0x0601], 0x00
    jmp after_carry
carry_taken:
    mov byte [0x0601], 0x01
after_carry:
    mov si, input
    mov ax, [cs:si]
    cmp ax, 5
    ja above
    mov byte [0x0602], 0x00
    jmp done
above:
    mov byte [0x0602], 0x01
done:
    hlt

input: dw 7

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
