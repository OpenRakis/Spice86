; compile it with fasm
use16
start:
    mov ax, 0
    mov ss, ax
    mov sp, 0x100
    mov ds, ax
    ; mask every PIC line: no interrupt can wake the CPU
    mov al, 0xFF
    out 21h, al
    ; the program reached the halt
    mov [2], 0xAA
    cli
    sti
    hlt                  ; waits forever: only a cycle breakpoint can end the run

rb 65520-$
    jmp start
rb 65535-$
    db 0ffh
