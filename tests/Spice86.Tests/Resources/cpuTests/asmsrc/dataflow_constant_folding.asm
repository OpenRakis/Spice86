; compile it with fasm
; Address and immediate arithmetic that a constant fold must keep exact: a displacement that wraps at 16 bits,
; negative displacements on a zero register, sign-extended 8-bit immediates, and the segment word of a far
; pointer read with a displacement.
; DS = SS = 2000h. Pass p writes its results at offset 40h * p (physical 20000h + 40h * p).
; Jump cases write 1 when the jump is taken and 2 when it is not.
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
    mov word [0F00h], 4
    mov word [96B4h], 5A5Ah
    mov word [0FFFEh], 6B6Bh

pass_loop:
; known words at offsets 0001h and 0013h of segment 2300h, and other words 10000h higher (segment 3300h)
    mov ax, 2300h
    mov es, ax
    mov word [es:0001h], 0A1A1h
    mov word [es:0013h], 0C3C3h
    mov ax, 3300h
    mov es, ax
    mov word [es:0001h], 0B2B2h
    mov word [es:0013h], 0D4D4h
    mov ax, 2300h
    mov es, ax

; a displacement added to a register offset wraps at 16 bits
    mov di, [cs:si]
    mov ax, [es:di+3]
    mov [bx+00h], ax

; the same with a constant register value
    mov di, 0FFFEh
    mov ax, [es:di+3]
    mov [bx+0Ch], ax
    push ds
    pop es

; a negative displacement added to a register known to be zero wraps at 16 bits
    mov di, 0
    mov ax, [di-26956]
    mov [bx+0Eh], ax
    mov ax, [di-2]
    mov [bx+10h], ax

; a sign-extended 8-bit immediate
    mov dx, [cs:si+2]
    add dx, -1
    mov [bx+02h], dx

; an 8-bit compare with 0F0h, signed then unsigned
    mov ax, [cs:si+2]
    mov byte [bx+04h], 1
    cmp al, 0F0h
    jl signed_byte_end
    mov byte [bx+04h], 2
signed_byte_end:
    mov byte [bx+05h], 1
    cmp al, 0F0h
    jb unsigned_byte_end
    mov byte [bx+05h], 2
unsigned_byte_end:

; a 16-bit compare with a sign-extended -2, signed then unsigned
    mov dx, [cs:si+2]
    mov byte [bx+06h], 1
    cmp dx, -2
    jg signed_word_end
    mov byte [bx+06h], 2
signed_word_end:
    mov byte [bx+07h], 1
    cmp dx, -2
    ja unsigned_word_end
    mov byte [bx+07h], 2
unsigned_word_end:

; a far pointer read with a displacement: the segment word is at the displacement + 2
    mov word [0E3Dh], 1357h
    mov word [0E3Fh], 2468h
    mov di, [cs:si+4]
    les dx, [di+3Dh]
    mov [bx+08h], dx
    mov [bx+0Ah], es
    push ds
    pop es

    add bx, 40h
    add si, 6
    dec word [0F00h]
    jnz pass_loop
    hlt

; per pass: register offset, compared value, far pointer base
inputs:
    dw 0FFFEh, 00F5h, 0E00h
    dw 0010h, 0080h, 0E00h
    dw 0FFFDh, 0FFFFh, 0E00h
    dw 0010h, 8000h, 0E00h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
