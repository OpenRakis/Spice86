; compile it with fasm
; Four callees for the merged stack save/restore/print forms: S1 restores registers
; the caller observes, S2 restores registers the caller never reads, S3 puts a
; "push sp" inside the entry run, S4 has a second exit path with a statement
; between the pops. Every callee calls leaf: ret right after its pushes so a
; call sits between each push and its pop. S5 leaks a saved slot's address to a
; routine an int reaches, which writes through it, so the saved-slots
; relaxation must refuse it.
; DS = SS = 2000h, SP = 1000h.
; Results go to offsets 40h-57h (physical 20040h-20057h).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; S1: callee_a restores SI and DI; the caller stores them after the call.
    mov si, 0A0Ah
    mov di, 0B0Bh
    call callee_a
    mov [40h], si
    mov [42h], di

; S2: callee_b has the same push/pop shape; the caller overwrites SI and DI
; right after the call, before any read, so the restores are not observed.
    mov si, 0C0Ch
    mov di, 0D0Dh
    call callee_b
    mov si, 0E1E1h
    mov di, 0F1F1h
    mov [44h], si
    mov [46h], di

; S3: callee_c pushes bx then sp and stores the pushed word: the SP value
; right after "push bx" (SP = 0FFCh inside the callee).
    call callee_c

; S4: callee_d has two exits: path 1 (cx = 0) pops di, si and returns; path 2
; (cx != 0) pops di, sets ax = 7, pops si and returns.
    mov cx, 0
    mov ax, 0FFFFh
    mov si, 0A1A1h
    mov di, 0B1B1h
    call callee_d
    mov [4Ah], ax
    mov [4Ch], si
    mov [4Eh], di
    mov cx, 1
    mov si, 0A2A2h
    mov di, 0B2B2h
    call callee_d
    mov [50h], ax
    mov [52h], si
    mov [54h], di

; S5: callee_e pushes bp and si, builds the frame pointer, then leaks the saved
; si slot's address in bx to int 60h, whose handler writes through it. The pop
; has to observe that write: under the saved-slots relaxation the method must
; refuse because the stack address escapes.
    xor ax, ax
    mov es, ax
    mov word [es:180h], int60_handler
    mov ax, cs
    mov word [es:182h], ax
    mov si, 0C3C3h
    call callee_e
    mov [56h], si

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

callee_a:
    push si
    push di
    call leaf
    mov si, 1111h
    mov di, 2222h
    pop di
    pop si
    ret

callee_b:
    push si
    push di
    call leaf
    mov si, 3333h
    mov di, 4444h
    pop di
    pop si
    ret

callee_c:
    push bx
    push sp
    call leaf
    pop ax
    mov [48h], ax
    pop bx
    ret

callee_d:
    push si
    push di
    call leaf
    mov si, 5555h
    mov di, 6666h
    test cx, cx
    jnz d_path2
    pop di
    pop si
    ret
d_path2:
    pop di
    mov ax, 7
    pop si
    ret

callee_e:
    push bp
    mov bp, sp
    push si
    lea bx, [bp-2]
    int 60h
    mov ax, [bp-2]
    pop si
    pop bp
    ret

int60_handler:
    mov word [ss:bx], 5A5Ah
    iret

leaf:
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
