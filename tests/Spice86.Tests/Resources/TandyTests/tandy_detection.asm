; Original probe of the firmware checks observed at OutRun 0401:6BE9.
; No game instructions or data are embedded.
bits 16
org 0x100

    mov ax, 0xFFFF
    mov es, ax
    cmp byte [es:0x000E], 0xFF
    jne not_tandy
    mov ax, 0xFC00
    mov es, ax
    cmp byte [es:0], 0x21
    jne not_tandy
    mov ah, 0xC0
    int 0x15
    jc fail
    cmp ah, 0
    jne fail
    cmp word [es:bx], 8
    jne fail
    cmp byte [es:bx+2], 0xFF
    jne fail
    cmp byte [es:bx+3], 0x0A
    jne fail
    cmp byte [es:bx+5], 0x70
    jne fail
    int 0x12
    cmp ax, 624
    jne fail
    mov ax, 0x40
    mov es, ax
    cmp word [es:0x15], 640
    jne fail
    mov al, 1
    jmp report
not_tandy:
    xor al, al
report:
    mov dx, 0x998
    out dx, al
    xor al, al
    jmp finish
fail:
    mov al, 0xFF
finish:
    mov dx, 0x999
    out dx, al
    mov ax, 0x4C00
    int 0x21
