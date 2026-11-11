; compile it with fasm
; A 32-bit stack operation writes only the low 16 bits of ESP: the high half a
; mov esp set must survive push eax / pop eax and a 32-bit enter/leave pair.
; The oracle reads the dword the program stores at 2000:0200.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov esp, 12341000h
    push eax
    pop eax
    db 66h
    enter 4, 0
    db 66h
    leave
    mov [0200h], esp
    hlt

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
