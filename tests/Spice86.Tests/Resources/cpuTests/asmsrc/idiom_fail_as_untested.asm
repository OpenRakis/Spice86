; Build: fasm idiom_fail_as_untested.asm ../idiom_fail_as_untested.bin
use16

start:
    mov byte [0x0400], 0x01
    mov al, byte [0x0500]
    test al, al
    jnz never_taken
    mov byte [0x0401], 0xDD
    cmp al, 7
    je indirect
    hlt
indirect:
    mov bx, never_taken
    jmp bx
never_taken:
    mov byte [0x0401], 0xEE
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
