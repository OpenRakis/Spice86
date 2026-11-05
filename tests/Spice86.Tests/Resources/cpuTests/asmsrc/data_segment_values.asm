; compile it with fasm
; Sections for the segment-value lattice: a constant merged right (S1), a constant kept through
; a live wrong source (S2), an entry-segment copy chain (S3), a stack-restored value (S4), a
; join of two different values (S5), a les pointer load (S6), a loop incrementing ES (S7),
; a callee that keeps a different DS after it returns (S8), a callee that reads a DS global with
; two different live DS (S9), a callee that reads a DS global with only DS = 2000h live (S10),
; a callee that saves and restores DS (S11) and an int handler that changes ES before iret (S12).
; S13a joins two pushes of different values into one pop, S13b pops a slot a push defines on one
; path only, S14 reads ES loaded by les while DS is lds-loaded with another value, S15 calls a
; callee that swaps DS/ES through the stack under an entry equality and preserves them by value.
; S16 pops es through slots of mixed widths: the last pop reads the high half of a pushed edx,
; not a stale 16-bit slot at the same delta.
; S17 enters one function body through two int vectors: the first sets ES and flows into a tail
; block whose middle is the second handler, invoked directly with another segment.
; DS = SS = 2000h. Results go to offsets 40h-5Ch (physical 20040h-2005Ch); offsets 60h-6Fh are
; the bytes and the far pointer the sections read, with per-segment copies that stay clear of the
; result region under every tested segment; 90h-97h hold two far pointers and 3000h:62h /
; 4000h:62h-63h the bytes S14, S15 and S17 read.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; per-segment bytes the sections read back
    mov byte [10h], 0E1h
    mov word [60h], 1111h
    mov byte [62h], 44h
    mov byte [63h], 77h
    mov byte [64h], 99h
    mov byte [65h], 0BBh
    mov byte [67h], 0DDh
    mov byte [68h], 11h
    mov word [6Ah], 5678h
    mov word [6Ch], 2001h
    mov ax, 2001h
    mov ds, ax
    mov byte [61h], 33h
    mov byte [62h], 55h
    mov byte [63h], 88h
    mov byte [64h], 0AAh
    mov byte [68h], 22h
    mov ax, 2002h
    mov ds, ax
    mov byte [62h], 66h
    mov ax, 3000h
    mov ds, ax
    mov byte [10h], 0E2h
    mov byte [62h], 66h
    mov ax, 4000h
    mov ds, ax
    mov byte [63h], 42h
    mov byte [62h], 0D4h
    mov ax, 2000h
    mov ds, ax
    mov word [90h], 0
    mov word [92h], 3000h
    mov word [94h], 0
    mov word [96h], 4000h

; S1: es = ds, the store lands in the same bytes as ds:[60h]
    mov ax, ds
    mov es, ax
    mov word [es:60h], 1111h
    mov ax, [ds:60h]
    mov [40h], ax

; S2: es stays 2000h while ds moves to 2001h; reading es:[60h] reads the 2000h bytes
    mov ax, ds
    mov es, ax
    mov ax, 2001h
    mov ds, ax
    mov ax, [es:60h]
    mov bx, 2000h
    mov ds, bx
    mov [42h], ax

; S3: a copy chain leaves es = ds
    push ds
    pop es
    mov byte [es:61h], 22h
    mov al, [es:61h]
    mov [44h], al

; S4: the pushed ds word is overwritten on the stack; pop es reads the written value
    push ds
    mov bx, sp
    mov word [ss:bx], 2001h
    pop es
    mov al, [es:61h]
    mov [45h], al

; S5: two paths set es differently and join; a two-turn loop runs both arms so the join is
; really reached with both values (turn 1 es = 2001h -> [46h] = 55h, turn 2 es = 2000h ->
; [51h] = 44h)
    mov si, 46h
    mov dx, 2
s5_loop:
    mov cx, dx
    dec cx
    jcxz s5_low
    mov ax, 2001h
    mov es, ax
    jmp short s5_join
s5_low:
    mov ax, 2000h
    mov es, ax
s5_join:
    mov al, [es:62h]
    mov [si], al
    mov si, 51h
    dec dx
    jnz s5_loop

; S6: les loads a far pointer; the read goes to the loaded segment
    les di, [6Ah]
    mov al, [es:62h]
    mov [47h], al

; S7: a loop bumps es through 2000h-2002h and keeps each read
    xor di, di
    mov ax, 2000h
    mov es, ax
    mov cx, 3
s7_loop:
    mov al, [es:62h]
    mov [di+48h], al
    inc di
    mov ax, es
    inc ax
    mov es, ax
    loop s7_loop

; S8: the callee leaves DS = 2001h when it returns; the caller reads through the live ds
    call s8_callee
    mov al, [63h]
    mov bx, 2000h
    mov es, bx
    mov [es:4Bh], al
    mov ds, bx

; S9: one reader called with DS = 2000h then DS = 2001h
    call s9_reader
    mov [4Ch], al
    mov ax, 2001h
    mov ds, ax
    call s9_reader
    mov bx, 2000h
    mov ds, bx
    mov [4Dh], al

; S10: one reader called with DS = 2000h only
    call s10_reader
    mov [4Eh], al

; S11: the callee saves ds, changes it, restores it; the caller reads through ds
    call s11_callee
    mov al, [67h]
    mov [4Fh], al

; S12: an int to a program handler that changes es before iret
    xor ax, ax
    mov es, ax
    mov word [es:60h*4], s12_handler
    mov [es:60h*4+2], cs
    mov ax, 2000h
    mov es, ax
    int 60h
    mov al, [es:68h]
    mov [50h], al
    mov ax, 2000h
    mov es, ax

; S13a: two arms push different values; the pop at the join reads a slot whose definitions
; disagree, so es is unknown ([52h] = 55h from 2001h:62h, [53h] = 44h from 2000h:62h)
    mov si, 52h
    mov dx, 2
s13a_loop:
    mov cx, dx
    dec cx
    jcxz s13a_low
    mov ax, 2001h
    push ax
    jmp short s13a_join
s13a_low:
    mov ax, 2000h
    push ax
s13a_join:
    pop es
    mov al, [es:62h]
    mov [si], al
    mov si, 53h
    dec dx
    jnz s13a_loop

; S13b: only one arm pushes; the pop on the other arm reads the same slot the push would have
; written, so the read is incomplete and es is unknown (turn 1 es = 2001h -> [54h] = 55h, turn 2
; pops the word turn 1 left in that slot -> es = 2001h -> [55h] = 55h)
    mov si, 54h
    mov dx, 2
s13b_loop:
    mov cx, dx
    dec cx
    jcxz s13b_nopush
    mov ax, 2001h
    push ax
    jmp short s13b_join
s13b_nopush:
    sub sp, 2
s13b_join:
    pop es
    mov al, [es:62h]
    mov [si], al
    mov si, 55h
    dec dx
    jnz s13b_loop
    mov ax, 2000h
    mov es, ax

; S14: es and ds both come from far-pointer loads with different values; the es read is through
; the es register class ([56h] = 66h from 3000h:62h)
    push ds
    les ax, [90h]
    lds bx, [94h]
    mov al, [es:62h]
    pop ds
    mov bx, 56h
    mov [bx], al
    mov ax, 2000h
    mov es, ax

; S15: s15_inner reads through es under an entry ds = es equality; way 2 reaches it through
; s15_outer, which passes a ds loaded by lds and es = ds (equality without a known value),
; way 1 calls it with ds = es = 2000h
    push ds
    lds bx, [94h]
    call s15_outer
    pop ds
    mov bx, 57h
    mov [bx], al
    mov ax, 2000h
    mov ds, ax
    mov es, ax
    call s15_inner
    mov bx, 58h
    mov [bx], al
    mov al, [62h]
    mov bx, 59h
    mov [bx], al

; S16: a 16-bit push slot must not attribute a wider or narrower pop: pop ecx consumes the two
; pushed words, pop cx reads the low half of the pushed edx and pop es its high half, so es is
; 3000h and [5Ah] reads 3000:0010 (E2h); a stale ax slot at the same delta would resolve es to
; the pushed 2000h instead
    mov ax, 2000h
    mov edx, 30000000h
    push ax
    push ds
    pop ecx
    push edx
    pop cx
    pop es
    mov al, [es:10h]
    mov [5Ah], al
    mov ax, 2000h
    mov es, ax

; S17: a shared tail entered through two call roots: s17_x jumps to the arm that sets es =
; 3000h, s17_y is also reached directly with es = 4000h, so the es of s17_y merges the
; in-partition value with the entry value ([5Bh] = 66h from 3000h:62h, [5Ch] = D4h from
; 4000h:62h)
    mov cx, 0
    call s17_a
    mov [5Bh], al
    mov cx, 1
    call s17_b
    mov [5Ch], al
    mov ax, 2000h
    mov es, ax

; S18: the IRQ0 handler changes BP without restoring it; the delay makes a tick pending and
; s18_wait's post-sti event check is where it fires, so the [bp-2] read after the in-frame call
; goes through the handler's bp (0064h -> [62h] = 44h), not the frame slot holding the marker
    xor ax, ax
    mov es, ax
    mov word [es:8*4], s18_irq0
    mov [es:8*4+2], cs
    mov ax, ds
    mov es, ax
    mov byte [0E60h], 0
    mov al, 00110110b
    out 43h, al
    mov al, 051h
    out 40h, al
    mov al, 022h
    out 40h, al
    xor al, al
    out 21h, al
    mov cx, 0FFFFh
s18_delay:
    dec cx
    jnz s18_delay
    call s18_frame
    mov [83h], al
    cli
    mov ax, 2000h
    mov es, ax

; S19: a word read at SS:FFFF faults - the CPU raises #SS (vector 0Ch) where a data-kind access
; raises #GP (0Dh). s19_ss stores 0Ch at [84h] and skips the faulting instruction; s19_gp
; would store 0Dh at [85h].
    xor ax, ax
    mov es, ax
    mov word [es:0Ch*4], s19_ss
    mov [es:0Ch*4+2], cs
    mov word [es:0Dh*4], s19_gp
    mov [es:0Dh*4+2], cs
    mov ax, ds
    mov es, ax
    mov bp, 0FFFFh
    mov ax, [bp]

    hlt

s18_frame:
    push bp
    mov bp, sp
    sub sp, 2
    mov word [bp-2], 0D4D4h
    call s18_wait
    mov al, [bp-2]
    add sp, 2
    pop bp
    ret

s18_wait:
    sti
    nop
    ret

s18_irq0:
    mov bp, 0064h
    mov byte [0E60h], 1
    mov al, 20h
    out 20h, al
    iret

s19_ss:
    mov bp, sp
    add word [bp], 3
    mov byte [84h], 0Ch
    iret

s19_gp:
    mov bp, sp
    add word [bp], 3
    mov byte [85h], 0Dh
    iret

s8_callee:
    mov ax, 2001h
    mov ds, ax
    ret

s9_reader:
    mov al, [64h]
    ret

s10_reader:
    mov al, [65h]
    ret

s11_callee:
    push ds
    mov ax, 2001h
    mov ds, ax
    mov byte [67h], 0CCh
    pop ds
    ret

s12_handler:
    push ax
    mov ax, 2001h
    mov es, ax
    pop ax
    iret

; passes its entry ds and es = ds to s15_inner
s15_outer:
    mov ax, ds
    mov es, ax
    call s15_inner
    ret

; reads es:[63h] under an entry ds = es equality, then swaps the pushed ds and es back: each
; register leaves the call holding the other entry version's value, which is its own value
s15_inner:
    mov al, [es:63h]
    push ds
    push es
    pop ds
    pop es
    ret

; s17_a / s17_b are the two call roots; s17_x, s17_y and s17_w form one shared region entered
; at s17_x (through s17_a) and at s17_y (through s17_b), so the es of s17_y merges the
; in-partition value from s17_w with the s17_b entry's value
s17_a:
    jmp s17_x
s17_b:
    mov ax, 4000h
    mov es, ax
    jmp s17_y
s17_x:
    cmp cx, 0
    je s17_w
    ret
s17_y:
    mov al, [es:62h]
    cmp cx, 1
    je s17_x
    ret
s17_w:
    mov ax, 3000h
    mov es, ax
    jmp s17_y

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
