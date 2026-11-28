; Build: fasm idiom_relaxation_saved.asm ../idiom_relaxation_saved.bin
; saved-slots: a method saves registers across a device boundary,
; changes them, and restores the saved slots whose values the caller observes.
use16

start:
    mov ax, 2000h
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov ds, ax
    mov byte [es:0x0600], 0x01
    mov si, 0x11
    mov di, 0x22
    call worker
    mov word [es:0x0601], si
    mov word [es:0x0603], di
    hlt
worker:
    push si
    push di
    out 0x80, al
    mov si, 0x99
    mov di, 0x88
    pop di
    pop si
    ret
    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
