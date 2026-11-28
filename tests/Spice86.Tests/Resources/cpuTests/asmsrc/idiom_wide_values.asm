; Build: fasm idiom_wide_values.asm ../idiom_wide_values.bin
; two byte stores merge into a word read; two word stores merge into a dword read.
use16

start:
    mov byte [0x0600], 0x01
    mov byte [0x0610], 0x34
    mov byte [0x0611], 0x12
    mov ax, word [0x0610]
    mov byte [0x0601], ah
    mov word [0x0612], 0x5678
    mov word [0x0614], 0x1234
    mov eax, dword [0x0612]
    shr eax, 16
    mov byte [0x0602], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
