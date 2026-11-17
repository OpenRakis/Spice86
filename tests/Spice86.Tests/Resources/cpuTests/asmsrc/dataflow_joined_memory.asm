; compile it with fasm
; Joined RAM accesses: two word loads or stores at adjacent offsets should merge into one
; dword access ONLY when the pair is proven - same known segment, constant adjacent offsets
; (low <= FFFCh, high == low+2) or the same bounded index register at d and d+2 whose interval
; keeps the widened access inside the segment, no wrap, all in known RAM, no observer between.
; DS = SS = 2000h, ES = 2100h, SP = 1000h. Pass p writes its results at offset 80h * p
; (physical 20000h + 80h * p). Initial RAM (fixture-side ConfigureMachine):
;   DS:0800..0803 = 89ABCDEFh   DS:0804..0807 = 01234567h
;   ES:FFFE = A55Ah             ES:0000 = CAFEh
; Each independent section ends with `cmp di,di` to kill all status flags.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:64h*4], observe_iret
    mov [es:64h*4+2], cs
    mov ax, 2100h
    mov es, ax
    cld
    xor bx, bx
    mov si, inputs
    mov bp, 4

pass_loop:
; L1: adjacent constant word loads + add/adc -> joinable dword read
    mov ax, [0800h]
    mov dx, [0802h]
    mov cx, [cs:si]
    mov di, [cs:si+2]
    add ax, cx
    adc dx, di
    mov [bx+00h], ax        ;; CDEF + x
    mov [bx+02h], dx        ;; 89AB + y + carry
    cmp di, di

; L2: second adjacent pair + add/adc
    mov ax, [0804h]
    mov dx, [0806h]
    add ax, cx
    adc dx, di
    mov [bx+04h], ax        ;; 4567 + x
    mov [bx+06h], dx        ;; 0123 + y + carry
    cmp di, di

; S1: adjacent word stores (bx-relative) -> dynamic offset, stays words
    mov ax, [0800h]
    mov dx, [0802h]
    mov [bx+08h], ax
    mov [bx+0Ah], dx        ;; dword bytes = 89ABCDEF
    cmp di, di

; S2: absolute word stores (constant offsets) -> joinable dword store
    mov ax, [0800h]
    mov dx, [0802h]
    mov [0044h], ax
    mov [0046h], dx         ;; dword at 44h = 89ABCDEF
    cmp di, di

; D1: dynamic-offset pair feeding add/adc - unknown offsets must stay words
    push di
    mov di, 0800h
    mov ax, [di]
    mov dx, [di+2]
    add ax, cx
    mov cx, [cs:si+2]
    adc dx, cx
    mov [bx+0Ch], ax        ;; CDEF + x
    mov [bx+0Eh], dx        ;; 89AB + y + carry
    pop di
    cmp di, di

; O1: int observer between the two stores records first word and old second word
    mov word [bx+12h], 1234h
    mov ax, [0800h]
    mov [bx+10h], ax        ;; first word store
    int 64h                 ;; observer: [bx+1Ch]=ax, [bx+1Eh]=old [bx+12h], [bx+22h]=old [004Ah]
    mov dx, [0802h]
    mov [bx+12h], dx        ;; second word store
    cmp di, di

; O2: absolute pair with int observer between (must not merge)
    mov word [004Ah], 1234h
    mov ax, [0800h]
    mov [0048h], ax
    int 64h                 ;; observer: same outputs
    mov dx, [0802h]
    mov [004Ah], dx
    cmp di, di

; T1: a store between the two reads changes the high word - no join across it
    mov ax, [0800h]
    mov word [0802h], 0BEEFh
    mov dx, [0802h]
    mov [bx+14h], ax        ;; CDEF
    mov [bx+16h], dx        ;; BEEF, not 89AB
    mov word [0802h], 89ABh ;; restore for the next pass
    cmp di, di

; B1: boundary pair loads at ES:FFFE/0000 feeding add/adc - the wrapped dword
; read must NOT form; on 286 a joined access faults instead of wrapping
    mov ax, [es:0FFFEh]
    mov dx, [es:0h]
    mov cx, [cs:si]         ;; D1 left cx = y; reload x
    add ax, cx
    adc dx, di
    mov [bx+18h], ax        ;; A55A + x
    mov [bx+1Ah], dx        ;; CAFE + y + carry
    cmp di, di

; J1: bounded-index word stores (di = bx & 0Fh = 0) -> joined dword store at 60h
    mov ax, [0800h]
    mov dx, [0802h]
    mov di, bx
    and di, 0Fh
    mov [di+60h], ax        ;; dword at 60h = 89ABCDEF
    mov [di+62h], dx
    cmp di, di

    add bx, 80h
    add si, 4
    dec bp
    jnz pass_loop

; J2: bounded index reaching the segment end (di in 0..FFF0h): the second word store wraps
; to offset 0, so the pair must stay two word stores - a joined dword store would write the
; high word at linear segment+10000h instead
    mov di, bx
    and di, 0Fh             ;; di = 0
    not di                  ;; di = FFFFh
    and di, 0FFF0h          ;; di = FFF0h, proven interval [0, FFF0h]
    mov ax, 1111h
    mov dx, 2222h
    mov [di+0Eh], ax        ;; DS:FFFE = 1111h
    mov [di+10h], dx        ;; wraps to DS:0000 = 2222h
    cmp di, di

; J3: a crossing pair through a register known constant (di = FFFEh): the second word store
; wraps to offset 0, a known fact - even under the stay-in-segment relaxation the pair stays
; two word stores
    mov di, 0FFFEh
    mov ax, 3333h
    mov dx, 4444h
    mov [di], ax            ;; DS:FFFE = 3333h
    mov [di+2], dx          ;; wraps to DS:0000 = 4444h, overwrites J2's word
    cmp di, di

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

observe_iret:
    mov [bx+1Ch], ax
    mov dx, [bx+12h]
    mov [bx+1Eh], dx
    mov dx, [004Ah]
    mov [bx+22h], dx
    iret

align 8
inputs:
    dw 0000h, 0000h
    dw 0FFFFh, 0001h
    dw 1234h, 5678h
    dw 8000h, 0FFFFh

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
