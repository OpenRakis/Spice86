; compile it with fasm
; convention_resolved_calls: a resolved call reads and may write only what the
; callee's convention and the hardware contract describe.
; 1: s1_reads_bx reads the BX the caller set just before the call: the write stays.
; 2: s2_noread reads nothing: the CX write before the call is dead (the caller
;    overwrites CX before anything observes it).
; 3: s3_flagger changes CF: the caller's jc after the call still reads it.
; 4: s4_faulty faults on div cx=0 and s4_via reaches it through a call: both
;    calls stay Unknown, so the BP write reaches the int 0 handler and the
;    handler's CX write reaches the caller.
; 5: s5_spin loops until the timer fires; the int 8 handler reads SI, so the SI
;    write before the call survives through the contract reads.
use16
start:
    cli                     ; no interrupt may fire before the vectors and the PIT are set
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    ; interrupt vectors: int 0 (divide error) at 0000:0000, int 8 (timer) at 0000:0020
    xor ax, ax
    mov es, ax
    mov word [es:0000h], de_handler
    mov word [es:0002h], 0F000h
    mov word [es:0020h], timer_handler
    mov word [es:0022h], 0F000h
    ; PIT counter 0, fast ticks; unmask IRQ0 only
    mov al, 0FFh
    out 0A1h, al
    mov al, 30h
    out 43h, al
    mov al, 0A9h
    out 40h, al
    mov al, 04h
    out 40h, al
    mov al, 0FEh
    out 21h, al
    mov byte [030Ah], 0
    mov byte [030Bh], 0

    ; 1
    mov bx, 1111h
    call s1_reads_bx
    mov bp, 0

    ; 2
    mov di, 2222h
    call s2_noread
    mov di, 9
    mov [0302h], di

    ; 3
    clc
    call s3_flagger
    jnc .cf_clear
    mov byte [030Ah], 1
.cf_clear:

    ; 4
    mov bp, 0BEEFh
    xor dx, dx
    mov ax, 5
    mov cx, 0
    call s4_faulty            ; faults; the handler fixes cx and it returns
    mov [0304h], cx           ; the handler's cx=1 must reach the caller
    mov cx, 1
    call s4_faulty            ; returns without faulting, so the entry is returning
    call s4_via
    mov bp, 0

    ; 5
    mov si, 5555h
    sti
    call s5_spin
    cli
    mov si, 0
    mov cx, 0               ; the loop counter's rest is timing-dependent
    hlt

s1_reads_bx:
    mov [0300h], bx
    ret

s2_noread:
    ret

s3_flagger:
    stc
    ret

s4_faulty:
    test cx, cx
    jnz .ok
    div cx
.ok:
    ret

s4_via:
    call s4_faulty
    ret

s5_spin:
    mov cx, 0FFFFh
.wait:
    cmp byte [030Bh], 1
    jae .done
    loop .wait
.done:
    ret

de_handler:
    mov [0306h], bp
    mov cx, 1
    iret

timer_handler:
    mov [0308h], si
    inc byte [030Bh]
    mov al, 0FFh
    out 21h, al           ; mask IRQ0 so the level-high PIT output cannot re-deliver
    mov al, 20h
    out 20h, al           ; EOI
    iret

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
