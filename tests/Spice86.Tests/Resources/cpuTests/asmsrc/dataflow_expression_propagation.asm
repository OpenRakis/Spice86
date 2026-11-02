; compile it with fasm
; Values written once and read once, with edges a merged expression must keep: a wrap before a shift, operands or
; memory changed before the reader, a pop, a call or an int before the reader, a value read twice, sub-registers,
; a segment register as the reader, flags still read, a join and a loop between the write and the reader.
; DS = SS = 2000h. Pass p writes its results at offset 40h * p (physical 20000h + 40h * p).
; Jump cases write 1 when the jump is taken and 2 when it is not.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:61h*4], set_ax_2222
    mov [es:61h*4+2], cs
    mov ax, ds
    mov es, ax
    cld
    xor bx, bx
    mov si, inputs
    mov word [0F00h], 2

pass_loop:
; add / shr / mov with the register still read later
    mov dx, [cs:si]
    add dx, 1
    shr dx, 1
    mov ax, dx
    mov [bx+00h], ax
    mov [bx+02h], dx

; the same with the register overwritten before any read
    mov dx, [cs:si]
    add dx, 1
    shr dx, 1
    mov ax, dx
    mov dx, 0
    mov [bx+04h], ax
    mov [bx+06h], dx

; the same with 8-bit registers
    mov al, [cs:si+2]
    add al, 80h
    shr al, 1
    mov cl, al
    mov [bx+08h], cl

; a signed shift
    mov dx, [cs:si]
    add dx, 1
    sar dx, 1
    mov cx, dx
    mov [bx+0Ah], cx

; an operand changed between the write and the reader
    mov dx, [cs:si+4]
    mov ax, dx
    inc dx
    mov cx, ax
    mov [bx+0Ch], cx
    mov [bx+0Eh], dx

; a memory read, then a write to the same address, then the reader
    mov di, 0E00h
    mov bp, 0E00h
    mov ax, [cs:si]
    mov [di], ax
    mov dx, [cs:si+4]
    mov ax, [di]
    mov [bp+0], dx
    mov cx, ax
    mov [bx+10h], cx
    mov ax, [0E00h]
    mov [bx+12h], ax

; a pop, then a push, then the reader
    mov cx, [cs:si]
    push cx
    pop ax
    push word 7777h
    mov dx, ax
    pop cx
    mov [bx+14h], dx
    mov [bx+16h], cx

; a call, then an int, between the write and the reader
    mov ax, [cs:si]
    add ax, 3
    call set_ax_1111
    mov cx, ax
    mov [bx+18h], cx
    mov ax, [cs:si]
    add ax, 3
    int 61h
    mov cx, ax
    mov [bx+1Ah], cx

; a value read twice
    mov ax, [cs:si]
    mov cx, [cs:si+4]
    add ax, cx
    mov dx, ax
    mov di, ax
    mov [bx+1Ch], dx
    mov [bx+1Eh], di

; sub-registers
    mov al, 5
    mov ah, 0
    mov cx, ax
    mov [bx+20h], cx
    mov dx, [cs:si+4]
    mov ax, dx
    mov al, 1
    mov cx, ax
    mov [bx+22h], cx

; a segment register as the reader, with AX read later
    mov ax, 2200h
    mov es, ax
    mov dx, [cs:si]
    mov [es:0000h], dx
    push ds
    pop es
    mov ax, 2200h
    mov ds, ax
    mov cx, [0000h]
    mov dx, ax
    mov ax, 2000h
    mov ds, ax
    mov [bx+24h], cx
    mov [bx+26h], dx

; the flags of the shift still read
    mov dx, [cs:si+4]
    add dx, 1
    mov byte [bx+28h], 1
    shr dx, 1
    jc shift_carry_end
    mov byte [bx+28h], 2
shift_carry_end:
    mov [bx+2Ah], dx

; a write before a join and its reader after the join
    mov ax, [cs:si]
    add ax, 2
    mov cx, [cs:si+6]
    jcxz written_before_join
    mov dx, 5
written_before_join:
    mov di, ax
    mov [bx+2Ch], di

; a write inside a loop read in the next iteration
    mov cx, 3
    xor ax, ax
    xor dx, dx
read_next_iteration:
    add dx, ax
    mov ax, cx
    loop read_next_iteration
    mov [bx+2Eh], dx
    mov [bx+30h], ax

    add bx, 40h
    add si, 8
    dec word [0F00h]
    jnz pass_loop
    hlt

set_ax_1111:
    mov ax, 1111h
    ret

set_ax_2222:
    mov ax, 2222h
    iret

; per pass: v, byte value, w, path selector
inputs:
    dw 0FFFFh, 0080h, 0ABCDh, 0000h
    dw 7FFFh, 0012h, 00FEh, 0001h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
