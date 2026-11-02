use16

; Exercises stale call-stack frames: code that leaves a function by resetting SP and jumping back
; to the main loop instead of returning. The abandoned frames stay on the tracked call stack, and a
; later `push label; ret` inside a live function lands at the stale frame's stack level. That return
; must not be attributed to the stale frame: a live callee always pushes below its caller, so a
; candidate shadowed by a newer frame at a higher address is stale.
;
; Counters and markers live in low RAM (segment 0). DS = 0 throughout.

start:
    mov ax, 0
    mov ds, ax
    mov ss, ax
    mov sp, 0x100
    mov word [0x00], 0              ; iteration counter

loop_top:
    mov sp, 0x100                   ; reset SP each iteration
    inc word [0x00]
    cmp word [0x00], 1
    je first_iteration
    call func_b
    mov byte [0x11], 0xBB           ; returned from B
    hlt

first_iteration:
    call func_a                     ; A never returns: it jumps back to loop_top
    jmp loop_top                    ; unreachable

func_a:
    call func_a2                    ; A2's frame sits two words below the loop SP
    ret                             ; unreachable

func_a2:
    mov sp, 0x100                   ; abandon A and A2: stale frames stay on the tracked stack
    jmp loop_top

func_b:
    push b_inner                    ; jump inside B, at the stale A2 stack level
    ret
b_inner:
    mov byte [0x10], 0xAA           ; the push/ret jump target ran
    ret                             ; real ret: consumes B's own frame

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
