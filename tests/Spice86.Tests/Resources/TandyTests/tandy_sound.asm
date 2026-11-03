; Original native sound probe: C0h/C1h PSG writes and C4h-C7h DMA playback.
; OutRun was observed using both native sound blocks after firmware detection.
bits 16
org 0x100

    mov ah, 0x81
    int 0x1A
    jc fail
    cmp ax, 0xC4
    jne fail
    mov dx, 0x998
    out dx, al
    mov ax, 0x40
    mov es, ax
    cmp byte [es:0xD4], 0xFF
    jne fail

    ; A latch/data word writes a tone period through both PSG ports.
    mov dx, 0xC0
    mov ax, 0x1080
    out dx, ax
    mov al, 0x90
    out dx, al

    cli
    xor ax, ax
    mov es, ax
    mov word [es:0x3C], irq7
    mov ax, cs
    mov word [es:0x3E], ax
    mov al, 0x7F
    out 0x21, al
    mov al, 5
    out 0x0A, al
    xor al, al
    out 0x0C, al
    mov ax, cs
    shl ax, 4
    add ax, samples
    out 2, al
    mov al, ah
    out 2, al
    xor al, al
    out 0x83, al
    mov ax, 7
    out 3, al
    mov al, ah
    out 3, al
    mov al, 0x49
    out 0x0B, al
    mov al, 1
    out 0x0A, al
    mov al, 0xC0
    out 0xC6, al
    mov al, 0xE1
    out 0xC7, al
    in al, 0xC6
    cmp al, 0xC0
    jne fail
    in al, 0xC7
    cmp al, 0xE1
    jne fail
    mov al, 0x0F
    out 0xC4, al
    in al, 0xC4
    cmp al, 7
    jne fail
    sti
    mov cx, 0xFFFF
.wait:
    cmp byte [cs:completed], 1
    je .done
    loop .wait
    jmp fail
.done:
    cli
    xor al, al
    out 0x0C, al
    in al, 3
    cmp al, 0xFF
    jne fail
    in al, 3
    cmp al, 0xFF
    jne fail
    mov dx, 0x998
    mov al, [cs:completed]
    out dx, al
    xor al, al
    out 0xC4, al
    mov al, 0x9F
    out 0xC0, al
    xor al, al
    jmp finish
fail:
    mov al, 0xFF
finish:
    mov dx, 0x999
    out dx, al
    mov ax, 0x4C00
    int 0x21

irq7:
    push ax
    inc byte [cs:completed]
    mov al, 0x20
    out 0x20, al
    pop ax
    iret

completed: db 0
samples: db 0, 32, 64, 96, 128, 160, 192, 255
