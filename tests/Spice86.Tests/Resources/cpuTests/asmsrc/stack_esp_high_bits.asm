; compile it with fasm
; A 32-bit stack operation writes only the low 16 bits of ESP: the high half a
; mov esp set must survive push eax / pop eax and a 32-bit enter/leave pair.
; A 16-bit or 32-bit enter 0,0 / leave pair must also leave the high half intact.
; The oracle reads the dwords the program stores at 2000:0200, 2000:0204 and 2000:0208.
; A 16-bit enter 2,2 / leave pushes BP and copies one caller frame word below it,
; a 32-bit enter 4,2 / leave does the same with dwords; the oracle reads the words
; at 2000:8FFA-8FFF and the dwords at 2000:9FF4-9FFF.
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
    mov esp, 12345000h
    enter 0, 0
    leave
    mov [0204h], esp
    mov esp, 12347000h
    db 66h
    enter 0, 0
    db 66h
    leave
    mov [0208h], esp
    mov word [04FEh], 0BEEFh
    mov esp, 12349000h
    mov bp, 0500h
    enter 2, 2
    leave
    mov dword [05FCh], 0CAFEF00Dh
    mov esp, 1234A000h
    mov ebp, 12340600h
    db 66h
    enter 4, 2
    db 66h
    leave
    hlt

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
