; Speculative CFG fixture: sweep of a seeded hardware-interrupt generation root.
;
; With interrupt vectors installed and speculation on, the emulator seeds the INT 8
; (IRQ0 / 8254 PIT) handler as a speculative CFG generation root at construction time.
; The stub installed by the emulator at IVT[8] is written first; this 64 KB image is
; then loaded over F000, overwriting it; and the known-safe seeding runs last, decoding
; whatever stale bytes the image left at F000:0006.
;
; The low pad is NOPs, not zeros: the speculative explorer no longer decodes chains of
; zero instructions, so a zero pad would seed nothing and the first INT 8 would have no
; stale speculative node to reconcile. The program copies its own minimal ISR over the
; seeded handler address and lets the timer fire. On the first INT 8, live memory no
; longer matches the seeded NOP decode, so the reconciler SWEEPS the seeded speculative
; node - which is still registered as a generation root. This is the exact scenario the
; RemoveInstruction fan-out fix must handle: the swept root must not linger as a
; detached, de-indexed ghost.
;
; Real code lives high (F000:8000) so the low handler region and the ISR copy never collide
; with executing code.
;
; assembled with: fasm speculative_seeded_timer_sweep.asm ../speculative_seeded_timer_sweep.bin

use16

times 0x8000-$ db 0x90  ; NOP-fill the low F000 handler region (kept non-zero on purpose)

start:
    xor ax, ax
    mov ds, ax          ; DS=0 to read the IVT
    mov ss, ax
    mov sp, 0x7000
    mov byte [cs:marker], 0

    ; destination = emulator-seeded INT 8 handler H, read from IVT[8] (0000:0020)
    mov di, [0x20]      ; H offset
    mov ax, [0x22]      ; H segment (0xF000)
    mov es, ax

    ; source = our ISR template, assembled below in this same segment (CS == 0xF000)
    push cs
    pop ds
    mov si, isr
    mov cx, isr_end - isr
    cld
    rep movsb           ; overwrite the seeded handler bytes with our ISR

    ; configure PIT channel 0 (mode 3, ~1ms), same sequence as externalint.asm
    mov al, 00110110b
    out 43h, al
    mov al, 51h
    out 40h, al
    mov al, 22h
    out 40h, al

    ; unmask the master PIC so IRQ0 (INT 8) can fire
    mov al, 0
    out 21h, al

    sti
waitloop:
    cmp byte [cs:marker], 0
    jz waitloop

    cli
    hlt

; Minimal ISR copied over the seeded handler. Its bytes differ from the seeded
; callback+IRET decode, so the first INT 8 reconciles to a mismatch and sweeps the seeded
; root. Never executed from here (it sits past the HLT); it only runs once copied to H,
; where CS is also 0xF000, so [cs:marker] resolves to the same byte the wait loop polls.
isr:
    mov al, 0x20
    out 0x20, al        ; EOI to the master PIC
    inc byte [cs:marker]
    iret
isr_end:

marker:
    db 0

rb 65520-$
jmp start
rb 65535-$
db 0ffh
