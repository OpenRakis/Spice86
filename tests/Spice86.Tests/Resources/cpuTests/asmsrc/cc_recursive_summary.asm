; compile it with fasm
; Probe: recursive callees and BP frames. DS = SS = 2000h, SP = 1000h.
; R1 fact(4) recursive with a BP frame; R2 a BP-framed caller of fact; R3 the same caller shape
; calling a non-recursive leaf (negative twin); R4 a BP-framed caller of mutually recursive is_even/is_odd;
; R5 r5_even/r5_odd mutually recursive callees with BP frames, result [48h].
; Results: [40h]=0018h [42h]=0009h [44h]=0009h [46h]=0005h [48h]=0001h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    push word 4
    call fact
    add sp, 2
    mov [40h], ax
    push word 3
    call r2_outer
    add sp, 2
    mov [42h], ax
    push word 3
    call r3_outer
    add sp, 2
    mov [44h], ax
    push word 5
    call r4_outer
    add sp, 2
    mov [46h], ax
    push word 4
    call r5_even
    add sp, 2
    mov [48h], ax
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

fact:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    cmp ax, 1
    jbe fact_done
    dec ax
    push ax
    call fact
    add sp, 2
    mul word [bp+4]
fact_done:
    pop bp
    ret

r2_outer:
    push bp
    mov bp, sp
    push word [bp+4]
    call fact
    add sp, 2
    add ax, [bp+4]
    pop bp
    ret

leaf:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    add ax, ax
    pop bp
    ret

r3_outer:
    push bp
    mov bp, sp
    push word [bp+4]
    call leaf
    add sp, 2
    add ax, [bp+4]
    pop bp
    ret

r4_outer:
    push bp
    mov bp, sp
    mov cx, [bp+4]
    call is_even
    add ax, [bp+4]
    pop bp
    ret

is_even:
    test cx, cx
    jz is_even_yes
    dec cx
    call is_odd
    ret
is_even_yes:
    mov ax, 1
    ret

is_odd:
    test cx, cx
    jz is_odd_no
    dec cx
    call is_even
    ret
is_odd_no:
    xor ax, ax
    ret

r5_even:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    test ax, ax
    jz r5_even_yes
    dec ax
    push ax
    call r5_odd
    add sp, 2
    add ax, [bp+4]
    sub ax, [bp+4]
    pop bp
    ret
r5_even_yes:
    mov ax, 1
    pop bp
    ret

r5_odd:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    test ax, ax
    jz r5_odd_no
    dec ax
    push ax
    call r5_even
    add sp, 2
    add ax, [bp+4]
    sub ax, [bp+4]
    pop bp
    ret
r5_odd_no:
    xor ax, ax
    pop bp
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
