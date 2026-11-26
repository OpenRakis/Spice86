; Build: fasm idiom_stack_frames.asm ../idiom_stack_frames.bin
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 3
    call framed
    mov byte [0x0601], al
    hlt
framed:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    add ax, 0x40
    mov sp, bp
    pop bp
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
