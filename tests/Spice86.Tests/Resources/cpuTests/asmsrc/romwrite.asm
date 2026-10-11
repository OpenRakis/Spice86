; Original synthetic regression. Assemble with nasm -f bin romwrite.asm -o ..\romwrite.bin
bits 16
org 0

start:
    cli
    cld
    mov ax, 0x1000
    mov ds, ax
    mov word [0x0100], 0xC1C0
    mov word [0x0102], 0xC3C2
    mov ax, 0xB800
    mov es, ax
    mov si, 0x0100
    mov di, 0x9190
    mov cx, 4
    ; B800:9190 translates to C1190, inside the mapped VGA ROM.
    rep movsb
    mov word [es:0x9190], 0xA55A
    mov ax, 0x600D
    hlt

times 0xFFF0 - ($ - $$) db 0
    jmp start
times 0xFFFF - ($ - $$) db 0
    db 0xFF
