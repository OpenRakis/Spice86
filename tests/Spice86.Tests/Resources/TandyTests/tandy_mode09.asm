; Original probe of OutRun's observed mode 09h and native gate-array path.
bits 16
org 0x100

    mov ax, 9
    int 0x10
    mov ah, 0x0F
    int 0x10
    cmp ax, 0x2809
    jne fail
    mov ax, 0xB800
    mov es, ax
    mov byte [es:0], 0x12
    mov byte [es:0x2000], 0x34
    mov byte [es:0x4000], 0x56
    mov byte [es:0x6000], 0x78
    mov byte [es:160], 0xAB

    mov ax, 0x0C0E
    xor bx, bx
    mov cx, 2
    mov dx, 4
    int 0x10
    mov ah, 0x0D
    int 0x10
    cmp al, 0x0E
    jne fail

    mov dx, 0x3DA
    mov al, 0x11
    out dx, al
    mov dx, 0x3DE
    mov al, 4
    out dx, al
    mov ax, 0x1000
    mov bx, 0x0602
    int 0x10

    ; Keep bank 6 displayed while selecting bank 0 for CPU accesses.
    mov dx, 0x3DF
    mov al, 0xC6
    out dx, al
    mov byte [es:0], 0xEF
    mov ax, 0x9800
    mov es, ax
    cmp byte [es:0], 0x12
    jne fail
    cmp byte [es:0x6000], 0x78
    jne fail
    mov ax, 0x8000
    mov es, ax
    cmp byte [es:0], 0xEF
    jne fail

    ; Two retraces also let the coalesced drawing setup latch mode 09h.
    call wait_retrace
    call wait_retrace
    mov dx, 0x998
    mov al, 9
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

wait_retrace:
    mov dx, 0x3DA
    mov cx, 0xFFFF
.clear:
    in al, dx
    test al, 8
    jz .begin
    loop .clear
    jmp fail
.begin:
    mov cx, 0xFFFF
.set:
    in al, dx
    test al, 8
    jnz .done
    loop .set
    jmp fail
.done:
    ret
