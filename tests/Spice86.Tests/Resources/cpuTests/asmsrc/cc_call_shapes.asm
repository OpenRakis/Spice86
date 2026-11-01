; compile it with fasm
; Sections for call graph shapes: one function called from two sites that read different
; outputs (S1), a function with two ret paths that write different registers (S2), a
; recursive sum 1..n (S3), a tail call through jmp (S4), a callee that pops its return
; address and jumps to a fixed continuation (S5) and an int 61h program handler (S6).
; DS = SS = 2000h, SP = 1000h. Results go to offsets 40h-59h (physical 20040h-20059h).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; S1: s1_fn writes AX and DX; site 1 stores AX, site 2 stores DX.
; Catches: an output dropped because one call site does not read it.
    call s1_fn
    mov [40h], ax
    xor ax, ax
    xor dx, dx
    call s1_fn
    mov [42h], dx

; S2: s2_fn has two ret paths that write different registers (path A writes AX, path B
; writes BX); both paths are executed, the caller stores AX and BX after each call.
; Catches: outputs computed from one return path only.
    mov cx, 0
    call s2_fn
    mov [44h], ax
    mov cx, 1
    call s2_fn
    mov [46h], bx

; S3: s3_fn is recursive: a sum 1..n in AX through a register argument CX, n = 4.
; Catches: a recursive call summarized before its own summary is known (a wrong fixed
; point).
    mov cx, 4
    call s3_fn
    mov [48h], ax

; S4: s4_t1 ends with jmp s4_t2; s4_t2 writes AX and returns to s4_t1's caller.
; Catches: t1 considered as not writing AX.
    call s4_t1
    mov [4Ah], ax

; S5: s5_callee never returns normally: it pops its return address and jumps to a fixed
; continuation label in the caller; the store under the call is unreachable.
; Catches: a frame / return analysis that assumes every callee returns to the next
; instruction.
    call s5_callee
    mov word [4Ch], 0DEADh
s5_cont:
    mov word [4Ch], 1234h

; S6: int 61h to a program handler that reads AX and returns AX = AX * 2 + 1, leaving the
; other registers unchanged; called twice with different AX; the caller also stores CX
; set before each int. AX is written twice before the first int so only the last write
; may be read.
; Catches: the program handler treated as using / killing every register (a lost
; precision bug, not a wrong result) and, more important, the AX output dropped or an
; input read too early.
    xor ax, ax
    mov es, ax
    mov word [es:61h*4], s6_handler
    mov [es:61h*4+2], cs
    mov ax, 2000h
    mov es, ax
    mov ax, 9
    mov ax, 3
    mov cx, 7777h
    int 61h
    mov [4Eh], ax
    mov [50h], cx
    mov ax, 20h
    mov cx, 8888h
    int 61h
    mov [52h], ax
    mov [54h], cx

; S7: int 62h to a program handler that reads BX (pointing at a word in the scratch area)
; and writes only AX; the caller sets BX to a constant right before the int, stores AX,
; then overwrites BX so that the int is the only reader of the 5Ch write (a method's exit
; reads every register, so the write would otherwise stay live whatever the int reads).
; Both remaining vectors are installed in the same es = 0 setup.
; Catches: the handler's reads dropped (the mov bx,5Ch before the int becomes a dead
; write).
    mov word [5Ch], 0CAFEh
    xor ax, ax
    mov es, ax
    mov word [es:62h*4], s7_handler
    mov [es:62h*4+2], cs
    mov word [es:63h*4], s8_handler
    mov [es:63h*4+2], cs
    mov ax, 2000h
    mov es, ax
    mov bx, 5Ch
    int 62h
    mov [56h], ax
    mov bx, 5678h

; S8: the caller clears CF (cmp bx,0 leaves CF = 0 with a computable relation) right
; before int 63h; the handler sets the stacked CF; the caller stores a different byte on
; each path of the following jc.
; Catches: the flags not counted as may-writes (the jc reads the relation of the setter
; before the int instead of the flag the handler patched).
    cmp bx, 0
    int 63h
    jc s8_set
    mov byte [58h], 0
    jmp s8_done
s8_set:
    mov byte [58h], 1
s8_done:

    hlt

s1_fn:
    mov ax, 1111h
    mov dx, 2222h
    ret

s2_fn:
    test cx, cx
    jz s2_path_a
    mov bx, 0BBBBh
    ret
s2_path_a:
    mov ax, 0AAAAh
    ret

s3_fn:
    test cx, cx
    jz s3_done
    push cx
    dec cx
    call s3_fn
    pop cx
    add ax, cx
    ret
s3_done:
    xor ax, ax
    ret

s4_t1:
    jmp s4_t2

s4_t2:
    mov ax, 3456h
    ret

s5_callee:
    pop ax
    jmp s5_cont

s6_handler:
    shl ax, 1
    inc ax
    iret

; s7_handler (vector 62h): AX = [bx].
s7_handler:
    mov ax, [bx]
    iret

; s8_handler (vector 63h): set CF in the stacked flags (push bp / mov bp,sp put the flags
; at [bp+6]).
s8_handler:
    push bp
    mov bp, sp
    or word [bp+6], 1
    pop bp
    iret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
