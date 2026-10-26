; compile it with fasm
; Registers and flags where an interrupt handler runs: a divide error handler that reads AX and changes AX and CX,
; a timer interrupt delivered in a loop that keeps its values in registers (the handler saves what it uses and
; changes DF before iret restores the flags), a software interrupt that reads AH and returns AX, and DF set by
; popf and by a called function.
; DS = SS = 2000h. Pass p writes its results at offset 40h * p (physical 20000h + 40h * p).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:0000h], divide_error
    mov [es:0002h], cs
    mov word [es:8*4], timer_tick
    mov [es:8*4+2], cs
    mov word [es:60h*4], add_ah_to_al
    mov [es:60h*4+2], cs
    mov ax, ds
    mov es, ax
    cld
    mov byte [0E02h], 0
; PIT counter 0, mode 3, about 1 ms; all IRQs unmasked
    mov al, 00110110b
    out 43h, al
    mov al, 51h
    out 40h, al
    mov al, 22h
    out 40h, al
    mov al, 0
    out 21h, al
    sti
    xor bx, bx
    mov si, inputs
    mov word [0F00h], 2

pass_loop:
; a divide error handler that reads AX, changes AX and CX, and runs the division again
    mov word [0E00h], 0
    mov ax, [cs:si]
    mov cx, [cs:si+2]
    xor dx, dx
    add ax, 1
divide:
    div cx
    mov [bx+00h], ax
    mov [bx+02h], dx
    mov [bx+04h], cx
    mov ax, [0E00h]
    mov [bx+06h], ax

; a timer interrupt delivered in a loop that keeps its counter, pointer and value in registers
    mov cx, 3
    mov di, 0E10h
    mov ax, [cs:si+4]
wait_tick:
    cmp byte [0E02h], 0
    je wait_tick
    mov byte [0E02h], 0
    stosw
    add ax, 1
    loop wait_tick
    mov [bx+08h], di
    mov [bx+0Ah], ax
    mov [bx+0Ch], cx
    mov dx, [0E10h]
    mov [bx+0Eh], dx
    mov dx, [0E14h]
    mov [bx+10h], dx

; a software interrupt whose handler reads AH and returns a value in AX
    mov ah, [cs:si+6]
    mov al, 3
    int 60h
    mov [bx+12h], ax

; DF set by popf, then by a called function, then cleared for movsb in a loop
    std
    pushf
    cld
    popf
    mov di, 0E20h
    mov ax, [cs:si+8]
    stosw
    mov [bx+14h], di
    cld
    call set_direction
    mov di, 0E30h
    stosw
    mov [bx+16h], di
    cld
    push si
    mov si, 0E20h
    mov di, 0E40h
    mov cx, 2
copy_bytes:
    movsb
    loop copy_bytes
    pop si
    mov [bx+18h], di
    mov dx, [0E40h]
    mov [bx+1Ah], dx

    add bx, 40h
    add si, 0Ah
    dec word [0F00h]
    jnz pass_loop
    cli
    hlt

divide_error:
    push bp
    mov bp, sp
    mov [0E00h], ax
    mov cx, 7
    add ax, 10h
    mov word [bp+2], divide
    pop bp
    iret

timer_tick:
    push ax
    mov byte [0E02h], 1
    mov ax, 0DEADh
    std
    mov al, 20h
    out 20h, al
    pop ax
    iret

add_ah_to_al:
    add al, ah
    mov ah, 77h
    iret

set_direction:
    std
    ret

; per pass: dividend - 1, divisor, stored value, AH value, stored value
inputs:
    dw 0063h, 0000h, 1000h, 0005h, 0BEEFh
    dw 0013h, 0004h, 0FFFFh, 00FFh, 0102h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
