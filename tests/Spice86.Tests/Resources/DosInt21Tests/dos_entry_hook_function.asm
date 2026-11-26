; Build: fasm dos_entry_hook_function.asm dos_entry_hook_function.com
; A DOS service must save the caller SS:SP in the current PSP (offset 2Eh) under its own
; function number. The subroutine leaves AH = 62h set through an untyped INT 2Fh, then calls
; the typed AH=30h (Get DOS version) at a deeper stack pointer - a site whose AH write the
; generator drops because the function returns normally. Interpreted, the 30h call records
; SS:(SP-6); if the typed call decided the save from a stale AH, it records nothing and the
; field keeps the earlier value. Results land at 3000:0000.
use16
org 100h

result_segment equ 3000h

start:
    call exercise
    mov ax, 4C00h
    int 21h

exercise:
    ; Record once through an untyped INT 21h service (AH=0Bh): saves SS:(SP-6).
    mov ah, 0Bh
    int 21h

    ; Leave AH = 62h behind through an untyped interrupt so the next typed call
    ; must not reuse it for the entry-save decision.
    mov ah, 62h
    int 2Fh

    ; Typed AH=30h at a deeper stack pointer: saves SS:(SP-8) over the previous value.
    push bp
    mov ah, 30h
    int 21h
    pop bp

    ; Read the current PSP's saved SS:SP (typed AH=62h returns the segment in BX).
    mov ah, 62h
    int 21h
    mov es, bx
    mov ax, result_segment
    mov ds, ax
    mov ax, [es:2Eh]
    mov [0000h], ax
    mov ax, [es:30h]
    mov [0002h], ax
    mov [0004h], bx
    ret
