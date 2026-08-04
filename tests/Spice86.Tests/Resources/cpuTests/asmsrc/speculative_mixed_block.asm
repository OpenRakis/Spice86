; Speculative CFG test fixture T12: mixed-block guard placement (mid-block).
;
; A block where discovery executes the first 2 instructions, then takes a branch
; out. The remaining instructions in the straight-line block are speculative.
; The generated source must show the guard AFTER the 2 observed instructions
; and BEFORE the speculative tail, within the same block label.
;
; Layout:
;   start -> observed_prefix (2 instructions executed) -> branch out
;   When branch not taken: continues into speculative_tail (never executed during discovery)
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: CMP makes JE taken -> branch to done_early
;   0x01 = speculative path: CMP makes JE not taken -> fallthrough to speculative tail
;
; Expected memory layout:
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = 0xDD (discovery: done_early) or 0xEE (speculative: speculative tail)
;   [0x0402] = 0xAA (done marker)

use16

start:
    mov byte [0x0400], 0x01

    mov al, byte [0x0500]
    cmp al, 0x00
    je done_early

    ; Speculative tail: JE not taken -> these instructions are never executed during discovery
    mov byte [0x0401], 0xEE
    jmp done

done_early:
    ; Discovery path: JE taken
    mov byte [0x0401], 0xDD

done:
    mov byte [0x0402], 0xAA
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
