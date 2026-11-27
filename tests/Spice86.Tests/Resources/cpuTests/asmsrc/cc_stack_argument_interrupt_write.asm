use16
start:
    cli
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:60h*4], rewrite_argument
    mov [es:60h*4+2], cs
    push 1111h
    call read_across_interrupt
    add sp, 2
    mov [0200h], ax
    mov [0204h], sp
    hlt
read_across_interrupt:
    push bp
    mov bp, sp
    mov dx, [bp+4]
    int 60h
    mov ax, [bp+4]
    mov [0202h], ax
    add ax, dx
    pop bp
    ret
rewrite_argument:
    mov bx, sp
    mov word [ss:bx+0Ah], 9999h
    iret
rb 0FFF0h-$
    jmp start
rb 0FFFFh-$
    db 0FFh
