use16

; Reproduces the duplicate-method / duplicate-registration code generation failure
; for self-modifying code at a CALL target (the Dune2 "unknown_3409_0025" shape).
;
; One subroutine entry address ("stub") is far-called from several distinct call sites.
; Before each call the stub's resident instruction is rewritten in place to a different
; "jmp far" variant pointing at a different handler. Each call therefore reaches a distinct
; instruction variant living at the same stub address. Because the stub address is a call
; target, the function partitioner promotes each variant into its own partition, so several
; partitions end up sharing the same entry address - exactly the shape that produced duplicate
; C# methods (CS0111) and duplicate DefineFunction registrations before the fix.
;
; Markers live in low RAM (segment 0). Expected RAM: [0x10]=0xAA [0x11]=0xBB [0x12]=0xCC.

start:
    mov ax, 0
    mov ds, ax
    mov ss, ax
    mov sp, 0x100
    mov ax, 0xF000
    mov es, ax                  ; ES = BIOS code segment for in-place patching

    ; Install variant A (jmp far handler_a) into the stub, then call it.
    call install_a
    call far 0xF000:stub
    ; Install variant B (jmp far handler_b), then call.
    call install_b
    call far 0xF000:stub
    ; Install variant C (jmp far handler_c), then call.
    call install_c
    call far 0xF000:stub
    hlt

; -----------------------------------------------------------------------------
; The mutable stub. It is sized to hold a far jump (opcode EA + 4 byte seg:off).
; Its bytes are overwritten by install_a/b/c before each call.
; -----------------------------------------------------------------------------
stub:
    jmp far 0xF000:handler_a    ; placeholder bytes, patched before each call
    db 0                        ; padding so later variants fit

; Handlers: write a marker then far-return to the stub's caller.
handler_a:
    mov byte [0x10], 0xAA
    retf
handler_b:
    mov byte [0x11], 0xBB
    retf
handler_c:
    mov byte [0x12], 0xCC
    retf

; install_X rewrites the stub's far-jump offset to point at handler_X.
; The opcode (EA) and segment (F000) are identical across variants; only the
; 16-bit offset immediately after the opcode changes, producing distinct
; instruction variants at the stub address.
install_a:
    mov word [es:stub + 1], handler_a
    retn
install_b:
    mov word [es:stub + 1], handler_b
    retn
install_c:
    mov word [es:stub + 1], handler_c
    retn

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
