; Build: fasm idiom_call_sites.asm ../idiom_call_sites.bin
use16

start:
    mov byte [0x0400], 0x01
    push 0x0042
    call with_arg
    add sp, 2
    mov byte [0x0401], al
    call discarded
    db 0x66
    call wide32
    mov byte [0x0402], al
    hlt
with_arg:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    pop bp
    ret
discarded:
    mov ax, 0x9999
    ret
wide32:
    mov eax, 0x10
    mov ax, 0x55
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
