; compile it with fasm
; convention_landing_outputs: an entry reached by a non-call way in (a tail jump
; from another method) outputs only what the landings of its returns read.
; S1: s1_callee is reached by a call and by s1_tramp's tail jump; every landing
;    reads AX only, so AX is its single output. BX and CX writes are dead.
; S2: s2_callee is reached the same way; one call site reads AX, another reads
;    BX, so its outputs are the union of both.
; S3: s3_m tail-jumps to s3_c, so s3_c's ret lands on the continuation of the
;    call to s3_m: that landing reads BX, and a direct call site reads AX, so
;    BX and AX stay outputs of s3_c even though s3_c has an unknown way in.
; S4: s4_n tail-jumps to s4_c from inside s4_m, so s4_c's ret lands on the
;    continuation of s4_m's call to s4_n; s4_m then returns to the caller,
;    which reads BX: the landing demand iterates through s4_m's own return
;    exits, so BX stays an output of s4_c (two call levels).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h

    ; S1
    mov bx, 0DEADh
    mov cx, 0BEEFh
    call s1_callee
    mov [0200h], ax
    mov bx, 0DEADh
    mov cx, 0BEEFh
    call s1_tramp
    mov [0202h], ax

    ; S2
    call s2_callee
    mov [0204h], ax
    call s2_callee
    mov [0206h], bx
    call s2_tramp
    mov [0208h], ax

    ; S3
    call s3_m
    mov [020Ah], bx
    call s3_c
    mov [020Ch], ax

    ; S4
    mov bx, 0
    call s4_m
    mov [020Eh], bx

    hlt

s1_callee:
    mov ax, 0111h
    mov bx, 0222h
    mov cx, 0333h
    ret

s1_tramp:
    jmp s1_callee

s2_callee:
    mov ax, 0AAA0h
    mov bx, 0BBB0h
    ret

s2_tramp:
    jmp s2_callee

s3_m:
    mov si, 5
    jmp s3_c

s3_c:
    mov ax, 0CCC0h
    mov bx, 0DDD0h
    ret

s4_m:
    call s4_n
    ret

s4_n:
    jmp s4_c

s4_c:
    mov bx, 0EEE0h
    ret

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
