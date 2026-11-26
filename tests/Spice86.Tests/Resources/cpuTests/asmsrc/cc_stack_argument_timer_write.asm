use16
start:
    cli
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:8*4], timer_handler
    mov [es:8*4+2], cs
    mov byte [0302h], 0
    mov al, 0FFh
    out 0A1h, al
    mov al, 30h
    out 43h, al
    mov al, 0A9h
    out 40h, al
    mov al, 04h
    out 40h, al
    mov al, 0FEh
    out 21h, al
    push 1111h
    call read_across_timer
    add sp, 2
    mov [0200h], ax
    mov [0204h], sp
    cld
    mov ax, ss
    mov es, ax
    mov di, 0FE0h
    mov cx, 10h
    xor ax, ax
    rep stosw
    hlt
read_across_timer:
    push bp
    mov bp, sp
    mov dx, [bp+4]
    mov [0300h], bp
    sti
.wait_tick:
    cmp byte [0302h], 0
    je .wait_tick
    cli
    mov ax, [bp+4]
    mov [0202h], ax
    add ax, dx
    pop bp
    ret
timer_handler:
    push ax
    push bx
    mov bx, [0300h]
    mov word [ss:bx+4], 9999h
    mov byte [0302h], 1
    mov al, 20h
    out 20h, al
    pop bx
    pop ax
    iret
rb 0FFF0h-$
    jmp start
rb 0FFFFh-$
    db 0FFh
