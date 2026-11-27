; First run leaves target speculative. On CFG reload, take this arm after changing
; it: selector 1 changes the immediate (same final signature), selector 2 changes
; the opcode (different final signature).
use16
start:
    mov ax, 0
    mov ds, ax
    cmp byte [0x0500], 0
    jne choose_patch
    jmp done
choose_patch:
    cmp byte [0x0500], 2
    je patch_opcode
patch_target:
    mov byte [cs:target + 1], 0x12
    jmp target
patch_opcode:
    mov byte [cs:target], 0B9h
    jmp target
done:
    hlt
rb 0100h-$
target:
    mov ax, 0x0011
    mov word [0x0400], ax
    hlt
rb 0FFF0h-$
    jmp start
rb 0FFFFh-$
    db 0FFh
