use16

; Reproduces the multiple-misaligned-continuation code generation failure.
;
; A single shared subroutine ("dispatcher") is reached through one CALL instruction but,
; like a DOS overlay/trampoline thunk, it discards its real return address and returns to a
; different continuation each time it runs. None of those continuations is the instruction that
; statically follows the CALL, so every observed return is classified CallToMisalignedReturn.
; The single CALL node therefore accumulates several CallToMisalignedReturn successors, which is
; the shape the C# generator rejected.
;
; Counter and markers live in low RAM (segment 0). DS = 0 throughout.

start:
    mov ax, 0
    mov ds, ax
    mov ss, ax
    mov sp, 0x100
    mov word [0x00], 0          ; iteration counter

call_site:
    call dispatcher             ; the single shared call node
aligned_return:
    hlt                         ; statically-following instruction, never returned to

cont0:
    mov byte [0x10], 0xAA
    jmp call_site

cont1:
    mov byte [0x11], 0xBB
    jmp call_site

cont2:
    mov byte [0x12], 0xCC
    jmp done

done:
    hlt

dispatcher:
    pop ax                      ; discard the real (aligned) return address
    mov bx, [0x00]              ; current iteration
    inc word [0x00]
    cmp bx, 0
    je .to_cont0
    cmp bx, 1
    je .to_cont1
    mov ax, cont2
    push ax
    ret
.to_cont0:
    mov ax, cont0
    push ax
    ret
.to_cont1:
    mov ax, cont1
    push ax
    ret

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
