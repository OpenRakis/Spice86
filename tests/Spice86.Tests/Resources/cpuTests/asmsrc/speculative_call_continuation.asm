; compile it with fasm
; Speculative call-continuation fixture: code after a call or int whose callee is
; known to return must be explored speculatively so the unobserved arm runs it in
; generated code instead of failing "Untested return from call".
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: JNZ not taken -> writes 0xDD, then calls f2 (returns)
;   0x01 = unobserved arm: calls f / f2 and writes markers
;
; Sections on the unobserved arm:
;   S1: call f, whose aligned return was observed before the branch (evidence first)
;   S3: call f2, whose aligned return is only observed after the branch (pending site)
; Never-taken sites (explored only - a never-delivered int lowers to an untested
; abort, so they cannot execute in generated code):
;   S4: int 60h to a handler the program installed by a direct IVT write (iret
;       evidence comes from the observed int 60h on the common path)
;   S5: mov ah,30h; int 21h to the emulator-installed DOS handler
;   S2: call g, which only ever returned by redirecting its stacked return address
;   S5': mov ah,4Ch; int 21h (program termination) - continuation must stay untested
;
; Sections added by the callee-return scan:
;   S6: call p on the executed unobserved path - p is never called anywhere, so no
;       return evidence exists; the scan proves it returns (balanced push bp frame,
;       a store through [bp-2] that does not touch the return address) and the
;       continuation is explored so the unobserved run reaches its marker.
;   T1: call unbal on a never-taken arm - an unbalanced pop refuses the proof,
;       the marker stays unexplored (golden only).
;   T2: call framebad on a never-taken arm - the write through [bp+2] inside its
;       push bp; mov bp,sp frame may hit the return address and refuses the
;       proof; the marker stays unexplored (golden only).
;
; Expected memory:
;   [0x0400] = 0x01
;   [0x0401] = 0xDD (discovery) or 0x42 (unobserved: S1 marker)
;   [0x0402] = 0x00 (discovery) or 0x33 (unobserved: S3 marker)
;   [0x0403]/[0x0404] = 0x00 (the S4/S5 markers are explored, never run - golden only)
;   [0x0405] = 0xAA (done marker, both runs)
;   [0x0406] = 0x00 (discovery) or 0x44 (unobserved: S6 marker)
;   [0x040B]/[0x040C] = 0x00 (the T1/T2 markers are never explored)

use16

start:
    mov byte [0x0400], 0x01

    ; S1 evidence: f returns aligned before the branch is ever explored.
    call f

    ; S2 evidence: g is only ever reached through g_redirect, where it bumps its
    ; stacked return address by the size of the skipped store, so the ret lands at
    ; gskip_label instead of after_gcall - a redirected return.
    call g
after_gcall:
    mov byte [0x0408], 0xEE
gskip_label:

    ; S4: install the program's own handler on free vector 60h by a direct IVT write,
    ; then call it once so its iret produces aligned evidence.
    xor ax, ax
    mov es, ax
    mov word [es:60h*4], handler60
    mov [es:60h*4+2], cs
    int 60h

    mov al, byte [0x0500]
    test al, al
    jnz unobserved_arm

    ; Discovery path: writes 0xDD, then observes f2's aligned return AFTER the branch,
    ; which drains the arm's pending call site.
    mov byte [0x0401], 0xDD
    call f2
    jmp done

unobserved_arm:
    call f                  ; S1: evidence already known
    mov byte [0x0401], 0x42
    call f2                 ; S3: pending until the discovery path's call f2 returns
    mov byte [0x0402], 0x33
    call p                  ; S6: never called - proven to return by the subgraph scan
    mov byte [0x0406], 0x44
    ; Never-taken branches keep the int sites and the negative twins in the graph
    ; without running them: a never-delivered int lowers to an untested abort, and
    ; code after a call without a known return is not explored, so every site needs
    ; its own jnz target.
    xor ax, ax
    jnz s4_arm
    jnz s5_arm
    jnz neg_g
    jnz neg_21
    jnz t1_arm
    jnz t2_arm
    jmp done

s4_arm:
    int 60h                 ; S4: program-installed handler, aligned iret evidence
    mov byte [0x0403], 0x66
    jmp done

s5_arm:
    mov ah, 30h             ; S5: emulator-installed handler, DOS get version
    int 21h
    mov byte [0x0404], 0x30
    jmp done

neg_g:
    call g                  ; S2: g has only redirected-return evidence - no continuation
    mov byte [0x0409], 0x55
    jmp done

neg_21:
    mov ah, 4Ch             ; S5': termination pattern - no continuation
    int 21h
    mov byte [0x040A], 0x55
    jmp done

t1_arm:
    call unbal              ; T1: unbalanced callee - no continuation
    mov byte [0x040B], 0x55
    jmp done

t2_arm:
    call framebad           ; T2: writes the return address - no continuation
    mov byte [0x040C], 0x55
    jmp done

f:
    mov al, 0x42
    ret

f2:
    ret

g:
    mov bp, sp
    add word [bp], gskip_label - after_gcall
    ret

p:
    push bp
    mov bp, sp
    sub sp, 4
    mov word [bp-2], 5      ; local frame store, below the return address
    mov sp, bp
    pop bp
    ret

unbal:
    pop cx                  ; pops the caller's return address - unbalanced
    ret

framebad:
    push bp
    mov bp, sp
    mov word [bp+2], 0      ; writes the stacked return address
    mov sp, bp
    pop bp
    ret

handler60:
    mov dx, 0666h
    iret

done:
    mov byte [0x0405], 0xAA
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
