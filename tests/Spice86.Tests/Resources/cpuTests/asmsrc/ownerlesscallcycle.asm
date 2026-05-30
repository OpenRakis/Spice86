; ownerlesscallcycle: reproduces a CFG function-partitioning failure on an
; ownership-preserving call/loop cycle that no partition root can reach.
;
; The loop body forms a two-block ownership-preserving cycle:
;   loop_call (A): call helper        ; aligned call, continuation is loop_cont
;   loop_cont (B): ...; jb loop_call  ; loops back to A
; helper's RET lands on loop_cont, so A -> B is an aligned call-continuation
; edge and B is a call-continuation target. A is reached only by B's loop-back
; jump, so nothing calls A and A is not a call target.
;
; The loop is bootstrapped by pushing loop_cont and executing RET, i.e. a return
; whose target is loop_cont. The partitioner classifies that as a return-target
; edge, but suppresses promoting it to a root because loop_cont is also an
; aligned call-continuation target. With that suppression and no call target,
; execution-context entry, or CPU fault target inside the cycle, neither A nor B
; gets a partition root: the whole cycle is ownerless. Before the
; ownerless-region rescue, partitioning threw on this shape.
;
; compile it with fasm
use16

start:
    mov ax, 0
    mov ss, ax
    mov sp, 0100h
    mov byte [cs:counter], 0

    ; Bootstrap the loop by "returning" into its continuation block. This return
    ; target is suppressed for root promotion because it is also the aligned
    ; call-continuation of loop_call below.
    mov ax, loop_cont
    push ax
    ret

loop_call:
    call helper
loop_cont:
    inc byte [cs:counter]
    mov al, [cs:counter]
    cmp al, 3
    jb loop_call
    hlt

helper:
    ret

counter db 0

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
