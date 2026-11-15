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
;   U : unscaled mask index (and bx,1Fh; jmp [cs:bx+T], the Dune A337 shape):
;       the index is a byte offset into a word table of consecutive 3-byte
;       near-jump stubs, so only even offsets are whole entries. With the
;       byte-offset rule the jump reads the 16 real entries; without it half
;       the targets are words straddling two stubs (junk addresses inside the
;       stub block).
;   S : speculative bounded [es:si+T] on an arm the discovery run never takes:
;       si is reloaded on the arm so its value is unknown at the branch anchor;
;       the backward walk bounds it through an and-mask and supplies the entry
;       offsets the anchor evaluation reads at runtime.
;   N1..N7: negative twins on never-taken jnz targets - explored or refused but
;       never executed, so generated code must keep them untested:
;   G : a never-executed table entry points at junk J: two valid instructions,
;       then a conditional branch to invalid bytes (as assembled), while J's
;       fall-through runs into real region g_r - also the unexecuted arm of
;       g_branch's observed jnz, taken by the unobserved run only. The guess is
;       condemned and swept; g_r survives because the observed branch reaches it.
;   T : twin of G: the invalid bytes were overwritten at init, so they never
;       hold their loaded value and nothing condemns the guess.
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
;   [0x040D] = 0xDD or 0xBB (U: unscaled mask index, entry = selector*2)
;   [0x040E] = 0xDD or 0xCC (W: self-patched code at a guess's fall-through - unobserved only)
;   [0x040F] = 0xDD or 0xEE (G: region the swept junk's fall-through reaches - unobserved only)
;   [0x0410] = 0xDD or 0xF0 (T: same for the twin whose guess survives - unobserved only)
;   [0x0407] = 0xAA (done)

use16

start:
    mov byte [0x0400], 0x01
    mov byte [0x0408], 0x01 ; poison marker initial value (low RAM may be nonzero)
    mov byte [0x040C], 0x00 ; S marker initial value (low RAM may be nonzero)
    mov byte [0x040E], 0xDD ; W marker initial value (low RAM may be nonzero)
    mov byte [0x040F], 0xDD ; G marker initial value
    mov byte [0x0410], 0xDD ; T marker initial value
    mov ax, cs
    mov es, ax              ; es = cs for the self-patch; ds stays 0 for markers

    ; R setup: trash the first bytes of retry_entry so the first table resolution
    ; leaves its candidate pending (bytes differ from the loaded image).
    mov byte [es:retry_entry], 0x90
    mov byte [es:retry_entry+1], 0x90
    mov byte [es:retry_entry+2], 0x90

    ; T setup: t_invalid's first byte differs from the loaded image before any
    ; table resolves, so t_junk's invalid successor is never trusted evidence.
    mov byte [es:t_invalid], 0x0F

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
    ; U: unscaled mask index. bx = selector*2 (0 on the discovery run, 2 on the
    ; unobserved run), bounded by and bx,1Fh; the scale step sits outside the
    ; recognizer's back-walk window, so the index looks like a bare byte offset
    ; into u_table's word entries and only the 16 even offsets are whole
    ; entries. Entry N jumps to stub u_sN, which jumps to the matching case.
u_switch:
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1
    times 17 nop
    mov bx, ax
    and bx, 1Fh
    jmp word [cs:bx+u_table]
u_case0:
    mov byte [0x040D], 0xDD
    jmp g_branch
u_case1:
    mov byte [0x040D], 0xBB
    jmp g_branch
u_case2:
    hlt
u_case3:
    hlt
u_case4:
    hlt
u_case5:
    hlt
u_case6:
    hlt
u_case7:
    hlt
u_case8:
    hlt
u_case9:
    hlt
u_case10:
    hlt
u_case11:
    hlt
u_case12:
    hlt
u_case13:
    hlt
u_case14:
    hlt
u_case15:
    hlt
    ; G: g_table's middle entry is never executed and points at junk J inside
    ; the image. J decodes two valid instructions; its conditional branch goes
    ; to bytes that decode invalid as assembled, and its fall-through runs into
    ; the real region g_r. g_r is also the unexecuted arm of g_branch's observed
    ; jnz, taken by the unobserved run only. The guess that owns J is condemned
    ; and swept; g_r survives because the observed branch reaches it too.
g_branch:
    cmp byte [0x0500], 0
    jnz g_r
g_switch:
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1
    cmp ax, 4
    ja g_default
    mov bx, ax
    jmp word [cs:bx+g_table]
g_default:
    hlt
g_case0:
    jmp t_branch
g_case2:
    jmp t_branch
g_junk:
    nop
    jnz g_invalid
g_r:
    mov byte [0x040F], 0xEE
    jmp g_switch
g_invalid:
    times 8 db 0xFF

    ; T: twin of G where the invalid bytes no longer hold their loaded values
    ; when the table resolves (init overwrote one), so nothing is evidence
    ; and the guess stays.
t_branch:
    cmp byte [0x0500], 0
    jnz t_r
t_switch:
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1
    cmp ax, 4
    ja t_default
    mov bx, ax
    jmp word [cs:bx+t_table]
t_default:
    hlt
t_case0:
    jmp w_switch
t_case2:
    jmp w_switch
t_junk:
    nop
    jnz t_invalid
t_r:
    mov byte [0x0410], 0xF0
    jmp t_switch
t_invalid:
    times 8 db 0xFF

    ; W: w_table's third entry is never executed: it lands on a lone nop whose
    ; fall-through f holds 0xFF bytes while the table resolves, so a guess's
    ; fall-through must not poison f. The program then writes real code at f
    ; (a marker store and a jump back), and the jnz takes f only on the
    ; unobserved run.
w_switch:
    mov al, byte [0x0500]
    mov ah, 0
    shl ax, 1
    cmp ax, 4
    ja w_default
    mov bx, ax
    jmp word [cs:bx+w_table]
w_default:
    hlt
w_case0:
    jmp w_write
w_case1:
    jmp w_write
w_case2:
    nop
f:
    times 8 db 0xFF
w_write:
    mov word [es:f], 0x06C6              ; C6 06 = mov byte [m],imm8
    mov word [es:f+2], 0x040E            ; m = 0x040E
    mov byte [es:f+4], 0xCC              ; imm
    mov byte [es:f+5], 0xE9              ; jmp near
    mov word [es:f+6], poison_section-(f+8)
    ; The branch block is only reached through this indirect jump, so static
    ; speculation cannot walk to jnz f before the patch above has run.
    mov bx, w_branch
    jmp bx
w_branch:
    cmp byte [0x0500], 0
    jnz f
poison_section:
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
; 16 word entries pointing at consecutive 3-byte near-jump stubs: a word read
; at an odd table offset lands inside the stub block (junk target).
u_table:       dw u_s0, u_s1, u_s2, u_s3, u_s4, u_s5, u_s6, u_s7
               dw u_s8, u_s9, u_s10, u_s11, u_s12, u_s13, u_s14, u_s15
w_table:       dw w_case0, w_case1, w_case2
g_table:       dw g_case0, g_case2, g_junk
t_table:       dw t_case0, t_case2, t_junk
u_s0:          jmp near u_case0
u_s1:          jmp near u_case1
u_s2:          jmp near u_case2
u_s3:          jmp near u_case3
u_s4:          jmp near u_case4
u_s5:          jmp near u_case5
u_s6:          jmp near u_case6
u_s7:          jmp near u_case7
u_s8:          jmp near u_case8
u_s9:          jmp near u_case9
u_s10:         jmp near u_case10
u_s11:         jmp near u_case11
u_s12:         jmp near u_case12
u_s13:         jmp near u_case13
u_s14:         jmp near u_case14
u_s15:         jmp near u_case15

; Half-entry reads of u_table land in this span (entry N's high byte plus
; entry N+1's low byte): a reader that treats every byte offset as an entry
; finds hlt decoys here, so the junk targets survive in the dump.
rb 0x1A07-$
u_junk:
    times 0xCF01 db 0xF4

rb 65520-$
jmp start
rb 65535-$
db 0ffh
