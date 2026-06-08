use16

; Test: push CS / call near / retf pattern called via far call from a different segment.
;
; The bug: when generated code does a FarCall, it sets CS to the return segment (caller's CS),
; not to the callee's segment. So "push CS" inside the callee pushes the wrong value.
;
; Layout (loaded at physical F0000):
;   F000:FFF0 (reset vector) -> jumps to F000:0000 (start)
;   F000:0000 (start) - sets up stack, does far call to E000:1000
;   E000:1000 (= physical F0000, offset 1000h) - uses push CS / call near / retf pattern
;
; Physical address of E000:1000 = E000*16 + 1000 = E0000 + 1000 = E1000
; Offset within binary = E1000 - F0000 = ... wait, that's negative.
;
; Let's use F100:0000 instead. Physical = F100*16 + 0 = F1000. Offset in binary = F1000 - F0000 = 1000h.
; So code at file offset 1000h is addressable as F100:0000.

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h

    ; Far call to F100:0000 (physical F1000, file offset 1000h)
    call 0F100h:0000h

    ; After return, AX should be 0x42
    mov word [0x0000], ax
    hlt

; Pad to file offset 1000h (= physical F1000 = F100:0000)
rb 1000h-$

; This is the callee at F100:0000
callee_entry:
    ; push CS / call near / retf pattern
    ; CS should be F100 here (set by the far call)
    push cs             ; push CS (should be F100) onto stack
    call near helper    ; push IP (return_point) onto stack
return_point:
    ; After helper does "retf", we resume here with CS=F100
    mov ax, 0x42
    retf             ; far return back to F000:xxxx (the caller)

helper:
    ; Does some work then far-returns using pushed CS + near-call return IP
    xor bx, bx
    retf                ; pops IP=return_point, CS=F100 (the pushed CS)

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
