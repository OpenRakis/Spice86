; compile with fasm
; SP tracking through joins and loops after calls and event checks.
; DS = SS = 2000h. Results (words): 40h, 42h = section 1 (both arms),
; 44h = section 2, 46h = section 3, 48h = section 4, 4Ah = section 5,
; 4Ch, 4Eh = section 6 (result, saved slot).
use16

start:
    cli
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
; section 1: if/else whose arms each call a leaf, joined before the ret
    mov ax, 5
    call s1_join
    mov [40h], ax
    mov ax, 0FF00h
    call s1_join
    mov [42h], ax
; section 2: a loop containing a call
    mov ax, 3
    call s2_loop
    mov [44h], ax
; section 3: a loop whose back edge pushes without a pop; SP after it is
; unknown, so the frame read through BP must use the real SP
    push 1234h
    call s3_push_loop
    add sp, 2
    mov [46h], ax
; section 4: a callee not proved to return pops its argument (ret 2); the
; caller's SP after the call is not its offset before the call
    call s4_caller
    mov [48h], ax
; section 5: a callee changes BP; the caller's BP copy of SP is gone, so a
; store through BP may overwrite the slot pushed after the call
    call s5_caller
    mov [4Ah], ax
; section 6: a callee saves BX it never reads otherwise; the caller's BX value
; still reaches the saved slot, read back below SP after the return
    mov ax, 3
    call s6_outer
    mov [4Ch], ax
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

s1_join:
    test ax, 8000h
    jnz s1_negative
    call leaf_inc
    jmp s1_joined
s1_negative:
    call leaf_dec
s1_joined:
    add ax, ax
    ret

s2_loop:
    mov cx, ax
    xor ax, ax
s2_next:
    call leaf_inc
    loop s2_next
    ret

leaf_inc:
    inc ax
    ret

leaf_dec:
    dec ax
    ret

s3_push_loop:
    mov cx, 2
s3_next:
    push cx
    loop s3_next
    mov bp, sp
    mov ax, [bp+6]
    add sp, 4
    ret

s4_caller:
    push 5678h
    push 0AAAAh
    call s4_unproved
    mov bp, sp
    mov ax, [bp+0]
    add sp, 2
    ret

s4_unproved:
    mov cx, 2
s4_next:
    push cx
    loop s4_next
    add sp, 4
    ret 2

s5_caller:
    push bp
    mov bp, sp
    call s5_clobber
    push 0BBBBh
    mov word [bp+0], 0DDDDh
    pop ax
    pop bp
    ret

s5_clobber:
    mov bp, sp
    ret

s6_outer:
    mov bx, 7777h
    call s6_saver
    mov bx, 1
    mov bp, sp
    mov dx, [bp-4]
    mov [4Eh], dx
    ret

s6_saver:
    push bx
    mov bx, ax
    add bx, bx
    mov ax, bx
    pop bx
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
