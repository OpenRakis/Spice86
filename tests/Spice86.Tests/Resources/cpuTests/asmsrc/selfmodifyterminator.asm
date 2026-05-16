;00: 42 00 ff ff
; compile it with fasm
; Test: self-modifying code that replaces the TERMINATOR of a block.
;
; A block ending with 'jne loop_top' is executed on the first iteration.
; On the second iteration, code patches that jne into 'jmp short done'.
; The emulator detects the modification, creates a SelectorNode at the
; terminator's address, and dispatches to the new jmp variant.
;
; This exercises the scenario where the terminator of a block is replaced
; by self-modifying code.
;
; Flow:
;   Iteration 1: counter=0, call handler (no patch), return, or ax,ax, jne taken
;   Iteration 2: counter=1, call handler (patches jne→jmp), return, or ax,ax,
;                 selector dispatches to jmp short done
;
; Expected stack (memory at 0000:0000): 42 00 FF FF
;   [0000] = 0x0042 (AX pushed at 'done', second push)
;   [0002] = 0xFFFF (AX pushed on first iteration, first push)
use16

start:
    ; Setup stack so two pushes fill addresses 0-3
    mov ax, 0
    mov ss, ax
    mov sp, 4

    ; First iteration marker
    mov ax, 0FFFFh
    mov byte [counter], 0

loop_top:
    ; Push current AX value
    push ax

    ; Call the handler — it may patch the terminator
    call patch_handler

    ; Set AX to second-iteration marker
    mov ax, 0042h

    ; This block: [mov ax, 0042h] [or ax, ax] [jne loop_top]
    ; The jne is the terminator. On iteration 2 it will have been patched.
    or ax, ax                       ; ZF=0 since AX=0x0042 != 0
block_terminator:
    jne loop_top                    ; iter 1: taken. iter 2: patched to jmp short done.

    ; Fallthrough — should never be reached
    mov ax, 0DEADh
    push ax
    hlt

done:
    push ax
    hlt

patch_handler:
    cmp byte [counter], 0
    je .no_patch

    ; Patch the jne at block_terminator into jmp short done
    mov ax, 0F000h
    mov es, ax
    mov byte [es:block_terminator], 0EBh
    mov byte [es:block_terminator + 1], done - (block_terminator + 2)
    ret

.no_patch:
    inc byte [counter]
    ret

counter db 0

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
