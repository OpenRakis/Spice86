; compile it with fasm
; Save / restore pairs, stack parameters, and pushed stack slots read or written in other ways.
; DS = SS = 2000h. Pass p writes its results at offset 40h * p (physical 20000h + 40h * p).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx
    mov si, inputs
    mov word [0F00h], 2

pass_loop:
; push AX / push ES around a region that uses both, then pop both
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov es, dx
    push ax
    push es
    mov ax, 1
    mov es, ax
    mov cx, es
    add cx, ax
    pop es
    pop ax
    mov [bx+00h], ax
    mov [bx+02h], es
    mov [bx+04h], cx

; a push that passes a parameter, read by the callee through BP, and a pop that only cleans up
    mov ax, [cs:si+4]
    push ax
    call read_parameter
    pop cx
    mov [bx+06h], dx
    mov [bx+08h], cx

; the pushed slot read through a BP set after the push
    mov ax, [cs:si+6]
    push ax
    push bp
    mov bp, sp
    mov dx, [bp+2]
    pop bp
    pop ax
    mov [bx+0Ah], dx

; SP read between a push and its pop
    push ax
    mov cx, sp
    pop ax
    mov [bx+0Ch], cx

; the pushed slot overwritten through SS:BP before the pop
    mov ax, [cs:si+8]
    push ax
    mov bp, sp
    mov word [bp], 5A5Ah
    pop ax
    mov [bx+0Eh], ax

; the pushed slot overwritten through DS (DS = SS) before the pop
    mov ax, [cs:si+8]
    push ax
    mov word [0FFEh], 6B6Bh
    pop dx
    mov [bx+10h], dx

; two paths from one push that pop into different registers
    mov ax, [cs:si+0Ah]
    mov cx, [cs:si+0Ch]
    xor dx, dx
    xor di, di
    push ax
    test cx, cx
    jz pop_into_di
    pop dx
    jmp popped
pop_into_di:
    pop di
popped:
    mov [bx+12h], dx
    mov [bx+14h], di

; ES set inside a save / restore region and read by a called function
    push es
    mov ax, [cs:si+0Eh]
    mov es, ax
    call read_es
    pop es
    mov [bx+16h], dx

; a function that leaves its push region through two returns
    mov ax, [cs:si+0Ch]
    call two_returns
    mov [bx+18h], dx

    add bx, 40h
    add si, 10h
    dec word [0F00h]
    jnz pass_loop
    hlt

read_parameter:
    push bp
    mov bp, sp
    mov dx, [bp+4]
    add dx, 3
    pop bp
    ret

read_es:
    mov dx, es
    ret

two_returns:
    push ax
    test ax, ax
    jz two_returns_zero
    pop dx
    add dx, 10h
    ret
two_returns_zero:
    pop dx
    add dx, 20h
    ret

; per pass: AX value, ES value, parameter, slot value, overwritten slot value, popped value, path selector, ES value
inputs:
    dw 1234h, 3000h, 0100h, 0BEEFh, 1111h, 2222h, 0001h, 4000h
    dw 0FFFFh, 5000h, 0FFFEh, 0001h, 3333h, 4444h, 0000h, 6000h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
