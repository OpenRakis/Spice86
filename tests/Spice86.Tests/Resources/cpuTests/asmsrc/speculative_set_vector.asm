; compile it with fasm
; Handler-installation discovery fixture: the program installs an IRQ0 (int 8h)
; handler through int 21h AH=25h. The install seam explores the handler, and
; because vector 8 is hardware-invocable its entry becomes a generation root and
; a (vector, handler) delivery pair is recorded so the generated
; HardwareInterruptFunction dispatches to it.
;
;   positive: irq0_handler lives in the image; on the unobserved run the PIT is
;       programmed, the IRQ fires and the handler writes the marker.
;   negative twin: int 9h installed at 3000:0100, RAM outside the loaded image
;       the program fills afterwards - its bytes never hold loaded values, so
;       the candidate stays pending, no entry exists, and reaching the vector
;       fails as untested in generated code.
;
; Selector byte at physical [0x9000]: 0 = discovery run (no PIT),
;   1 = unobserved run (PIT fires IRQ0), 2 = negative run (also executes int 9h).
;   (0x0500 is off-limits: InitializeDOS writes the low DOS area.)
; DS = SS = 2000h; result word at DS:0x40 (physical 0x20040).

use16

start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h

    ; the BIOS-default PIT rate can deliver IRQ0 at any point while the PIC is
    ; unmasked, which would interrupt the run at a nondeterministic address;
    ; mask every IRQ until the unobserved arm deliberately unmasks IRQ0
    cli
    mov al, 0FFh
    out 21h, al

    ; install the IRQ0 handler: int 21h AH=25h takes DS:DX
    push ds
    mov ax, cs
    mov ds, ax
    mov dx, irq0_handler
    mov ax, 2508h
    int 21h
    ; negative install: int 9h -> 3000:0100, outside the image
    mov dx, 0100h
    mov ax, 3000h
    mov ds, ax
    mov ax, 2509h
    int 21h
    pop ds

    ; a body for the interpreter run of the negative path; the candidate stays
    ; pending because these bytes never held loaded values
    mov ax, 3000h
    mov es, ax
    mov byte [es:0100h], 0CFh

    ; selector byte lives at physical 0x9000
    xor ax, ax
    mov es, ax
    mov al, [es:9000h]
    test al, al
    jz done                          ; discovery run: never program the PIT
    mov byte [0x42], 77h               ; sentinel: the unobserved arm was taken
    ; unmask IRQ0 and enable interrupts first: the one-shot event below is
    ; dropped if it fires while IRQ0 is still masked or IF is clear
    mov al, 11111110b
    out 21h, al
    sti
; PIT channel 0, mode 3, about 7 ms. The handler re-masks every IRQ on its
; first run, so a repeating mode cannot deliver a second IRQ0 inside the
; int 9h frame sequence below and tear the stack frame.
    mov al, 00110110b
    out 43h, al
    mov al, 51h
    out 40h, al
    mov al, 22h
    out 40h, al
    ; the handler masks every IRQ on its first run, so the repeating mode cannot
    ; deliver a second IRQ0 inside the int 9h frame sequence below
wait_tick:
    cmp word [0x40], 0
    je wait_tick
    cmp byte [es:9000h], 2
    jb done                          ; unobserved run: marker seen, halt
    int 9h                           ; negative run: still-pending vector
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

irq0_handler:
    mov word [0x40], 5AA5h
    mov al, 0FFh
    out 21h, al                      ; no second IRQ0: later deliveries would race
    mov al, 20h
    out 20h, al
    iret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
