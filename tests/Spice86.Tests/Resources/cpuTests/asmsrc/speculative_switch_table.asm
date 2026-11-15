; compile it with fasm
; Speculative switch-table fixture: an observed near indirect jump through a word
; memory operand preceded by the canonical bounds-check sequence must have its
; table entries explored speculatively, so the unobserved case runs in generated
; code instead of failing "Untested near jump target".
;
; Selector byte at [0x0500]: 0x00 = discovery run (index 0), 0x01 = unobserved
; run (index 2). One linear flow: the index fed to every switch is selector*2.
;
; Sections (each switch jumps to table base + 2*index):
;   P1: dos_re shape  cmp ax,N; ja def; xchg bx,ax; shl bx,1; jmp [cs:bx+T]
;   P2: jbe form      cmp ax,N; jbe sw / sw: mov bx,ax; shl bx,1; jmp [cs:bx+T]
;   P3: imm16 bound   cmp ax,imm16 (N>127 forces the iw encoding)
;   P4: Watcom byte   cmp al,N; ja def; xor ah,ah; mov bx,ax; shl bx,1; jmp [cs:bx+T]
;   P5: DS table      jmp [bx+T] (default segment)
;   P6: add bx,bx scale   cmp ax,N; ja def; mov bx,ax; add bx,bx; jmp [cs:bx+T]
;   P7: add R,imm bias    ...; mov bx,ax; add bx,bx; add bx,4; jmp [cs:bx+T]
;   P8: and bound         ...; mov bx,ax; and bx,3; add bx,bx; jmp [cs:bx+T]
;   R : retry - a table entry target is first trashed (untrusted -> pending on
;       the transfer), then restored to its loaded bytes and resolved by the
;       second execution of the same switch (bound 1, index = selector).
;   X : poison - a table entry names an image address holding FF bytes (decodes
;       invalid, dropped without poisoning). The program then writes real code
;       there, and an observed branch has it as its unexecuted arm, taken by the
;       unobserved run only. If TableEntry items poisoned, that arm would never
;       be explored and the unobserved run would fail as untested.
;   S : speculative bounded [es:si+T] on an arm the discovery run never takes:
;       si is reloaded on the arm so its value is unknown at the branch anchor;
;       the backward walk bounds it through an and-mask and supplies the entry
;       offsets the anchor evaluation reads at runtime.
;   N1..N7: negative twins on never-taken jnz targets - explored or refused but
;       never executed, so generated code must keep them untested:
;   N1: shl before the compare (compare on the pre-transform register) -
;       RESOLVES on purpose since the backward walk carries the bound on AX
;       through the mov; the golden records it
;   N2: foreign write to the tracked register   - must not resolve
;   N3: table contains a zero entry             - must not resolve
;   N4: cbw with bound >= 0x80                  - must not resolve
;   N5: add R,R' of a different register        - must not resolve
;   N6: and R,mask with mask not 2^n-1          - must not resolve
;   N7: sub R,imm not an accepted transform     - must not resolve
;
; Expected memory:
;   [0x0400] = 0x01
;   [0x0401] = 0xDD (discovery) or 0x11 (unobserved: P1 case2 marker)
;   [0x0402] = 0xDD or 0x22 (P2)
;   [0x0403] = 0xDD or 0x33 (P3)
;   [0x0404] = 0xDD or 0x44 (P4)
;   [0x0405] = 0xDD or 0x55 (P5)
;   [0x0406] = 0x77 (R: retry entry ran - only on the unobserved run)
;   [0x0408] = 0x66 (X: poison arm ran - only on the unobserved run)
;   [0x0409] = 0xDD or 0x66 (P6: add bx,bx scale)
;   [0x040A] = 0xDD or 0x77 (P7: add R,imm bias)
;   [0x040B] = 0xDD or 0x88 (P8: and-mask bound)
;   [0x040C] = 0x99 (S: speculative anchor table - only on the unobserved run)
;   [0x0407] = 0xAA (done)

use16

start:
    mov byte [0x0400], 0x01
    mov byte [0x0408], 0x01 ; poison marker initial value (low RAM may be nonzero)
    mov byte [0x040C], 0x00 ; S marker initial value (low RAM may be nonzero)
    mov ax, cs
    mov es, ax              ; es = cs for the self-patch; ds stays 0 for markers

    ; R setup: trash the first bytes of retry_entry so the first table resolution
    ; leaves its candidate pending (bytes differ from the loaded image).
    mov byte [es:retry_entry], 0x90
    mov byte [es:retry_entry+1], 0x90
    mov byte [es:retry_entry+2], 0x90

    ; S setup: the arm's index source lives at [0x0502] (1 -> s_case1)
    mov word [0x0502], 1

    ; P1: dos_re shape
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1               ; ax = index (0 or 2); flags recomputed below
    cmp ax, 2
    ja p1_default
    xchg bx, ax
    shl bx, 1
    jmp word [cs:bx+p1_table]
p1_default:
    hlt
p1_case0:
    mov byte [0x0401], 0xDD
    jmp p2
p1_case1:
    hlt
p1_case2:
    mov byte [0x0401], 0x11
    jmp p2

    ; P2: jbe form - fallthrough is default, taken branch reaches the switch
p2:
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1
    cmp ax, 2
    jbe p2_switch
    hlt
p2_switch:
    mov bx, ax
    shl bx, 1
    jmp word [cs:bx+p2_table]
p2_case0:
    mov byte [0x0402], 0xDD
    jmp p3
p2_case1:
    hlt
p2_case2:
    mov byte [0x0402], 0x22
    jmp p3

    ; P3: imm16 bound encoding (bound 300 forces cmp ax, imm16)
p3:
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1
    cmp ax, 300
    ja p3_default
    mov bx, ax
    shl bx, 1
    jmp word [cs:bx+p3_table]
p3_default:
    hlt
p3_case0:
    mov byte [0x0403], 0xDD
    jmp p4
p3_case1:
    hlt
p3_case2:
    mov byte [0x0403], 0x33
    jmp p4

    ; P4: Watcom byte form
p4:
    mov al, byte [0x0500]
    shl al, 1               ; al = index (0 or 2)
    cmp al, 2
    ja p4_default
    xor ah, ah
    mov bx, ax
    shl bx, 1
    jmp word [cs:bx+p4_table]
p4_default:
    hlt
p4_case0:
    mov byte [0x0404], 0xDD
    jmp p5
p4_case1:
    hlt
p4_case2:
    mov byte [0x0404], 0x44
    jmp p5

    ; P5: table in DS (default segment) - read the selector while DS=0, then
    ; point DS at the code segment for the table read.
p5:
    mov cl, byte [0x0500]
    mov ax, cs
    mov ds, ax
    mov al, cl
    mov ah, 0
    shl ax, 1
    cmp ax, 2
    ja p5_default
    mov bx, ax
    shl bx, 1
    jmp word [bx+p5_table]
p5_default:
    hlt
p5_case0:
    xor dx, dx              ; restore DS = 0 for the marker write
    mov ds, dx
    mov byte [0x0405], 0xDD
    jmp p6b
p5_case1:
    hlt
p5_case2:
    xor dx, dx
    mov ds, dx
    mov byte [0x0405], 0x55
    jmp p6b

    ; P6: add bx,bx as the scale step instead of shl
p6b:
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1
    cmp ax, 2
    ja p6b_default
    mov bx, ax
    add bx, bx
    jmp word [cs:bx+p6b_table]
p6b_default:
    hlt
p6b_case0:
    mov byte [0x0409], 0xDD
    jmp p7b
p6b_case1:
    hlt
p6b_case2:
    mov byte [0x0409], 0x66
    jmp p7b

    ; P7: add R,imm folds a bias into the table offset (bx = 2*ax + 4)
p7b:
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1
    cmp ax, 2
    ja p7b_default
    mov bx, ax
    add bx, bx
    add bx, 4
    jmp word [cs:bx+p7b_table]
p7b_default:
    hlt
p7b_case0:
    mov byte [0x040A], 0xDD
    jmp p8b
p7b_case1:
    hlt
p7b_case2:
    mov byte [0x040A], 0x77
    jmp p8b

    ; P8: the bound is an and-mask (bx = ax & 3, then doubled)
p8b:
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1
    mov bx, ax
    and bx, 3
    add bx, bx
    jmp word [cs:bx+p8b_table]
p8b_case0:
    mov byte [0x040B], 0xDD
    jmp p9
p8b_case1:
    hlt
p8b_case2:
    mov byte [0x040B], 0x88
    jmp p9
p8b_case3:
    hlt

    ; S: speculative bounded [es:si+T] on an arm only the unobserved run takes.
    ; es = cs was installed at start, so the segment is known at the jnz
    ; anchor; si is reloaded on the arm and bounded by the and-mask, so the
    ; walk gives the anchor evaluation the entry offsets {s_table, s_table+2}.
p9:
    cmp byte [0x0500], 0
    jnz s_arm
    jmp s_done
s_arm:
    mov si, word [0x0502]
    and si, 1
    add si, si
    jmp word [es:si+s_table]
s_case0:
    hlt
s_case1:
    mov byte [0x040C], 0x99
s_done:

    ; R: the SAME switch executes twice. The first pass takes case 0 on both
    ; runs; retry_entry's bytes are trashed at start so its candidate goes
    ; pending on the transfer. case0 restores the loaded bytes and loops back
    ; with index = selector: 0 on the discovery run (same edge again, so only
    ; the pending retry can resolve retry_entry), 1 on the unobserved run.
retry_first:
    xor ax, ax              ; restore DS = 0 for the remaining markers
    mov ds, ax
retry_switch:
    cmp ax, 1
    ja retry_default
    mov bx, ax
    shl bx, 1
    jmp word [cs:bx+retry_table]
retry_default:
    hlt
retry_case0:
    ; second landing (flag set) leaves the loop
    cmp byte [0x0501], 0
    jne retry_done
    mov byte [0x0501], 1
    ; restore the loaded bytes of retry_entry (mov byte [0x0406],0x77 / then
    ; falls through to after_retry), then loop back with index = selector
    mov byte [es:retry_entry], 0xC6
    mov byte [es:retry_entry+1], 0x06
    mov word [es:retry_entry+2], 0x0406
    mov byte [es:retry_entry+4], 0x77
    mov al, byte [0x0500]
    mov ah, 0
    jmp retry_switch
retry_done:
    jmp after_retry

retry_entry:
    mov byte [0x0406], 0x77
    ; falls through to after_retry
after_retry:
    ; X: poison section. Forced index 0 keeps both runs on p6_case0; the second
    ; entry names poison_target, which still holds FF bytes at resolution time
    ; (trusted, decodes invalid, dropped without poisoning).
    xor ax, ax
    cmp ax, 1
    ja p6_default
    mov bx, ax
    shl bx, 1
    jmp word [cs:bx+p6_table]
p6_default:
    hlt
p6_case0:
    ; overwrite the poison target with real code after the table was resolved:
    ; mov byte [0x0408],0x66 ; hlt
    mov byte [es:poison_target], 0xC6
    mov byte [es:poison_target+1], 0x06
    mov word [es:poison_target+2], 0x0408
    mov byte [es:poison_target+4], 0x66
    mov byte [es:poison_target+5], 0xF4
    ; reach the tail through an indirect jump so static speculation cannot drain
    ; the jnz (and poison the FF region) before the patch executes
    mov bx, p6_tail
    jmp bx
p6_tail:
    ; observed conditional branch whose unexecuted arm is exactly poison_target;
    ; the done marker is written first because the taken arm halts.
    mov byte [0x0407], 0xAA
    cmp byte [0x0500], 0
    jnz poison_target
    jmp after_poison
poison_target:
    times 8 db 0xFF
after_poison:
    ; Negative twins: each switch executes with index 0 (both runs) so the
    ; recognizer sees the site and refuses it; the table's entry 0 continues
    ; the flow. The unexecuted entries stay untested in generated code.
n1_arm:
    ; N1: compare on the register the index was copied from - the backward
    ; walk resolves it intentionally (bound 2 on AX, bx = 2*ax, entries at
    ; n1_table + {0,2,4})
    xor ax, ax
    mov bx, ax
    shl bx, 1
    cmp ax, 2
    ja n1_default
    jmp word [cs:bx+n1_table]
n1_default:
    hlt
n1_cont:
    ; N2: foreign write to the tracked register between copy and scale
    ; (neg keeps bx = 0 so the unrecognised jump still lands on entry 0)
    xor ax, ax
    cmp ax, 2
    ja n2_default
    mov bx, ax
    neg bx
    shl bx, 1
    jmp word [cs:bx+n2_table]
n2_default:
    hlt
n2_cont:
    ; N3: the table contains a 0x0000 entry
    xor ax, ax
    cmp ax, 2
    ja n3_default
    mov bx, ax
    shl bx, 1
    jmp word [cs:bx+n3_table]
n3_default:
    hlt
n3_cont:
    ; N4: cbw with bound >= 0x80
    xor ax, ax
    cmp al, 200
    ja n4_default
    cbw
    mov bx, ax
    shl bx, 1
    jmp word [cs:bx+n4_table]
n4_default:
    hlt
n4_cont:
    ; N5: add of a different register is not an accepted transform
    ; (cx is kept 0 so the unrecognised jump still lands on entry 0)
    xor ax, ax
    xor cx, cx
    cmp ax, 2
    ja n5_default
    mov bx, ax
    add bx, cx
    jmp word [cs:bx+n5_table]
n5_default:
    hlt
n5_cont:
    ; N6: and-mask that is not 2^n-1 cannot bound the index
    xor ax, ax
    mov bx, ax
    and bx, 6
    add bx, bx
    jmp word [cs:bx+n6_table]
n6_cont:
    ; N7: sub on the index register is not an accepted transform
    ; (sub by 0 keeps bx = 0 so the unrecognised jump still lands on entry 0)
    xor ax, ax
    cmp ax, 2
    ja n7_default
    mov bx, ax
    sub bx, 0
    jmp word [cs:bx+n7_table]
n7_default:
    hlt
n7_cont:
    jmp done

done:
    hlt

align 2
p1_table:      dw p1_case0, p1_case1, p1_case2
p2_table:      dw p2_case0, p2_case1, p2_case2
p3_table:      dw p3_case0, p3_case1, p3_case2
               times 298 dw p3_case1   ; bound 300 -> 301 entries
p4_table:      dw p4_case0, p4_case1, p4_case2
p5_table:      dw p5_case0, p5_case1, p5_case2
p6b_table:     dw p6b_case0, p6b_case1, p6b_case2
p7b_table:     dw 0x1234, 0x1234, p7b_case0, 0x1234, p7b_case2
p8b_table:     dw p8b_case0, p8b_case1, p8b_case2, p8b_case3
s_table:       dw s_case0, s_case1
n1_table:      dw n1_cont, done, done
n2_table:      dw n2_cont, done, done
n3_table:      dw n3_cont, 0, done
n4_table:      dw n4_cont
               times 200 dw done       ; bound 200 -> 201 entries
n5_table:      dw n5_cont, done, done
n6_table:      dw n6_cont, done, done
n7_table:      dw n7_cont, done, done
retry_table:   dw retry_case0, retry_entry
p6_table:      dw p6_case0, poison_target

rb 65520-$
jmp start
rb 65535-$
db 0ffh
