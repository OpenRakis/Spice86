; compile it with fasm
; Watched-IVT-cell discovery fixture: the program writes its interrupt handlers
; straight into the interrupt vector table, without any DOS call. The watched
; far cells re-evaluate the entry once both words were written and the new
; handler is explored; because vectors 8 and 9 are hardware-invocable the
; handler entries become generation roots and (vector, handler) delivery pairs
; are recorded so the generated HardwareInterruptFunction reaches them.
;
;   S1/S3 (positive): vector 8 (IRQ0) is installed twice directly:
;       mov word [es:20h], handler; mov word [es:22h], cs. The second install
;       rewrites the segment word with the SAME value - a write that stores the
;       same value must still count, or handler B is never found. On the
;       unobserved run the PIT fires IRQ0 and handler B writes the marker.
;   S2 (negative): vector 9 (IRQ1) is first installed to int9_legit, then only
;       its OFFSET word is rewritten to neg_handler - a half-written far
;       pointer must never be evaluated, so neg_handler is never discovered
;       (golden-only negative).
;   S4: a two-iteration loop installs IRQ0 each pass and ends with int 21h
;       AH=30h. Iteration 2 runs on the hot path and installs s4_handler_b:
;       the write is attributed to the instruction that made it, not to the
;       block's last instruction. The unobserved run's PIT reaches
;       s4_handler_b, which writes 0xB17 at DS:0x44 next to the 5AA5 marker.
;
; Selector byte at physical [0x9000]: 0 = discovery run (no PIT),
;   1 = unobserved run (PIT fires IRQ0). DS = SS = 2000h;
; result word at DS:0x40 (physical 0x20040).

use16

start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h

    ; mask every IRQ until the unobserved arm deliberately unmasks IRQ0
    cli
    mov al, 0FFh
    out 21h, al

    xor ax, ax
    mov es, ax                       ; the IVT always sits at 0000:0000

    ; S1: install IRQ0 handler A directly into the IVT (offset word, then
    ; segment word - the far cell evaluates once both words were written)
    mov word [es:20h], irq0_handler_a
    mov word [es:22h], cs

    ; S3: reinstall handler B in the same segment; the second segment store
    ; writes the same value but must still mark the word written
    mov word [es:20h], irq0_handler_b
    mov word [es:22h], cs

    ; S2: install an int 9h handler completely, then rewrite only the offset
    ; word - the half-written pointer is never evaluated
    mov word [es:24h], int9_legit
    mov word [es:26h], cs
    mov word [es:24h], neg_handler

    ; S4 (hot-path install): a two-iteration loop whose body stores IRQ0's IVT
    ; entry directly and ends with int 21h AH=30h. The first iteration
    ; discovers the block and installs s4_handler_a; the second runs on the hot
    ; path and installs s4_handler_b. Evaluating the queued cell at the next
    ; block boundary would attribute the write to the block's last instruction
    ; - the int 21h node - and drop it, so the cell is re-evaluated right after
    ; the instruction that wrote it.
    xor di, di
s4_loop:
    mov bx, di
    mov ax, [cs:s4_handlers+bx]
    mov word [es:20h], ax
    mov word [es:22h], cs
    mov ah, 30h
    int 21h
    add di, 2
    cmp di, 4
    jb s4_loop

    ; selector byte lives at physical 0x9000
    mov al, [es:9000h]
    test al, al
    jz done                          ; discovery run: never program the PIT
    mov byte [0x42], 77h             ; sentinel: the unobserved arm was taken
    ; unmask IRQ0 and enable interrupts before arming the PIT one-shot
    mov al, 11111110b
    out 21h, al
    sti
; PIT channel 0, mode 3, about 7 ms. The handler re-masks every IRQ on its
; first run, so a repeating mode cannot deliver a second IRQ0.
    mov al, 00110110b
    out 43h, al
    mov al, 51h
    out 40h, al
    mov al, 22h
    out 40h, al
wait_tick:
    cmp word [0x40], 0
    je wait_tick
done:
    cli
    mov al, 0FFh
    out 21h, al                     ; mask every IRQ: no more deliveries
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    ; The emulated and generated runs push their interrupt frames at slightly
    ; different stack depths; scrub the tail so the final-state comparison is
    ; not sensitive to that residue.
    push ss
    pop es
    mov di, 0F00h
    mov cx, 80h
    xor ax, ax
scrub:
    stosw
    loop scrub
    hlt

irq0_handler_a:
    mov word [0x40], 0AA11h
    mov al, 0FFh
    out 21h, al                      ; no second IRQ0: later deliveries would race
    mov al, 20h
    out 20h, al
    iret

irq0_handler_b:
    mov word [0x40], 5AA5h
    mov al, 0FFh
    out 21h, al
    mov al, 20h
    out 20h, al
    iret

int9_legit:
    iret

neg_handler:
    hlt

s4_handler_a:
    mov word [0x40], 0AA22h
    mov al, 0FFh
    out 21h, al                      ; no second IRQ0: later deliveries would race
    mov al, 20h
    out 20h, al
    iret

s4_handler_b:
    mov word [0x40], 5AA5h
    mov word [0x44], 0B17h           ; marker proving the hot-path install ran
    mov al, 0FFh
    out 21h, al
    mov al, 20h
    out 20h, al
    iret

s4_handlers:
    dw s4_handler_a
    dw s4_handler_b

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
