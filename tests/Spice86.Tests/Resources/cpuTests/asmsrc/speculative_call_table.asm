; compile it with fasm
; Speculative call-table fixture: an observed near indirect call through a word
; memory operand preceded by the canonical bounds-check sequence must have its
; table callees explored speculatively, so the unobserved call runs in generated
; code instead of failing "Untested near call target". For each entry the D2
; call-continuation rule applies once the callee is known to return.
;
; Selector byte at [0x0500]: 0x00 = discovery run (index 0 -> f0), 0x01 =
; unobserved run (index 2 -> f2).
;
; Expected memory:
;   [0x0400] = 0x01
;   [0x0401] = 0xDD (discovery) or 0x22 (unobserved: f2 marker)
;   [0x0402] = 0xAA (continuation after the indirect call, both runs)

use16

start:
    mov byte [0x0400], 0x01

    ; cmp ax,N; ja def; mov bx,ax; shl bx,1; call word [cs:bx+T]
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1               ; ax = index (0 or 2)
    cmp ax, 2
    ja call_default
    mov bx, ax
    shl bx, 1
    call word [cs:bx+call_table]
    ; continuation after the indirect call: the D2 rule explores it once a table
    ; callee is known to return; on the discovery run it is simply executed.
    mov byte [0x0402], 0xAA
    jmp done
call_default:
    hlt

f0:
    mov byte [0x0401], 0xDD
    ret
f1:
    hlt
f2:
    mov byte [0x0401], 0x22
    ret

done:
    hlt

align 2
call_table:    dw f0, f1, f2

rb 65520-$
jmp start
rb 65535-$
db 0ffh
