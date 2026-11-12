; compile it with fasm
; G1.5: a stack in device memory is not a value store. Planar memory at A000h loads the 4
; latches on every read and a write-mode-1 write copies the latches, not the written byte.
; With SS = A000h, push ax is a mode-1 write at ss:sp-2 that stores the latches, and pop bx
; reads the planes back - BX gets 5555h, never the pushed 1234h. Printing the pair as an
; assignment would both deliver the pushed value to BX and drop the write's plane effect.
; DS = 2000h. Results go to offsets 40h-44h (physical 20040h-20044h).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor si, si

; VGA setup: sequencer map mask all planes, graphics controller maps memory at A000h,
; bit mask FFh.
    mov dx, 03C4h
    mov ax, 0F02h
    out dx, ax
    mov ax, 0604h
    out dx, ax
    mov dx, 03CEh
    mov ax, 0006h
    out dx, ax
    mov ax, 0FF08h
    out dx, ax
    mov ax, 0A000h
    mov es, ax
; plane bytes at es:[0] = 55h/66h/77h/99h
    mov dx, 03C4h
    mov ax, 0102h
    out dx, ax
    mov byte [es:0], 55h
    mov ax, 0202h
    out dx, ax
    mov byte [es:0], 66h
    mov ax, 0402h
    out dx, ax
    mov byte [es:0], 77h
    mov ax, 0802h
    out dx, ax
    mov byte [es:0], 99h
    mov ax, 0F02h
    out dx, ax
; one read loads the latches with es:[0]'s planes
    mov al, [es:0]
; write mode 1: a write copies the latches, not the data byte
    mov dx, 03CEh
    mov ax, 0105h
    out dx, ax
; the stack now lands in the VGA planar window at A000:0100h
    mov ax, 0A000h
    mov ss, ax
    mov sp, 0100h
    mov ax, 1234h
    mov cx, 5678h
    push ax
    pop bx
    push cx
    mov [si+40h], bl
; read es:[00FE]'s four planes back through the read map select
    mov ax, 0005h
    out dx, ax
    mov ax, 0004h
    out dx, ax
    mov al, [es:0FEh]
    mov [si+41h], al
    mov ax, 0104h
    out dx, ax
    mov al, [es:0FEh]
    mov [si+42h], al
    mov ax, 0204h
    out dx, ax
    mov al, [es:0FEh]
    mov [si+43h], al
    mov ax, 0304h
    out dx, ax
    mov al, [es:0FEh]
    mov [si+44h], al
; restore the boot map (B800h window) so the end-of-run RAM dump stays inside it
    mov ax, 0E06h
    out dx, ax
; put the stack back in RAM
    mov ax, 2000h
    mov ss, ax
    mov sp, 1000h

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
