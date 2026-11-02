use16

; Exercises redirected returns: calls whose callee rewrites the stacked return address and lands
; one byte past the pushed return address while the stack is back at its call-time level.
;
; The int 0x80 handler mimics the Borland 8087 emulator: it bumps the stacked return IP by one so
; the interrupt "returns" past the operand byte that follows the int, and it reaches its own iret
; through a push/ret jump inside the handler.
;
; Counters and markers live in low RAM (segment 0). DS = 0 throughout.

start:
    mov ax, 0
    mov ds, ax
    mov ss, ax
    mov sp, 0x100
    mov word [0x200], handler       ; IVT entry for int 0x80 (0x80 * 4)
    mov word [0x202], cs
    mov word [0x00], 0              ; iteration counter

int_loop:
    int 0x80
    db 0F4h                         ; operand byte (hlt): wrongly executing it stops the run
    inc word [0x00]
    cmp word [0x00], 300
    jb int_loop
    mov byte [0x10], 0xAA           ; int loop done

    call int_wrapper
    mov byte [0x11], 0xBB           ; near-called int done

    push 0x1234                     ; one argument word for the ret 2 callee
    call ret2_wrapper
    db 0F4h                         ; operand byte the callee's ret 2 must skip
    mov byte [0x12], 0xCC           ; ret 2 done

done:
    hlt

int_wrapper:
    int 0x80
    db 0F4h
    ret

ret2_wrapper:
    mov bp, sp
    inc word [bp]                   ; rewrite the stacked return IP: land past the operand byte
    ret 2                           ; discard the argument word

handler:
    mov bp, sp                      ; int frame at SS:SP: return IP, CS, flags
    inc word [bp]                   ; skip the operand byte after the int
    push handler_epilogue           ; reach the epilogue through a push/ret jump
    ret
handler_epilogue:
    iret

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
