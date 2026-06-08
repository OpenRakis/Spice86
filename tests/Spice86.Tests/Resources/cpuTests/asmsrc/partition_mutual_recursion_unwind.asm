use16

; Reproduces the JumpDispatcher mutual-recursion unwind bug in generated code.
;
; function_a and function_b are BOTH call targets, so the partitioner makes each its
; own partition. They jump to each other, putting both partitions in one strongly
; connected component, so each cross-jump is lowered as CyclicCrossPartitionFlow:
; "if (JumpDispatcher.Jump(...)) goto entrydispatcher; return RequiredJumpAsmReturn;".
; (This is the same partition shape as partition_cross_function_loop.)
;
; The bug only shows up when the cycle bounces back into a partition that is STILL on
; the jump stack. The first call uses cx=3, producing a -> b -> a: that b -> a jump
; happens while function_a's enclosing JumpDispatcher.Jump call has not yet assigned
; JumpAsmReturn, so function_b's "return RequiredJumpAsmReturn" reads it while still null.
; partition_cross_function_loop (cx=2) returns before bouncing back, so it never hits
; this unwind and did not catch the regression.
;
; Markers live in low RAM (segment 0). Expected RAM: [0x10]=0xAA (function_a_done),
; [0x11]=0xBB (function_b_done).

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    mov cx, 3
    call function_a
    mov cx, 1
    call function_b
    hlt

function_a:
    dec cx
    jz function_a_done
    jmp function_b

function_a_done:
    mov ax, 0
    mov ds, ax
    mov byte [0x10], 0xAA
    ret

function_b:
    dec cx
    jz function_b_done
    jmp function_a

function_b_done:
    mov ax, 0
    mov ds, ax
    mov byte [0x11], 0xBB
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
