; compile it with fasm
; Sections for register-only calling conventions: a leaf add called twice with different
; inputs (C1), a DOS-style status returned in CF and tested with jc / jnc (C2), a 32-bit
; product returned in DX:AX (C3), a far pointer returned in ES:BX (C4), callee-saved
; SI / DI (C5) and a dead CX the caller overwrites next to a BP the callee never touches
; (C6).
; DS = SS = 2000h, SP = 1000h. Results go to offsets 40h-51h (physical 20040h-20051h);
; the byte C4 reads sits at 2000:0100h and its decoy at 1000:0100h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; scratch bytes C4 reads back
    mov byte [100h], 5Ah
    mov ax, 1000h
    mov es, ax
    mov byte [es:100h], 66h
    mov ax, 2000h
    mov es, ax

; C1: add_ax_bx (inputs AX, BX, output AX) called twice with different inputs; before the
; second call each input register is written twice so only the last write may be read.
; Catches: an input read too early (before the caller's last write to it) or folded as a
; constant from the first call.
    mov ax, 3
    mov bx, 4
    call add_ax_bx
    mov [40h], ax
    mov ax, 8
    mov bx, 9
    mov ax, 10h
    mov bx, 20h
    call add_ax_bx
    mov [42h], ax

; C2: c2_status returns a DOS-style status in CF (CF set iff AX < 10h); the caller does
; jc / jnc and stores a different byte on each path; called once with each outcome.
; Catches: CF not seen as an output (the caller's flag test reads a stale CF).
    mov ax, 5
    call c2_status
    jc c2_first_set
    mov byte [44h], 0EEh
    jmp short c2_first_done
c2_first_set:
    mov byte [44h], 11h
c2_first_done:
    mov ax, 20h
    call c2_status
    jc c2_second_set
    mov byte [45h], 22h
    jmp short c2_second_done
c2_second_set:
    mov byte [45h], 0EEh
c2_second_done:

; C3: c3_mul returns the 32-bit product in DX:AX; the caller stores both halves.
; Catches: DX dropped from the outputs.
    mov ax, 8000h
    mov bx, 4
    call c3_mul
    mov [46h], ax
    mov [48h], dx

; C4: c4_farptr returns a far pointer in ES:BX to the scratch byte; the caller reads a
; byte through es:[bx]. ES points at the decoy segment before the call.
; Catches: ES dropped from the outputs.
    mov ax, 1000h
    mov es, ax
    call c4_farptr
    mov al, [es:bx]
    mov [4Ah], al
    mov ax, 2000h
    mov es, ax

; C5: c5_saved pushes si / di, uses both, pops di / si; the caller set SI / DI before the
; call and stores them after.
; Catches: SI / DI counted as outputs or as clobbered and not restored.
    mov si, 1111h
    mov di, 2222h
    call c5_saved
    mov [4Ch], si
    mov [4Eh], di

; C6: c6_dead changes CX that the caller never reads after the call (it writes CX again
; first), and never touches BP that the caller set before the call and stores after.
; Catches: the untouched register considered clobbered (kept in a stale local) or the dead
; CX treated as an output that must be kept.
    mov bp, 3333h
    mov cx, 4444h
    call c6_dead
    mov cx, 5555h
    mov [50h], bp

    hlt

add_ax_bx:
    add ax, bx
    ret

c2_status:
    cmp ax, 10h
    ret

c3_mul:
    mul bx
    ret

c4_farptr:
    mov bx, 2000h
    mov es, bx
    mov bx, 100h
    ret

c5_saved:
    push si
    push di
    mov si, 0AAAAh
    mov di, 0BBBBh
    pop di
    pop si
    ret

c6_dead:
    mov cx, 9999h
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
