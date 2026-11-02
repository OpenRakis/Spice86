; compile it with fasm
; Flags whose setter is not one adjacent instruction: flags from two instructions, a join of two setters, an operand
; changed on one path, a memory operand changed after the compare, flags read as a value, a carry returned by a
; function, and a carry read in another block.
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

pass_loop:
; CF from the cmp, ZF from the inc
    mov ax, [cs:si]
    mov cx, [cs:si+2]
    mov dx, [cs:si+4]
    mov byte [bx+00h], 1
    cmp ax, cx
    inc dx
    jbe flags_from_two_end
    mov byte [bx+00h], 2
flags_from_two_end:
    mov [bx+02h], dx

; a jump after a join of two paths that set the flags differently
    mov di, [cs:si+6]
    test di, di
    jz reversed_compare
    cmp ax, cx
    jmp compare_join
reversed_compare:
    cmp cx, ax
compare_join:
    mov byte [bx+04h], 1
    jb compare_join_end
    mov byte [bx+04h], 2
compare_join_end:

; an operand of the cmp changed on one path only, the flags unchanged
    mov cx, [cs:si+6]
    mov dx, [cs:si+2]
    cmp ax, dx
    jcxz operand_join
    mov ax, dx
operand_join:
    mov byte [bx+05h], 1
    jb operand_join_end
    mov byte [bx+05h], 2
operand_join_end:
    mov [bx+06h], ax

; a memory operand changed between the cmp and the jump
    mov ax, [cs:si]
    mov [0E00h], ax
    cmp byte [0E00h], 5
    mov byte [0E00h], 9
    mov byte [bx+08h], 1
    je memory_changed_end
    mov byte [bx+08h], 2
memory_changed_end:

; the flags of a cmp read as a value (O, S, Z, A, P and C kept)
    mov cx, [cs:si+2]
    cmp ax, cx
    pushf
    pop dx
    and dx, 08D5h
    mov [bx+0Ah], dx

; a carry returned by a function
    call below_as_carry
    mov byte [bx+0Ch], 1
    jc returned_carry_end
    mov byte [bx+0Ch], 2
returned_carry_end:

; a carry read by adc in another block
    mov dx, [cs:si+4]
    add ax, cx
    jcxz carry_join
    mov dx, 1000h
carry_join:
    adc dx, 0
    mov [bx+0Eh], dx
    mov [bx+10h], ax

    add bx, 40h
    add si, 8
    dec word [0F00h]
    jnz pass_loop
    hlt

below_as_carry:
    cmp ax, cx
    jb below_as_carry_set
    clc
    ret
below_as_carry_set:
    stc
    ret

; per pass: a, b, c, path selector
inputs:
    dw 0001h, 0002h, 0003h, 0001h
    dw 0005h, 0002h, 0FFFFh, 0000h
    dw 8009h, 8009h, 1234h, 0000h
    dw 0FFFFh, 0000h, 0000h, 0001h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
