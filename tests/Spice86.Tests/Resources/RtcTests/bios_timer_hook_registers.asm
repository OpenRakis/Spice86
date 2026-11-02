; Build: nasm -f bin bios_timer_hook_registers.asm -o bios_timer_hook_registers.com
; The BIOS timer must shield interrupted AX, DX and DS from the INT 1Ch hook.

    org 0x100
    bits 16

    RESULT_PORT equ 0x999
    DETAILS_PORT equ 0x998
    ITERATIONS equ 3

start:
    cli
    push cs
    pop ds
    xor ax, ax
    mov es, ax
    mov word [es:0x1c * 4], timer_hook
    mov [es:0x1c * 4 + 2], cs
    mov ax, 0x40
    mov es, ax
    mov ax, [es:0x6c]
    mov [initial_ticks], ax
    mov si, sp
    mov cx, ITERATIONS

timer_loop:
    mov ax, 152
    mov dx, 0xabcd
    std
    stc
    int 0x08
    pushf
    pop bp
    cmp ax, 152
    jne failed
    cmp dx, 0xabcd
    jne failed
    mov bx, ds
    mov ax, cs
    cmp bx, ax
    jne failed
    cmp sp, si
    jne failed
    and bp, 0x401
    cmp bp, 0x401
    jne failed
    loop timer_loop

    cld
    mov dx, DETAILS_PORT
    mov al, 1
    out dx, al
    cmp word [hook_calls], ITERATIONS
    jne failed
    mov ax, [es:0x6c]
    sub ax, [initial_ticks]
    cmp ax, ITERATIONS
    jne failed
    mov al, 2
    out dx, al
    mov dx, RESULT_PORT
    xor al, al
    out dx, al
    hlt

failed:
    cld
    mov dx, RESULT_PORT
    mov al, 0xff
    out dx, al
    hlt

timer_hook:
    inc word [cs:hook_calls]
    xor ax, ax
    mov dx, 0x1234
    mov ds, dx
    cld
    iret

initial_ticks dw 0
hook_calls dw 0
