use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor dx, dx
    xor di, di
    mov cx, 8
far_again:
    mov bx, cx
    dec bx
    shl bx, 2
    call dword [cs:far_table+bx]
    loop far_again
    hlt
far_a:
    add dx, 1
    retf
far_b:
    add dx, 10h
    retf
far_c:
    add dx, 100h
    retf
far_d:
    add dx, 1000h
    retf
; X, called as F000:far_x
far_x:
    push cs
    pop ax
    mov [di], ax
    mov word [di+2], 1000h
    add di, 4
    retf
far_table:
    dw far_a, 0F000h, far_b, 0F000h, far_c, 0F000h, far_d, 0F000h
    dw far_x, 0F000h, far_x, 0F010h, far_z, 0F000h, far_z - 10h, 0F001h

; Y, physically at F000:far_x+100h, called as F010:far_x
rb far_x + 100h - $
far_y:
    push cs
    pop ax
    mov [di], ax
    mov word [di+2], 2000h
    add di, 4
    retf
; Z, called as both F000:far_z and F001:far_z-10h (same physical address)
far_z:
    push cs
    pop ax
    mov [di], ax
    mov word [di+2], 3000h
    add di, 4
    retf

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
