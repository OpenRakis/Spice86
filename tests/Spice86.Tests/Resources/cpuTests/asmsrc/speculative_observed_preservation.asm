; compile it with fasm
; Observed-preservation fixture: an inline search loop leaves DI on a 14-byte
; record, a leaf call sits between the search and `call word [cs:di+0Ch]`, and
; the record-array walk must cross that call. (The search is inline because a
; search in its own callee plus the crossed call is over the walk's block
; transition bound.) The leaf's write set depends on a
; driver reached through `call far dword [cs:ptr]` whose code the static
; write scan cannot read (a direct far jump). The run sees the driver return
; balanced, so the registers it gave back unchanged answer for it.
;
;   S1 (observed site, three searches): leaf_p saves DI around the driver
;       call; the driver writes AX and DI. With the driver's observed answer
;       (AX, DI) the leaf's push/pop restores DI, so the walk reaches the loop
;       and every record's handler becomes a guarded target.
;   N1 (observed site, key 1 in both runs): leaf_n1 calls its driver without
;       saving DI; the driver changes DI on the second of three warmup
;       returns only. DI is not preserved, so the site keeps only its
;       observed target (golden-only).
;   N2 (observed site, key 1 in both runs): `push ax; call leaf_n2`, where
;       leaf_n2 cannot be read statically and returns with `ret 2`: the
;       return is not balanced, so nothing is recorded and the site keeps
;       only its observed target (golden-only).
;
; Selector byte at DS:0500h: 0 = discovery run, 1 = unobserved run. DS = 0,
; SS:SP = 2000:1000, CS = image (F000); record reads use cs: overrides.
;
; Result bytes at DS:0400h..0409h:
;   [0400] = 02 when the program completed
;   [0401..0403] = h_p0..h_p2   (S1, list_p)
;   [0404..0406] = h_n10..h_n12 (N1, list_n1)
;   [0407..0409] = h_n20..h_n22 (N2, list_n2)
; Work bytes: [0483] = S1 loop counter, [0484] = driver n1 call counter.

use16

KEY0 = 0x11
KEY1 = 0x22
KEY2 = 0x33

start:
    ; a stack away from the 64 KB wrap, so `ret 2` returns above the caller's SP
    mov ax, 0x2000
    mov ss, ax
    mov sp, 0x1000
    ; zero the marker and work bytes 0400..048F (ES = DS = 0)
    push ds
    pop es
    mov di, 0x0400
    mov cx, 0x90
    xor ax, ax
    cld
    rep stosb

    ; Warmup: observed calls give the search functions and handlers their
    ; return evidence, and the leaves and drivers their observed returns.
    call h_p0
    call h_p1
    call h_p2
    call h_n10
    call h_n11
    call h_n12
    call h_n20
    call h_n21
    call h_n22
    call leaf_n1
    call leaf_n1
    call leaf_n1
    push ax
    call leaf_n2

; --- S1: observed site, the same search three times ---
    mov byte [0x0483], 3
s1_loop:
    call key_for_selector               ; AL = KEY0 (discovery) or KEY2 (unobserved)
    mov di, list_p
    mov cx, 3
.l:
    cmp byte [cs:di], al
    je s1_hit
    add di, 14
    loop .l
    jmp s1_next
s1_hit:
    call leaf_p
    call word [cs:di+0x0C]
s1_next:
    dec byte [0x0483]
    jnz s1_loop

; --- N1: the driver changed DI on one observed return ---
    mov al, KEY0
    mov di, list_n1
    mov cx, 3
.l:
    cmp byte [cs:di], al
    je n1_hit
    add di, 14
    loop .l
    jmp n1_done
n1_hit:
    call leaf_n1
    call word [cs:di+0x0C]
n1_done:

; --- N2: unbalanced return, nothing recorded ---
    mov al, KEY0
    mov di, list_n2
    mov cx, 3
.l:
    cmp byte [cs:di], al
    je n2_hit
    add di, 14
    loop .l
    jmp n2_done
n2_hit:
    push ax
    call leaf_n2
    call word [cs:di+0x0C]
n2_done:

    mov byte [0x0400], 2
    hlt

; AL = search key: KEY0 when the selector is 0, KEY2 otherwise.
key_for_selector:
    mov bl, byte [0x0500]
    xor bh, bh
    mov al, byte [cs:key_sel+bx]
    ret
key_sel:
    db KEY0, KEY2

; ---------------------------------------------------------------------------
; Leaves and drivers.
; ---------------------------------------------------------------------------

leaf_p:
    push di
    call far dword [cs:drv_p_ptr]
    pop di
    ret

drv_p:
    mov ax, 0x1111
    mov di, 0x5555
    jmp 0xF000:drv_p_tail
drv_p_tail:
    retf

leaf_n1:
    call far dword [cs:drv_n1_ptr]
    ret

drv_n1:
    inc byte [0x0484]
    cmp byte [0x0484], 2
    jne .keep
    mov di, 0x7777
.keep:
    jmp 0xF000:drv_n1_tail
drv_n1_tail:
    retf

leaf_n2:
    mov ax, 0x2222
    jmp 0xF000:leaf_n2_tail
leaf_n2_tail:
    ret 2

drv_p_ptr:
    dw drv_p, 0xF000
drv_n1_ptr:
    dw drv_n1, 0xF000

; ---------------------------------------------------------------------------
; Records: key byte, 11 bytes of payload, handler word at +0Ch.
; ---------------------------------------------------------------------------

macro NEARREC key, handler {
    db key
    rb 11
    dw handler
}

list_p:
    NEARREC KEY0, h_p0
    NEARREC KEY1, h_p1
    NEARREC KEY2, h_p2

list_n1:
    NEARREC KEY0, h_n10
    NEARREC KEY1, h_n11
    NEARREC KEY2, h_n12

list_n2:
    NEARREC KEY0, h_n20
    NEARREC KEY1, h_n21
    NEARREC KEY2, h_n22

h_p0:  inc byte [0x0401]
       ret
h_p1:  inc byte [0x0402]
       ret
h_p2:  inc byte [0x0403]
       ret
h_n10: inc byte [0x0404]
       ret
h_n11: inc byte [0x0405]
       ret
h_n12: inc byte [0x0406]
       ret
h_n20: inc byte [0x0407]
       ret
h_n21: inc byte [0x0408]
       ret
h_n22: inc byte [0x0409]
       ret

rb 65520-$
jmp start
rb 65535-$
db 0ffh
