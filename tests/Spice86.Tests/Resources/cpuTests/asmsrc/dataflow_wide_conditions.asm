; compile it with fasm
; Wide conditions: the two/three test decision trees of a 32-bit compare fold into one packed
; comparison, and a range subtraction inline folds into `x is < lo or > hi` / `x is >= lo and <=
; hi` printing. DS = SS = 2000h, SP = 1000h. Pass p writes its results at offset 40h * p
; (physical 20000h + 40h * p). Input pairs (X in DX:AX, Y in CX:BP) and the byte Z are read
; from the CS tables each section. Every section ends with `cmp di,di` to kill all status
; flags.
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
    mov di, zvals

pass_loop:
; 00: signed three-way -> (int)X < (int)Y
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov bp, [cs:si+4]
    mov cx, [cs:si+6]
    cmp dx, cx
    jl .t00
    jg .f00
    cmp ax, bp
    jb .t00
.f00:
    mov byte [bx+00h], 0
    jmp .j00
.t00:
    mov byte [bx+00h], 1
.j00:
    cmp di, di

; 01: unsigned three-way -> X < Y
    cmp dx, cx
    jb .t01
    ja .f01
    cmp ax, bp
    jb .t01
.f01:
    mov byte [bx+01h], 0
    jmp .j01
.t01:
    mov byte [bx+01h], 1
.j01:
    cmp di, di

; 02: two JNEs -> X != Y
    cmp dx, cx
    jne .t02
    cmp ax, bp
    jne .t02
    mov byte [bx+02h], 0
    jmp .j02
.t02:
    mov byte [bx+02h], 1
.j02:
    cmp di, di

; 03: two JE paths -> X == Y
    cmp dx, cx
    jne .f03
    cmp ax, bp
    jne .f03
    mov byte [bx+03h], 1
    jmp .j03
.f03:
    mov byte [bx+03h], 0
.j03:
    cmp di, di

; 04: signed three-way against constant 00010001h -> (int)X < 65537
    cmp dx, 1
    jl .t04
    jg .f04
    cmp ax, 1
    jb .t04
.f04:
    mov byte [bx+04h], 0
    jmp .j04
.t04:
    mov byte [bx+04h], 1
.j04:
    cmp di, di

; 05: or ax,dx; jz -> X == 0; ax is overwritten before the join
    or ax, dx
    jz .t05
    mov byte [bx+05h], 0
    jmp .j05
.t05:
    mov byte [bx+05h], 1
.j05:
    mov ax, [cs:si]
    cmp di, di

; 06: sub al,'a'; cmp al,25; ja -> z < 'a' || z > 'z'; AL dead after
    mov al, [cs:di]
    sub al, 'a'
    cmp al, 25
    ja .t06
    mov byte [bx+06h], 0
    jmp .j06
.t06:
    mov byte [bx+06h], 1
.j06:
    cmp di, di

; 07: two compares -> z >= 'a' && z <= 'z'
    mov al, [cs:di]
    cmp al, 'a'
    jb .f07
    cmp al, 'z'
    ja .f07
    mov byte [bx+07h], 1
    jmp .j07
.f07:
    mov byte [bx+07h], 0
.j07:
    cmp di, di

; 08: unsigned cmp al,1; jb -> z == 0
    mov al, [cs:di]
    cmp al, 1
    jb .t08
    mov byte [bx+08h], 0
    jmp .j08
.t08:
    mov byte [bx+08h], 1
.j08:
    cmp di, di

; 09: TRAP - a sentinel store sits inside the inner test block: the unsigned
; three-way must NOT fold away the block and must still run the store exactly when
; the high halves are equal.
    mov byte [bx+0Ah], 0
    mov ax, [cs:si]
    cmp dx, cx
    jb .t09
    ja .f09
    mov byte [bx+0Ah], 1
    cmp ax, bp
    jb .t09
.f09:
    mov byte [bx+09h], 0
    jmp .j09
.t09:
    mov byte [bx+09h], 1
.j09:
    cmp di, di

; 0B: TRAP - the range subtraction value stays live after the branch, so the
; subtraction cannot inline: same boolean as 06 plus the stored AL bits.
    mov al, [cs:di]
    sub al, 'a'
    cmp al, 25
    ja .t0b
    mov byte [bx+0Bh], 0
    jmp .j0b
.t0b:
    mov byte [bx+0Bh], 1
.j0b:
    mov [bx+0Ch], al        ;; z - 'a'
    cmp di, di

; 0D: TRAP - the low compare's flags are observed by lahf inside the inner block:
; the three-way must NOT fold them away. 0Eh = the captured flag byte.
    mov ax, [cs:si]
    mov byte [bx+0Eh], 0
    cmp dx, cx
    jb .t0d
    ja .f0d
    cmp ax, bp
    lahf
    mov [bx+0Eh], ah        ;; SF:ZF:-:AF:-:PF:1:CF
    jb .t0d
.f0d:
    mov byte [bx+0Dh], 0
    jmp .j0d
.t0d:
    mov byte [bx+0Dh], 1
.j0d:
    cmp di, di

; 0F: signed three-way against constant 80000001h -> (int)X < (int)0x80000001
    cmp dx, 8000h
    jl .t0f
    jg .f0f
    cmp ax, 1
    jb .t0f
.f0f:
    mov byte [bx+0Fh], 0
    jmp .j0f
.t0f:
    mov byte [bx+0Fh], 1
.j0f:
    cmp di, di

; 10: signed three-way against constant FFFF8000h -> (int)X < (int)0xFFFF8000
    cmp dx, 0FFFFh
    jl .t10
    jg .f10
    cmp ax, 8000h
    jb .t10
.f10:
    mov byte [bx+10h], 0
    jmp .j10
.t10:
    mov byte [bx+10h], 1
.j10:
    cmp di, di

    add bx, 40h
    add si, 8
    add di, 1
    cmp bx, 7 * 40h
    jb pass_loop
    hlt

align 8
inputs:
    dd 00000000h, 00000000h
    dd 00000001h, 00000000h
    dd 00000000h, 00000001h
    dd 0FFFFFFFFh, 00000000h
    dd 080000000h, 07FFFFFFFh
    dd 000010000h, 00000FFFFh
    dd 012345678h, 012345678h
zvals:
    db 60h, 61h, 7Ah, 7Bh, 0FFh, 00h, 70h

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
