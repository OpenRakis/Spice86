use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h

    ; Indirect near call via BX (FF /2 - Grp5 group index 2)
    mov bx, indirect_target
    call bx

    ; Indirect near jump via BX (FF /4 - Grp5 group index 4)
    mov bx, jump_target
    jmp bx

indirect_target:
    mov ax, 1111h
    ret

jump_target:
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
