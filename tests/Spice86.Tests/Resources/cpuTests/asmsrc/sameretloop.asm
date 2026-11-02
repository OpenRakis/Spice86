; sameretloop: a function whose near return keeps popping the address it is
; already running at, so every return leaves CS:IP exactly where the function
; activation started. The loop is entered by pushing the body's entry and
; returning into it, making that entry a return-target function; inside, the
; body returns to its own entry until the counter reaches 4 and the hlt ends
; the run. A dispatcher that reads an unchanged CS:IP as "no control transfer"
; would hand these returns back to the interpreter; the control-transfer
; counter keeps them in generated execution.
;
; compile it with fasm
use16

start:
    mov ax, 0
    mov ss, ax
    mov sp, 0100h
    mov byte [cs:counter], 0

    ; Enter the loop body through a return whose target is the body's own
    ; entry, so the body is a function that will return to its own start.
    mov ax, loop_body
    push ax
    ret

loop_body:
    inc byte [cs:counter]
    cmp byte [cs:counter], 4
    jae loop_done
    push loop_body
    ret                 ; pops its own entry address: CS:IP does not move

loop_done:
    hlt

counter db 0

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
