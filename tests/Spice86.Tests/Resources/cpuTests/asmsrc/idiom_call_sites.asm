; Build: fasm idiom_call_sites.asm ../idiom_call_sites.bin
; a near call with a stack argument, a call whose output is discarded, and an o32 call.
; fasm has no o32 form, so the 66h-prefixed encodings are written out (see cc_call_32bit).
use16

start:
    mov byte [0x0600], 0x01
    push 0x0042
    call with_arg
    add sp, 2
    mov byte [0x0601], al
    call discarded
    db 66h, 0E8h            ; o32 call wide32 (rel32)
    dd wide32 - $ - 4
    mov byte [0x0602], al
    hlt
with_arg:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    pop bp
    ret
discarded:
    mov ax, 0x9999
    ret
wide32:
    mov eax, 0x55
    db 66h, 0C3h            ; o32 ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
