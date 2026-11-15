; compile it with fasm
; Speculative pushed-return fixture: the program builds a call by hand -
; it pushes a return address, then jumps indirectly. The callee's ret lands on
; the pushed address, which the explorer discovers as a PushedReturn candidate.
;
; Sections:
;   S1  push imm16; jmp word [cs:bx+T]  (table also resolves; unobserved entry 2)
;   S2  mov dx, ret2; push dx; jmp bx   (push r16 set by mov r16, imm16)
;   S3  NEGATIVE: push dx set from memory (argument push); the arm pops it
;   S4  NEGATIVE: plain tail jump, nothing pushed
;   S5  NEGATIVE: push of a register set by a non-`mov imm16` instruction between
;       scale and transfer -> the table stays refused (untested cases remain)
;   S6  pushed address outside the image code: decodes a bounded hlt, no poison
;
; Selector byte at [0x0500]: 0x00 = discovery run (S1 entry 0 -> c0),
; 0x01 = unobserved run (S1 entry 2 -> c2).
;
; Expected memory:
;   [0x0400] = 0x01
;   [0x0401] = 0xC0 (discovery) or 0xC2 (unobserved: c2 marker)
;   [0x0402] = 0xBB (ret1 reached through the callee's ret)
;   [0x0403] = 0xCC (ret2)
;   [0x0404] = 0xEE (S4 tail target)
;   [0x0405] = 0x55 (S5 entry 0)
;   [0x0408] = 0xDD (S3 arm popped the arg)
;   [0x0409] = 0x66 (S6 tail target)

use16

start:
    mov byte [0x0400], 0x01
    mov byte [0x0401], 0x00
    mov byte [0x0402], 0x00
    mov byte [0x0403], 0x00
    mov byte [0x0404], 0x00
    mov byte [0x0405], 0x00
    mov byte [0x0408], 0x00
    mov byte [0x0409], 0x00
    ; bound the out-of-image decode for S3/S6: two hlt bytes at 0x9000
    mov byte [cs:0x9000], 0xF4
    mov byte [cs:0x9001], 0xF4

    mov al, byte [0x0500]
    cmp al, 0
    jne unobserved_path

    mov ax, 0
    jmp tail1
unobserved_path:
    mov ax, 2
    jmp tail1

; --- S1: push imm16; jmp word [cs:bx+T] ---
tail1:
    cmp ax, 2
    ja tail1_default
    mov bx, ax
    shl bx, 1
    push word ret1
    jmp word [cs:bx+jmp_table1]
ret1:
    mov byte [0x0402], 0xBB
    jmp s2
tail1_default:
    hlt
c0:
    mov byte [0x0401], 0xC0
    ret
c1:
    hlt
c2:
    mov byte [0x0401], 0xC2
    ret

; --- S2: mov dx, ret2; push dx; jmp bx ---
s2:
    mov bx, s2_target
    mov dx, ret2
    push dx
    jmp bx
ret2:
    mov byte [0x0403], 0xCC
    jmp s3
s2_target:
    ret

; --- S3 NEGATIVE: push dx set from memory (argument push); arm pops it ---
s3:
    mov byte [0x0406], 0x00
    mov byte [0x0407], 0x90
    mov bx, s3_target
    mov dx, word [0x0406]      ; dx = 0x9000, from memory not an immediate
    push dx
    jmp bx
s3_target:
    pop cx                     ; the arm pops the argument
    mov byte [0x0408], 0xDD
    jmp s4

; --- S4 NEGATIVE: plain tail jump, nothing pushed ---
s4:
    mov bx, s4_target
    jmp bx
s4_target:
    mov byte [0x0404], 0xEE
    jmp s5

; --- S5 NEGATIVE: push r16 set by a non-`mov imm16` write between scale and
; --- transfer; the table is refused and its unexecuted entries stay untested ---
s5:
    mov byte [0x0406], 0x00
    mov byte [0x0407], 0x90
    mov dx, word [0x0406]      ; dx = 0x9000, not an immediate setter
    mov ax, 0
    cmp ax, 1
    ja tail5_default
    mov bx, ax
    shl bx, 1
    push dx
    jmp word [cs:bx+jmp_table5]
tail5_default:
    hlt
t5a:
    mov byte [0x0405], 0x55
    jmp s6
t5b:
    hlt

; --- S6: pushed address outside the image code decodes a bounded hlt ---
s6:
    mov bx, s6_target
    push word 0x9000
    jmp bx
s6_target:
    mov byte [0x0409], 0x66
    jmp done

done:
    hlt

align 2
jmp_table1:    dw c0, c1, c2
jmp_table5:    dw t5a, t5b

rb 65520-$
jmp start
rb 65535-$
db 0ffh
