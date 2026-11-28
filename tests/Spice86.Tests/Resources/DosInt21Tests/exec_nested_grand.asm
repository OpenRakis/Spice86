; Build: fasm exec_nested_grand.asm exec_nested_grand.com
; The grandchild of the nested-EXEC fixture: stores its marker to 5000:0030 and terminates.
; Its terminate must unwind to the child's EXEC call, not to the parent's.
use16
org 100h

result_seg equ 5000h

start:
    mov ax, result_seg
    mov es, ax
    mov word [es:0030h], 0ABCDh
    int 20h
