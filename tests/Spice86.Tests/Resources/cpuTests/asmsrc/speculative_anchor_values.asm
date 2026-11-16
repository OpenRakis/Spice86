; compile it with fasm
; Anchor-register discovery fixture: a speculative path carries the register
; values the CPU held when its anchor last executed, so an indirect call on the
; untaken arm resolves against live state instead of staying opaque. Each
; dispatcher's call arm is never taken during discovery; the arm instructions
; are decoded speculatively from the anchor's successors.
;
;   S1: `call di`, callback in DI (the printf shape). The dispatcher runs three
;       times, DI changing between the second and third invocation: the first
;       execution registers the site, each later anchor execution proposes the
;       register's current value, so both callbacks are discovered.
;   S2: `call word [const]`, the cell initialized in the image; the anchor's
;       evaluation reads it and proposes the stored target.
;   S3: same shape but the program rewrites the cell AFTER the site was
;       evaluated and its watched cell armed. Only the watched cell proposes
;       the new target: no anchor executes again before the unobserved run
;       enters the call arm directly, past the anchor.
;   S4: `call word [cs:bx]`, BX set on the shared path; the evaluation reads the
;       cell at DS:BX.
;   S5: `call di` sits past `call s5_leaf` on the call arm. The leaf writes AX
;       only, so the register knowledge the anchor carried survives the
;       continuation and the site registers like S1.
;   S6: same shape but s6_leaf writes DI after saving it (`push di` /
;       `pop di`): the restored register keeps the continuation knowledge and
;       the site registers too.
;   S7: same shape but s7_leaf crosses an indirect call (`call word
;       [cs:s7_drv]`) whose observed target writes AX only: the write scan
;       resolves it through the observed edge and the site registers too.
;   N1 (negative): `mov di, ax` on the speculative path before `call di` kills
;       the known register, so the site is refused (golden-only).
;   N2 (negative): a pointer cell holding 0x0000 is evaluated and refused
;       (golden-only).
;   N3 (negative): same shape as S5 but n3_leaf writes DI, so the continuation
;       knowledge loses it and the site is refused (golden-only).
;   N4 (negative): same shape but n4_leaf holds an indirect call on its own
;       dead arm, so the callee write scan is unknown and everything is
;       cleared (golden-only).
;   N5 (negative): same shape as S6 but n5_leaf pops DI from a different stack
;       slot (`push di` / `push ax` / `pop di` / `pop ax`), so the registers
;       cross the call clobbered and the site is refused (golden-only).
;   N6 (negative): same shape as S7 but the observed driver target writes DI,
;       so the register crosses the indirect call clobbered and the site is
;       refused (golden-only).
;   S8: `call word [cs:s8_ptr]` sits past `call s8_leaf`. The leaf's write
;       scan is unknown, so everything but CS is cleared; a CS cell site only
;       needs CS, which every return keeps, and the site registers too.
;   N7 (negative): `jmp word [ds:n7_ptr]` past a leaf that writes DS, so the
;       site's segment is unknown and the site is refused (golden-only).
;
; Selector byte at DS:0500h: 0 = discovery run (call arms never taken),
; 1 = unobserved run (every arm taken). DS = CS (flat bin).
;
; Result bytes at DS:0400h..0407h:
;   [0400] = 02 when the program completed
;   [0401] = cb_a  invocations (warmup + S1 arm calls)
;   [0402] = cb_b  invocations (warmup + S1 arm calls)
;   [0403] = cb_s2 invocations
;   [0404] = cb3a  invocations (warmup + S3 arm calls before the rewrite)
;   [0405] = cb3b  invocations (warmup + the post-rewrite arm entry)
;   [0406] = cb_s4 invocations
;   [0407] = cb_s5 invocations (warmup + S5 arm calls)
;   [0408] = cb_s6 invocations (warmup + S6 arm calls)
;   [0409] = cb_s7 invocations (warmup + S7 arm calls)
;   [040A] = cb_s8 invocations (warmup + S8 arm calls)

use16

start:
    mov byte [0x0400], 0x00
    mov byte [0x0401], 0x00
    mov byte [0x0402], 0x00
    mov byte [0x0403], 0x00
    mov byte [0x0404], 0x00
    mov byte [0x0405], 0x00
    mov byte [0x0406], 0x00
    mov byte [0x0407], 0x00
    mov byte [0x0408], 0x00
    mov byte [0x0409], 0x00
    mov byte [0x040A], 0x00

    ; Warmup: one observed call per callback builds the return evidence the
    ; speculative call sites need for their continuations.
    call cb_a
    call cb_b
    call cb_s2
    call cb3a
    call cb3b
    call cb_s4
    call cb_s5
    call cb_s6
    call cb_s7
    call s5_leaf
    call s6_leaf
    call s7_leaf
    call n3_leaf
    call n4_leaf
    call n5_leaf
    call n6_leaf
    call cb_s8
    call s8_leaf
    call n7_leaf
; --- S1: callback in DI, `call di` on the unexecuted arm ---
    mov di, cb_a
    call s1_dispatch
    call s1_dispatch                  ; first evaluation: proposes cb_a
    mov di, cb_b
    call s1_dispatch                  ; second evaluation: proposes cb_b

; --- S2: `call word [cs:s2_ptr]`, cell initialized in the image ---
    call s2_dispatch                  ; registers the site
    call s2_dispatch                  ; evaluation reads the cell: proposes cb_s2

; --- S3: pointer cell rewritten after the watch is armed ---
    call s3_dispatch                  ; registers the site
    call s3_dispatch                  ; evaluation: proposes cb3a, arms the watch
    mov word [cs:s3_ptr], cb3b           ; the watched cell proposes cb3b
    cmp byte [0x0500], 0
    je s3_done
    call s3_dispatch                  ; unobserved only: re-enter past the rewrite; the arm runs
s3_done:

; --- S4: `call word [cs:bx]`, BX set on the shared path ---
    mov bx, s4_ptr
    call s4_dispatch                  ; registers the site
    call s4_dispatch                  ; evaluation reads CS:BX: proposes cb_s4

; --- N1: DI written on the speculative path -> site refused ---
    call s1n_dispatch
    call s1n_dispatch                  ; second anchor execution evaluates the refused site

; --- N2: cell holding 0 -> candidate refused ---
    call s2n_dispatch
    call s2n_dispatch

; --- S5: `call di` past `call s5_leaf` (writes AX only) ---
    mov di, cb_s5
    call s5_dispatch                  ; registers the site past the leaf call
    call s5_dispatch                  ; evaluation proposes cb_s5

; --- N3: leaf writes DI -> continuation knowledge loses it, site refused ---
    mov di, cb_s5
    call n3_dispatch
    call n3_dispatch

; --- N4: leaf scan unknown -> everything cleared, site refused ---
    mov di, cb_s5
    call n4_dispatch
    call n4_dispatch

; --- S6: leaf saves and restores DI -> continuation knowledge survives ---
    mov di, cb_s6
    call s6_dispatch                  ; registers the site past the leaf call
    call s6_dispatch                  ; evaluation proposes cb_s6

; --- N5: leaf pops DI from a different slot -> site refused ---
    mov di, cb_s5
    call n5_dispatch
    call n5_dispatch

; --- S7: leaf crosses an indirect call through an observed target ---
    mov di, cb_s7
    call s7_dispatch                  ; registers the site past the leaf call
    call s7_dispatch                  ; evaluation proposes cb_s7

; --- N6: observed driver target writes DI -> site refused ---
    mov di, cb_s5
    call n6_dispatch
    call n6_dispatch

; --- S8: CS cell site past a leaf whose write scan is unknown ---
    call s8_dispatch                  ; registers the site past the leaf call
    call s8_dispatch                  ; evaluation proposes cb_s8

; --- N7: leaf writes DS -> the DS cell site is refused ---
    call n7_dispatch
    call n7_dispatch

    mov byte [0x0400], 0x02
    hlt

s1_dispatch:
    cmp byte [0x0500], 0
    jne s1_call
    ret
s1_call:
    call di
    ret

s2_dispatch:
    cmp byte [0x0500], 0
    jne s2_call
    ret
s2_call:
    call word [cs:s2_ptr]
    ret
s2_ptr:
    dw cb_s2

s3_dispatch:
    cmp byte [0x0500], 0
    jne s3_call
    ret
s3_call:
    call word [cs:s3_ptr]
    ret
s3_ptr:
    dw cb3a

s4_dispatch:
    cmp byte [0x0500], 0
    jne s4_call
    ret
s4_call:
    call word [cs:bx]
    ret
s4_ptr:
    dw cb_s4

s1n_dispatch:
    cmp byte [0x0500], 0xFF
    je s1n_dead
    ret
s1n_dead:
    mov di, ax
    call di
    ret

s2n_dispatch:
    cmp byte [0x0500], 0xFF
    je s2n_dead
    ret
s2n_dead:
    call word [cs:zero_ptr]
    ret
zero_ptr:
    dw 0

s5_dispatch:
    cmp byte [0x0500], 0
    jne s5_call
    ret
s5_call:
    call s5_leaf
    call di
    ret
s5_leaf:
    mov ax, 0x1234
    ret

n3_dispatch:
    cmp byte [0x0500], 0xFF
    je n3_dead
    ret
n3_dead:
    call n3_leaf
    call di
    ret
n3_leaf:
    mov di, 0x1234
    ret

n4_dispatch:
    cmp byte [0x0500], 0xFF
    je n4_dead
    ret
n4_dead:
    call n4_leaf
    call di
    ret
n4_leaf:
    cmp byte [0x0500], 0xFF
    je n4_indirect
    ret
n4_indirect:
    call si
    ret

cb_a:
    inc byte [0x0401]
    ret
cb_b:
    inc byte [0x0402]
    ret
cb_s2:
    inc byte [0x0403]
    ret
cb3a:
    inc byte [0x0404]
    ret
cb3b:
    inc byte [0x0405]
    ret
cb_s4:
    inc byte [0x0406]
    ret
cb_s5:
    inc byte [0x0407]
    ret

s6_dispatch:
    cmp byte [0x0500], 0
    jne s6_call
    ret
s6_call:
    call s6_leaf
    call di
    ret
s6_leaf:
    push di
    mov di, 0x1234
    mov ax, di
    pop di
    ret

n5_dispatch:
    cmp byte [0x0500], 0xFF
    je n5_dead
    ret
n5_dead:
    call n5_leaf
    call di
    ret
n5_leaf:
    push di
    push ax
    mov di, 0x1234
    pop di
    pop ax
    ret

cb_s6:
    inc byte [0x0408]
    ret

s7_dispatch:
    cmp byte [0x0500], 0
    jne s7_call
    ret
s7_call:
    call s7_leaf
    call di
    ret
s7_leaf:
    call word [cs:s7_drv]
    ret
s7_drv:
    dw s7_driver
s7_driver:
    mov ax, 0x1234
    ret

n6_dispatch:
    cmp byte [0x0500], 0xFF
    je n6_dead
    ret
n6_dead:
    call n6_leaf
    call di
    ret
n6_leaf:
    call word [cs:n6_drv]
    ret
n6_drv:
    dw n6_driver
n6_driver:
    mov di, 0x1234
    ret

cb_s7:
    inc byte [0x0409]
    ret

s8_dispatch:
    cmp byte [0x0500], 0
    jne s8_call
    ret
s8_call:
    call s8_leaf
    call word [cs:s8_ptr]
    ret
s8_ptr:
    dw cb_s8
s8_leaf:
    cmp byte [0x0500], 0xFF
    je s8_indirect
    ret
s8_indirect:
    call si
    ret

n7_dispatch:
    cmp byte [0x0500], 0xFF
    je n7_dead
    ret
n7_dead:
    call n7_leaf
    jmp word [ds:n7_ptr]
n7_ptr:
    dw cb_n7
n7_leaf:
    mov ax, ds
    mov ds, ax
    ret

cb_s8:
    inc byte [0x040A]
    ret
cb_n7:
    inc byte [0x040B]
    ret

rb 65520-$
jmp start
rb 65535-$
db 0ffh
