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
;   N1 (negative): `mov di, ax` on the speculative path before `call di` kills
;       the known register, so the site is refused (golden-only).
;   N2 (negative): a pointer cell holding 0x0000 is evaluated and refused
;       (golden-only).
;
; Selector byte at DS:0500h: 0 = discovery run (call arms never taken),
; 1 = unobserved run (every arm taken). DS = CS (flat bin).
;
; Result bytes at DS:0400h..0406h:
;   [0400] = 02 when the program completed
;   [0401] = cb_a  invocations (warmup + S1 arm calls)
;   [0402] = cb_b  invocations (warmup + S1 arm calls)
;   [0403] = cb_s2 invocations
;   [0404] = cb3a  invocations (warmup + S3 arm calls before the rewrite)
;   [0405] = cb3b  invocations (warmup + the post-rewrite arm entry)
;   [0406] = cb_s4 invocations

use16

start:
    mov byte [0x0400], 0x00
    mov byte [0x0401], 0x00
    mov byte [0x0402], 0x00
    mov byte [0x0403], 0x00
    mov byte [0x0404], 0x00
    mov byte [0x0405], 0x00
    mov byte [0x0406], 0x00

    ; Warmup: one observed call per callback builds the return evidence the
    ; speculative call sites need for their continuations.
    call cb_a
    call cb_b
    call cb_s2
    call cb3a
    call cb3b
    call cb_s4
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

rb 65520-$
jmp start
rb 65535-$
db 0ffh
