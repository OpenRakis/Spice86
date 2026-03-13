;00: 01 00 ff ff
; compile it with fasm
use16

; Minimal self-modifying call test BIOS

start:
    ; setup a tiny stack so CALL/RET work
    mov ax,0
    mov ss,ax
    mov sp,0100h

    mov byte [counter], 0

first_call:
    ; Call the handler; it may patch the return point
    call self_modifying_handler
function_return_point:
    ; Will be modified and returned to twice
    nop

    ; Bump counter and call again from the same place
    inc byte [counter]
    jmp first_call

self_modifying_handler:
    cmp byte [counter], 0
    je .ret_only
    ; Ensure ES points to BIOS code segment (F000h) before patching
    mov ax,0F000h
    mov es,ax
    mov byte [es:function_return_point], 0F4h    ; HLT opcode on second and later
    ; different ret, first time it is encountered
    ret
.ret_only:
    ret

counter db 0

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
