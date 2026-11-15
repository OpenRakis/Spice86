; compile it with fasm
; Speculative call-table fixture: an observed near indirect call through a word
; memory operand preceded by the canonical bounds-check sequence must have its
; table callees explored speculatively, so the unobserved call runs in generated
; code instead of failing "Untested near call target". For each entry the D2
; call-continuation rule applies once the callee is known to return.
;
; The discovery run executes the SAME indirect call twice with two different
; entries (0 then 1), the second after the first call returned: by then the call
; node carries an observed CallToReturn edge to its continuation. The table's
; last entry starts as 0x0000 (first resolution refuses on the refused-entry
; check) and is patched between the two calls, so the second resolution must
; still enqueue it: the CallToReturn successor must not refuse the table.
;
; Selector byte at [0x0500]: 0x00 = discovery run (entries 0 and 1), 0x01 =
; unobserved run (entry 2 -> f2).
;
; Expected memory:
;   [0x0400] = 0x01
;   [0x0401] = 0x11 (discovery: f0) or 0x22 (unobserved: f2)
;   [0x0402] = 0x02 (discovery: continuation ran twice) or 0x01 (unobserved: once)
;   [0x0403] = 0x77 (discovery: f1) or 0x00 (unobserved)

use16

start:
    mov byte [0x0400], 0x01
    mov byte [0x0401], 0x00
    mov byte [0x0402], 0x00
    mov byte [0x0403], 0x00

    mov al, byte [0x0500]
    cmp al, 0
    jne unobserved_path

    ; discovery run: two calls, entries 0 then 1; the second executes after the
    ; first returned, i.e. once the call node has an observed CallToReturn edge.
    mov cx, 0
    call do_call
    mov word [cs:call_table+4], f2
    mov cx, 1
    call do_call
    jmp done

unobserved_path:
    mov word [cs:call_table+4], f2
    mov cx, 2
    call do_call

done:
    hlt

; cmp ax,N; ja def; mov bx,ax; shl bx,1; call word [cs:bx+T]
do_call:
    mov ax, cx
    cmp ax, 2
    ja call_default
    mov bx, ax
    shl bx, 1
    call word [cs:bx+call_table]
    ; continuation after the indirect call: the D2 rule explores it once a table
    ; callee is known to return; on both runs it is executed.
    inc byte [0x0402]
    ret
call_default:
    hlt

f0:
    mov byte [0x0401], 0x11
    ret
f1:
    mov byte [0x0403], 0x77
    ret
f2:
    mov byte [0x0401], 0x22
    ret

align 2
call_table:    dw f0, f1, 0

rb 65520-$
jmp start
rb 65535-$
db 0ffh
