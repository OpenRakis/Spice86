; compile with fasm
; The interrupt shadow of sti and mov ss: interrupts are recognized only after the
; next instruction. The machine posts an IRQ 12 before the run, so it is pending at
; the sti. The program's int 74h handler records SS, SP and the pushed IP at its
; entry; the program stores the values the interpreter gives next to them. The
; variant flag at 0000h:0500h selects the path:
;   flag clear - sti, then mov ss starts the fallthrough block, then mov sp: the
;     delivery comes after mov sp, on the new stack.
;   flag 1     - the same, but mov sp heads a loop: the back edge targets it.
;   flag 2     - sti right before a loop header: the delivery comes after the
;     header's first instruction.
; Result region (3000h:0000h): 0000h = delivery count, 0002h/0004h/0006h =
; recorded SS/SP/IP, 0008h/000Ah/000Ch = expected SS/SP/IP.
use16
deliveries = 0x0000
rec_ss = 0x0002
rec_sp = 0x0004
rec_ip = 0x0006
exp_ss = 0x0008
exp_sp = 0x000A
exp_ip = 0x000C

start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:01D0h], frame_recorder
    mov ax, cs
    mov word [es:01D2h], ax
    mov al, [es:0500h]
    mov dx, 3000h
    mov es, dx
    mov bx, 2800h       ; new stack segment
    mov cx, 0800h       ; new stack pointer
    cmp al, 1
    je ss_loop_path
    cmp al, 2
    je sti_loop_path
    mov word [es:exp_ss], bx
    mov word [es:exp_sp], 0800h - 6
    mov word [es:exp_ip], ss_done
    sti                 ; ends its block: mov ss starts the fallthrough block
    mov ss, bx          ; its shadow covers mov sp
    mov sp, cx
ss_done:
    cli
    hlt

ss_loop_path:
    mov word [es:exp_ss], bx
    mov word [es:exp_sp], 0800h - 6
    mov word [es:exp_ip], ss_loop_body
    mov dx, 2
    sti
    mov ss, bx          ; ends its block: the back edge below targets mov sp
ss_loop_head:
    mov sp, cx
ss_loop_body:
    dec dx
    jnz ss_loop_head
    cli
    hlt

sti_loop_path:
    mov word [es:exp_ss], 2000h
    mov word [es:exp_sp], 1000h - 6
    mov word [es:exp_ip], sti_loop_next
    mov dx, 2
    sti                 ; ends its block: the loop header follows
sti_loop_head:
    dec dx
sti_loop_next:
    jnz sti_loop_head
    cli
    hlt

frame_recorder:
    push bp
    mov bp, sp          ; [bp+2] = pushed IP, [bp+4] = CS, [bp+6] = FLAGS
    push ax
    push ds
    mov ax, 3000h
    mov ds, ax
    inc word [deliveries]
    mov ax, ss
    mov [rec_ss], ax
    lea ax, [bp+2]      ; SP at handler entry
    mov [rec_sp], ax
    mov ax, [bp+2]
    mov [rec_ip], ax
    mov al, 20h         ; non-specific EOI, slave then master
    out 0A0h, al
    out 20h, al
    pop ds
    pop ax
    pop bp
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
