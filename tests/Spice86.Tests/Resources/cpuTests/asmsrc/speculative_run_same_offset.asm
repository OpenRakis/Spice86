; Speculative runs at equal offsets in different segments must have distinct C# field names.
; Selector 0000:0500 chooses observed results DD/BB or speculative results EE/CC.
use16
start:
    mov byte [0x0400], 0x01
    mov sp, 0x8000
    call 0F000h:0100h
    call 0F010h:0100h
    hlt
rb 0100h-$
first:
    cmp byte [0x0500], 0
    jne first_speculative
    mov byte [0x0401], 0xDD
    retf
first_speculative:
    mov al, 0xE0
    mov bl, 0x0E
    add al, bl
    mov [0x0401], al
    retf
rb 0200h-$
second:
    cmp byte [0x0500], 0
    jne second_speculative
    mov byte [0x0402], 0xBB
    retf
second_speculative:
    mov al, 0xC0
    mov bl, 0x0C
    add al, bl
    mov [0x0402], al
    retf
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
