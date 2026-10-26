; compile it with fasm
; Sub-registers written and the whole register read, and segment registers used again after a call.
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
; AL written, AX read
    mov ax, [cs:si]
    mov al, [cs:si+2]
    mov [bx+00h], ax

; AH written, AX read
    mov ax, [cs:si]
    mov ah, [cs:si+3]
    mov [bx+02h], ax

; AX written, EAX read: the upper half is kept
    mov eax, [cs:si+4]
    mov ax, [cs:si]
    mov [bx+04h], eax

; the two halves of AX exchanged
    mov ax, [cs:si+2]
    xchg al, ah
    mov [bx+08h], ax

; AX written as a whole, then AH, then AX read
    mov ax, [cs:si]
    add ax, 1
    mov ah, 7
    mov cx, ax
    mov [bx+0Ah], cx

; AX copied from DX, AL written, AX read: the high byte of DX stays
    mov dx, [cs:si+2]
    mov ax, dx
    mov al, 1
    mov cx, ax
    mov [bx+0Ch], cx

; DS set, used for a memory access and read again after a call
    push ds
    mov ax, 2200h
    mov ds, ax
    mov ax, [cs:si]
    mov [0000h], ax
    call do_nothing
    mov cx, [0000h]
    mov dx, ds
    pop ds
    mov [bx+0Eh], cx
    mov [bx+10h], dx

; ES set, used for a memory access and read again after a call
    mov ax, 2200h
    mov es, ax
    mov ax, [cs:si+2]
    mov [es:0002h], ax
    call do_nothing
    mov cx, [es:0002h]
    mov dx, es
    push ds
    pop es
    mov [bx+12h], cx
    mov [bx+14h], dx

    add bx, 40h
    add si, 8
    dec word [0F00h]
    jnz pass_loop
    hlt

do_nothing:
    ret

; per pass: w0, w1, d (32 bits)
inputs:
    dw 1234h, 0ABCDh
    dd 89ABCDEFh
    dw 00FFh, 0FF00h
    dd 00000001h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
