; compile it with fasm
; Sections for stack parameters and frames on SS = 2000h: C style caller cleanup (F1),
; Pascal style near ret 4 with a slot pushed before the call read back after it (F2),
; Pascal style far retf 4 to another segment value (F3), a frame with locals (F4), a
; frame read through SP without BP (F5), a callee that writes its parameter slot (F6),
; nested frames (F7), a callee whose only memory read has an explicit ss: override,
; called under two DS values (F8), and callees whose frames must keep BP changed:
; inc bp between mov bp,sp and mov sp,bp (F9), an SS write inside the frame (F10)
; and a frame store over the saved BP (F11). A callee whose [bp-2] store lands on
; the slot its pop si reads (F12, F13 at the segment wrap) does not preserve SI,
; and one whose BP copy moved after inc bp reads a byte of the saved SI (F14).
; DS = SS = 2000h, SP = 1000h. Results go to offsets 40h-63h (physical 20040h-20063h).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    mov word [80h], 1234h

; F1: push a; push b; call f; add sp,4. f reads [bp+4] and [bp+6], returns AX = a + b.
; The pushed register is overwritten between the pushes and the call.
; Catches: a parameter taken from the register instead of its stack slot (read too
; early), a push considered dead.
    mov ax, 1111h
    push ax
    mov ax, 2222h
    push ax
    mov ax, 0FFFFh
    call f1_callee
    add sp, 4
    mov [40h], ax

; F2: push a; push b; call g with g ending in ret 4. A word pushed before the two
; parameters is popped and stored after the call.
; Catches: wrong stack cleanup (the oracle checks SP through the final SP = 1000h and a
; value read from a slot pushed before F2).
    mov ax, 5A5Ah
    push ax
    mov ax, 1
    push ax
    mov ax, 2
    push ax
    call f2_callee
    mov [44h], ax
    pop ax
    mov [42h], ax

; F3: push a; push b; call far to 0F000h:f3_callee which reads [bp+6] / [bp+8] and ends
; in retf 4.
; Catches: far frame offsets computed like near ones.
    mov ax, 100h
    push ax
    mov ax, 20h
    push ax
    call 0F000h:f3_callee
    mov [46h], ax

; F4: f4 builds push bp; mov bp,sp; sub sp,4, writes [bp-2] / [bp-4], reads them back
; mixed with the parameter at [bp+4], ends mov sp,bp; pop bp; ret; the caller cleans.
; Catches: a local slot aliased with a parameter slot.
    mov ax, 3
    push ax
    call f4_callee
    add sp, 2
    mov [48h], ax

; F5: f5 reads its parameter through SP with no BP frame: mov bx,sp; mov ax,[ss:bx+2].
; Catches: frame recognition that only follows BP.
    push 7777h
    call f5_callee
    add sp, 2
    mov [4Ah], ax

; F6: f6 writes one of its parameter slots (mov word [bp+4], X); the caller reads that
; slot back after the call, before its add sp.
; Catches: parameter slots treated as read-only inputs.
    mov ax, 9999h
    push ax
    call f6_callee
    mov bx, sp
    mov ax, [ss:bx]
    mov [4Ch], ax
    add sp, 2

; F7: f7_outer (frame) calls f7_inner (frame) passing a value computed from its own
; parameter; f7_inner reads [bp+4]; f7_outer reads its own [bp+4] again after the call.
; Catches: BP-relative slots resolved against the wrong frame.
    mov ax, 7
    push ax
    call f7_outer
    add sp, 2
    mov [4Eh], ax

; F8: f8 reads its parameter only through an explicit ss: override; it is called once
; under DS = 2000h and once under DS = 3000h and must return the same value both times.
; Catches: a segment override counting the default segment (DS) as a read.
    mov ax, 2000h
    mov ds, ax
    push 1234h
    call f8_callee
    add sp, 2
    mov [50h], ax
    mov ax, 3000h
    mov ds, ax
    push 1234h
    call f8_callee
    add sp, 2
    mov bx, 2000h
    mov ds, bx
    mov [52h], ax

; F9: inc bp between mov bp,sp and mov sp,bp; the caller sees BP = 0FFDh and its SP
; is repaired by the add sp,1 after the copy.
; Catches: a BP copy trusted although a statement may have written BP since it.
    mov bp, 4321h
    call f9_callee
    mov [54h], bp

; F10: SS changed inside the frame (restored before the epilogue); BP again leaves
; the call changed at 0FFDh.
; Catches: an SS write leaving the BP copy trusted.
    mov bp, 4321h
    call f10_callee
    mov [56h], bp

; F11: mov [bp+0],ax overwrites the saved BP; the pop restores 7777h, not the
; caller's 4321h.
; Catches: a BP-based write that overlaps a slot treated as disjoint from it.
    mov bp, 4321h
    call f11_callee
    mov [58h], bp

; F12: negative_slot stores through [bp-2], which is exactly the slot its pop si
; reads, so the callee does NOT preserve SI (it returns 7777h). Several separate
; stores read the loaded SI before and after the call, so the caller can keep a
; local only when the callee really preserves SI.
; Catches: a BP-relative store whose wrapped displacement 0FFFEh is treated as a
; large positive offset and never matched against the saved-SI slot at -2, so the
; caller's local keeps the pre-call value.
    mov si, [80h]
    mov [84h], si
    mov [86h], si
    mov [88h], si
    mov [8Ah], si
    call negative_slot
    mov [5Ah], si
    mov [5Ch], si
    mov [8Ch], si

; F13: the same [bp-2] overwrite at the wrap boundary: with SP = 4 the saved SI sits
; at physical offset 0FFFEh; the callee again does not preserve SI.
    mov sp, 4
    mov si, [80h]
    mov [84h], si
    mov [86h], si
    mov [88h], si
    mov [8Ah], si
    call negative_slot
    mov sp, 1000h
    mov [5Eh], si
    mov [60h], si
    mov [8Eh], si
; the wrap call's saved BP and return IP landed at offsets 0-3: clear them so the
; result region does not pin a fixed code address
    mov word [0], 0
    mov word [2], 0

; F14: changed_bp_slot pushes di between mov bp,sp and inc bp so the SP version
; changes and the BP copy cannot be folded into the later SP expression; after
; inc bp the copy points above the saved-SI slot, so pop si reads the high byte
; of 1234h as the low byte and the callee does NOT preserve SI either. A stale BP
; delta would source the pop from the original push si and report SI unchanged.
    mov si, [80h]
    mov [84h], si
    mov [86h], si
    mov [88h], si
    mov [8Ah], si
    call changed_bp_slot
    mov [62h], si
    mov [90h], si
    mov byte [63h], 0

    hlt

f1_callee:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    add ax, [bp+6]
    pop bp
    ret

f2_callee:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    add ax, [bp+6]
    pop bp
    ret 4

f3_callee:
    push bp
    mov bp, sp
    mov ax, [bp+6]
    add ax, [bp+8]
    pop bp
    retf 4

f4_callee:
    push bp
    mov bp, sp
    sub sp, 4
    mov word [bp-2], 10h
    mov word [bp-4], 20h
    mov ax, [bp-2]
    add ax, [bp-4]
    add ax, [bp+4]
    mov sp, bp
    pop bp
    ret

f5_callee:
    mov bx, sp
    mov ax, [ss:bx+2]
    ret

f6_callee:
    push bp
    mov bp, sp
    mov word [bp+4], 4321h
    pop bp
    ret

f7_outer:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    add ax, 5
    push ax
    call f7_inner
    add sp, 2
    add ax, [bp+4]
    mov sp, bp
    pop bp
    ret

f7_inner:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    mov sp, bp
    pop bp
    ret

f8_callee:
    mov bx, sp
    mov ax, [ss:bx+2]
    ret

f9_callee:
    push bp
    mov bp, sp
    inc bp
    mov sp, bp
    add sp, 1
    ret

f10_callee:
    push bp
    mov bp, sp
    mov cx, ss
    mov ax, 3000h
    mov ss, ax
    mov ss, cx
    inc bp
    mov sp, bp
    add sp, 1
    ret

f11_callee:
    push bp
    mov bp, sp
    mov ax, 7777h
    mov [bp+0], ax
    mov sp, bp
    pop bp
    ret

negative_slot:
    push bp
    mov bp, sp
    push si
    mov word [bp-2], 7777h
    pop si
    pop bp
    ret

changed_bp_slot:
    push si
    mov bp, sp
    push di
    inc bp
    mov sp, bp
    pop si
    dec sp
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
