; compile it with fasm
; Record-array discovery fixture: a search loop leaves DI on a 14-byte record
; and the caller calls through the record's handler field. A backward walk from
; the site recognizes the loop (`add di,14` under `loop`) and reads every
; record's handler as a guarded speculative target.
;
;   P1: count immediate (`mov cx,3`), site on the observed path.
;   P2: count in a header cell (`mov cx,[cs:di]` / `add di,2`), plus a dead
;       `sub cx,1` entry path the backward walk skips.
;   P3: two lists on two entry paths sharing one loop head.
;   P4: far handlers (`call far [cs:di+0Ah]`) on an arm the discovery run
;       never takes: the site is speculative and registers through its anchor.
;   P5: the search loop sits inline in the same function as the site.
;   P6: a second path loads DI from a global; it adds nothing, the loop list
;       still applies.
;   P7: no loop, a compare ladder (`cmp ax,Kn / je` between `add di,14` steps)
;       yields one constant record list per path.
;   P8: the records live in another data segment (DS=F800, list at DS:0000 =
;       image offset 0x8000); a near `call [di+0Ch]` still lands in CS.
;   P9: forty instructions that do not write DI sit between the search return
;       and the site, in one block; the walk must reach the loop anyway.
;   N1..N4 (negative twins, dead `cmp [0x0500],0xFF / je` arms): DI written
;       between return and site, a DI-writing call between return and site, a
;       register-stride loop, an unrecognized count.
;   N5 (observed): the site runs through a global-path DI whose record is in no
;       recognized list, so its observed target is not among the read records:
;       the site gets no new target, the global handler still runs.
;   N6 (dead arm): a CX-writing call sits between `mov cx,3` and the loop on the
;       entry path, so the entry adds nothing and the site stays untested.
;
; Selector byte at DS:0500h: 0 = discovery run, 1 = unobserved run. DS = 0,
; CS = image (F000); record reads use cs: overrides (P8's list reads use DS).
;
; Result bytes at DS:0400h..041Eh:
;   [0400] = 02 when the program completed
;   [0401..0403] = h_a0..h_a2   (P1, list_a)
;   [0404..0406] = h_b0..h_b2   (P2, list_b)
;   [0407..0408] = h_c10,h_c11  (P3, list_c1)
;   [0409..040B] = h_c20..h_c22 (P3, list_c2)
;   [040C..040E] = fd0..fd2     (P4, list_d, far handlers)
;   [040F..0411] = h_e0..h_e2   (P5, list_e)
;   [0412..0414] = h_f0..h_f2   (P6, list_f)
;   [0415..0417] = h_70..h_72   (P7, rec_g)
;   [0418]       = h_n5         (N5, global record)
;   [0419..041B] = h_i0..h_i2   (P8, list_h in DS=F800)
;   [041C..041E] = h_j0..h_j2   (P9, list_j)

use16

KEY0 = 0x11
KEY1 = 0x22
KEY2 = 0x33

start:
    ; zero the marker bytes 0400..041F (ES = DS = 0)
    push ds
    pop es
    mov di, 0x0400
    mov cx, 0x20
    xor ax, ax
    cld
    rep stosb

    ; Warmup: one observed call per find function and handler builds the
    ; return evidence speculative call sites need for their continuations.
    call find_a
    call find_b
    call find_c
    call find_d
    call find_f
    call find_g
    call find_n3
    call find_n4
    call find_h
    call find_j
    call find_n6
    call writes_cx
    call set_di
    call h_a0
    call h_a1
    call h_a2
    call h_b0
    call h_b1
    call h_b2
    call h_c10
    call h_c11
    call h_c20
    call h_c21
    call h_c22
    call 0xF000:fd0
    call 0xF000:fd1
    call 0xF000:fd2
    call h_e0
    call h_e1
    call h_e2
    call h_f0
    call h_f1
    call h_f2
    call h_70
    call h_71
    call h_72
    call h_n5
    call h_i0
    call h_i1
    call h_i2
    call h_j0
    call h_j1
    call h_j2

; --- P1: count immediate ---
    call key_for_selector               ; AL = KEY0 (discovery) or KEY2 (unobserved)
    call find_a
    jnc p1_miss
    call word [cs:di+0x0C]
p1_miss:
    call find_a
    jnc p1_done
    call word [cs:di+0x0C]
p1_done:

; --- P2: count in a header cell, Dune shape ---
    call key_for_selector
    call find_b
    jnc p2_miss
    call word [cs:di+0x0C]
p2_miss:
    call find_b
    jnc p2_done
    call word [cs:di+0x0C]
p2_done:

; --- P3: two lists on two entry paths ---
    call key_for_selector
    call find_c
    jnc p3_miss
    call word [cs:di+0x0C]
p3_miss:
    call find_c
    jnc p3_done
    call word [cs:di+0x0C]
p3_done:

; --- P4: far handlers on the unobserved arm (speculative site via anchor) ---
    call p4_dispatch
    call p4_dispatch

; --- P5: search inline in the same function ---
    call key_for_selector
    call p5_body
    call p5_body

; --- P6: a global-loaded second path adds nothing ---
    call key_for_selector
    call find_f
    jc p6_found
    mov di, word [cs:ptr_f]
p6_found:
    call word [cs:di+0x0C]
    call find_f
    jc p6_found2
    mov di, word [cs:ptr_f]
p6_found2:
    call word [cs:di+0x0C]

; --- P7: constant-pointer compare ladder ---
    call key_for_selector7                ; AX = KEY0 (discovery) or KEY1 (unobserved)
    call find_g
    jnc p7_miss
    call word [cs:di+0x0C]
p7_miss:
    call key_for_selector7
    call find_g
    jnc p7_done
    call word [cs:di+0x0C]
p7_done:

; --- N5: site reached only through the global path ---
    call key_for_selector
    call find_n5                          ; always misses: DI comes from ptr_n5
    jc n5_found
    mov di, word [cs:ptr_n5]
n5_found:
    call word [cs:di+0x0C]

; --- P8: records in another data segment (list_h at F800:0000) ---
    call key_for_selector
    push ds
    mov bx, 0xF800
    mov ds, bx
    call find_h
    jnc p8_miss
    call word [di+0x0C]
p8_miss:
    pop ds
    call key_for_selector
    push ds
    mov bx, 0xF800
    mov ds, bx
    call find_h
    jnc p8_done
    call word [di+0x0C]
p8_done:
    pop ds

; --- P9: a long straight-line block before the site ---
    call key_for_selector
    call find_j
    jnc p9_miss
    times 40 nop
    call word [cs:di+0x0C]
p9_miss:
    call key_for_selector
    call find_j
    jnc p9_done
    times 40 nop
    call word [cs:di+0x0C]
p9_done:

; --- negative twins on dead arms ---
    call n1_dispatch
    call n1_dispatch
    call n2_dispatch
    call n2_dispatch
    call n3_dispatch
    call n3_dispatch
    call n4_dispatch
    call n4_dispatch
    call n6_dispatch
    call n6_dispatch

    mov byte [0x0400], 0x02
    hlt

; AL = key for the current selector byte: KEY0 when 0, KEY2 otherwise.
key_for_selector:
    mov bl, byte [0x0500]
    xor bh, bh
    mov al, byte [cs:key_sel+bx]
    ret
key_sel:
    db KEY0, KEY2

; AX = key for P7: KEY0 when 0, KEY1 otherwise.
key_for_selector7:
    mov bl, byte [0x0500]
    xor bh, bh
    movzx ax, byte [cs:key_sel7+bx]
    ret
key_sel7:
    db KEY0, KEY1

; ---------------------------------------------------------------------------
; Search functions: key in AL, return CF=1 with DI on the record, CF=0 on miss.
; ---------------------------------------------------------------------------

find_a:
    mov di, list_a
    mov cx, 3
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

find_b:
    mov di, hdr_b
    mov cx, word [cs:di]
    add di, 2
    cmp byte [0x0500], 0xFF
    jne .l
    sub cx, 1                               ; dead arm: skipped by the count walk
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

find_c:
    cmp byte [0x0500], 0
    jne .arm2
    mov di, hdr_c1
    mov cx, word [cs:di]
    add di, 2
    jmp .l
.arm2:
    mov di, list_c2
    mov cx, 2
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

find_d:
    mov di, list_d
    mov cx, 3
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

find_f:
    mov di, list_f
    mov cx, 3
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

; Compare-ladder search (P7): key in AX.
find_g:
    mov di, rec_g
    cmp ax, KEY0
    je .k
    add di, 14
    cmp ax, KEY1
    je .k
    add di, 14
.k:
    stc
    ret

; N3 negative: register stride.
find_n3:
    mov di, list_n3
    mov cx, 3
    mov ax, 14
.l:
    cmp byte [cs:di], al
    je .hit
    add di, ax
    loop .l
    clc
    ret
.hit:
    stc
    ret

; N4 negative: unrecognized count.
find_n4:
    mov di, list_n4
    mov ax, 3
    mov cx, ax
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

; P8: searches list_h under DS=F800 (the caller sets DS; the list sits at DS:0000).
find_h:
    mov di, 0
    mov cx, 3
.l:
    cmp byte [di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

; P9: same shape as find_a; the caller puts a long block between return and site.
find_j:
    mov di, list_j
    mov cx, 3
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

; N6 negative: a CX-writing call sits between `mov cx,3` and the loop on the
; entry path, so the count the walk would read is not the count the loop uses.
find_n6:
    mov di, list_n6
    mov cx, 3
    call writes_cx
    nop
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

writes_cx:
    mov cx, 9
    ret

; N5: never finds anything (searches a list that is not searched for KEY0/KEY2).
find_n5:
    mov di, list_n5
    mov cx, 1
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    clc
    ret
.hit:
    stc
    ret

set_di:
    mov di, ax
    ret

; ---------------------------------------------------------------------------
; Sections P4/P5 and the negative arms.
; ---------------------------------------------------------------------------

p4_dispatch:
    cmp byte [0x0500], 0
    jne .arm
    ret
.arm:
    call key_for_selector
    call find_d
    jnc .miss
    call far [cs:di+0x0A]
.miss:
    ret

p5_body:
    mov di, list_e
    mov cx, 3
.l:
    cmp byte [cs:di], al
    je .hit
    add di, 14
    loop .l
    jmp .miss
.hit:
    call word [cs:di+0x0C]
.miss:
    ret

n1_dispatch:
    cmp byte [0x0500], 0xFF
    je .dead
    ret
.dead:
    call find_a
    jnc .m
    add di, 4                               ; DI written between return and site
    call word [cs:di+0x0C]
.m:
    ret

n2_dispatch:
    cmp byte [0x0500], 0xFF
    je .dead
    ret
.dead:
    call find_a
    jnc .m
    call set_di                             ; a DI-writing call before the site
    call word [cs:di+0x0C]
.m:
    ret

n3_dispatch:
    cmp byte [0x0500], 0xFF
    je .dead
    ret
.dead:
    call find_n3
    jnc .m
    call word [cs:di+0x0C]
.m:
    ret

n4_dispatch:
    cmp byte [0x0500], 0xFF
    je .dead
    ret
.dead:
    call find_n4
    jnc .m
    call word [cs:di+0x0C]
.m:
    ret

n6_dispatch:
    cmp byte [0x0500], 0xFF
    je .dead
    ret
.dead:
    call find_n6
    jnc .m
    call word [cs:di+0x0C]
.m:
    ret

; ---------------------------------------------------------------------------
; Record lists. Record = 14 bytes: key at +0, far handler dword at +0x0A
; (offset then segment), near handler word at +0x0C.
; ---------------------------------------------------------------------------

macro NEARREC key, handler {
    db key
    rb 9
    dw 0, handler
}

macro FARREC key, handler {
    db key
    rb 9
    dw handler, 0xF000
}

list_a:
    NEARREC KEY0, h_a0
    NEARREC KEY1, h_a1
    NEARREC KEY2, h_a2

hdr_b:
    dw 3
list_b:
    NEARREC KEY0, h_b0
    NEARREC KEY1, h_b1
    NEARREC KEY2, h_b2

hdr_c1:
    dw 2
list_c1:
    NEARREC KEY0, h_c10
    NEARREC KEY1, h_c11

list_c2:
    NEARREC KEY0, h_c20
    NEARREC KEY2, h_c21
    NEARREC 0x44, h_c22

list_d:
    FARREC KEY0, fd0
    FARREC KEY1, fd1
    FARREC KEY2, fd2

list_e:
    NEARREC KEY0, h_e0
    NEARREC KEY1, h_e1
    NEARREC KEY2, h_e2

list_f:
    NEARREC KEY0, h_f0
    NEARREC KEY1, h_f1
    NEARREC KEY2, h_f2
ptr_f:
    dw list_f

rec_g:
    NEARREC KEY0, h_70
    NEARREC KEY1, h_71
    NEARREC KEY2, h_72

list_n3:
    NEARREC KEY0, h_a0
    NEARREC KEY1, h_a1
    NEARREC KEY2, h_a2

list_n4:
    NEARREC KEY0, h_a0
    NEARREC KEY1, h_a1
    NEARREC KEY2, h_a2

list_n5:
    NEARREC 0x77, h_a0                    ; keys never searched: find_n5 misses
rec_n5:
    NEARREC 0x77, h_n5
ptr_n5:
    dw rec_n5

list_j:
    NEARREC KEY0, h_j0
    NEARREC KEY1, h_j1
    NEARREC KEY2, h_j2

list_n6:
    NEARREC KEY0, h_a0
    NEARREC KEY1, h_a1
    NEARREC KEY2, h_a2

; list_h lives in another data segment: DS=F800 puts it at image offset 0x8000.
times 0x8000-$ db 0
list_h:
    NEARREC KEY0, h_i0
    NEARREC KEY1, h_i1
    NEARREC KEY2, h_i2

; ---------------------------------------------------------------------------
; Handlers.
; ---------------------------------------------------------------------------

h_a0:  inc byte [0x0401]
       ret
h_a1:  inc byte [0x0402]
       ret
h_a2:  inc byte [0x0403]
       ret
h_b0:  inc byte [0x0404]
       ret
h_b1:  inc byte [0x0405]
       ret
h_b2:  inc byte [0x0406]
       ret
h_c10: inc byte [0x0407]
       ret
h_c11: inc byte [0x0408]
       ret
h_c20: inc byte [0x0409]
       ret
h_c21: inc byte [0x040A]
       ret
h_c22: inc byte [0x040B]
       ret
fd0:   inc byte [0x040C]
       retf
fd1:   inc byte [0x040D]
       retf
fd2:   inc byte [0x040E]
       retf
h_e0:  inc byte [0x040F]
       ret
h_e1:  inc byte [0x0410]
       ret
h_e2:  inc byte [0x0411]
       ret
h_f0:  inc byte [0x0412]
       ret
h_f1:  inc byte [0x0413]
       ret
h_f2:  inc byte [0x0414]
       ret
h_70:  inc byte [0x0415]
       ret
h_71:  inc byte [0x0416]
       ret
h_72:  inc byte [0x0417]
       ret
h_n5:  inc byte [0x0418]
       ret
; P8 handlers run while DS=F800: they restore DS to write their markers.
h_i0:  push ds
       push ax
       xor ax, ax
       mov ds, ax
       inc byte [0x0419]
       pop ax
       pop ds
       ret
h_i1:  push ds
       push ax
       xor ax, ax
       mov ds, ax
       inc byte [0x041A]
       pop ax
       pop ds
       ret
h_i2:  push ds
       push ax
       xor ax, ax
       mov ds, ax
       inc byte [0x041B]
       pop ax
       pop ds
       ret
h_j0:  inc byte [0x041C]
       ret
h_j1:  inc byte [0x041D]
       ret
h_j2:  inc byte [0x041E]
       ret

rb 65520-$
jmp start
rb 65535-$
db 0ffh
