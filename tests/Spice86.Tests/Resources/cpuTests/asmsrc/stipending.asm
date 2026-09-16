; compile it with fasm
use16
start:
    mov ax,0
    mov ss,ax
    mov sp,0x100
    mov ds,ax
    mov dx,0
    ; int 8 handler
    mov word [8*4], inthandler
    mov word [8*4+2], cs
    ; PIT counter 0, mode 3, ~1ms
    mov al, 00110110b
    out 43h, al
    mov al, 51h
    out 40h, al
    mov al, 22h
    out 40h, al
    ; unmask all IRQs
    mov al, 0
    out 21h, al

    ; critical section 1: cli ... sti
    cli
    mov dx, 0
    mov ecx, 0FFFFFFh
l1:
    loop l1
    sti
    nop
    mov [0], dx          ; expect 1: the IRQ raised during l1 is delivered right after sti

    ; critical section 2: pushf; cli ... popf
    sti
    pushf
    cli
    mov dx, 0
    mov ecx, 0FFFFFFh
l2:
    loop l2
    popf
    nop
    mov [1], dx          ; expect 1: same, delivered right after popf (IF restored to 1)
    hlt

inthandler:
    push ax
    mov dx, 1
    mov al, 20h
    out 20h, al
    pop ax
    iret

rb 65520-$
    jmp start
rb 65535-$
    db 0ffh
