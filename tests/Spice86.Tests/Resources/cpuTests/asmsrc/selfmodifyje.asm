; selfmodifyje: verify that ReplaceInstruction does not permanently clear
; CanHaveMoreSuccessors on the predecessors of a replaced instruction.
;
; Bug: SwitchPredecessorsToNew in NodeLinker adds the new successor before
; removing the old one. Adding the new successor can bring Successors.Count up
; to MaxSuccessorsCount, which sets CanHaveMoreSuccessors = false. Removing the
; old successor afterwards drops the count back below max, but CanHaveMoreSuccessors
; is never reset to true. As a result, when je later falls through to hlt, the
; link je -> hlt is silently skipped.
;
; Flow:
;   Iteration 1: CX=0, je taken -> selfmodify (MOV AX,1234), patches imm -> 5678,
;                CX=1, AX!=5678 so inner je not taken, CX=0, jmp jump
;   Iteration 2: CX=0, je taken -> selfmodify is stale (memory says MOV AX,5678),
;                SignatureReducer merges the instruction; ReplaceInstruction triggers
;                SwitchPredecessorsToNew on je, which erroneously sets
;                je.CanHaveMoreSuccessors = false. AX=5678, inner je jump taken.
;   Iteration 3: CX=1, je NOT taken -> falls through to hlt.
;                With the bug: je.CanHaveMoreSuccessors is still false so the
;                je -> hlt link is never added. hlt has no predecessor from je.
;                With the fix: CanHaveMoreSuccessors is reset to true after the
;                remove, and the je -> hlt link is correctly added.
;
; Expected memory: empty (no values written; correctness verified via CFG assertions)
;
; compile it with fasm
use16

start:
    mov cx, 0
jump:
    cmp cx, 0
    je selfmodify
    hlt
selfmodify:
    mov ax, 1234
    mov word [cs:selfmodify+1], 5678
    mov cx, 1
    cmp ax, 5678
    je jump
    mov cx, 0
    jmp jump

; BIOS entry point
rb 65520-$
jmp start
rb 65535-$
db 0ffh
