; compile it with fasm
; Expected memory at 0000:0000:
;   00 00 00 00 22 22 11 11
;
; If ExecuteNext hot-runs the completed body block from its entry while execution
; is already at the interior jmp, the stack gets duplicate values at 0000:0000..0003.
use16

start:
    mov ax, 0
    mov ss, ax
    mov sp, 8
    jmp body

body:
    mov ax, 1111h
    push ax

    mov ax, 2222h
    push ax

block_terminator:
    jmp done

bad:
    mov ax, 0DEADh
    push ax
    hlt

done:
    hlt

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh