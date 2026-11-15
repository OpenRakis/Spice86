; compile it with fasm
; Dump-time retry fixture: discovery leaves candidates pending on bytes that do
; not hold their loaded values, and the dump-time retry decodes only the ones
; the program restored.
;
;   S1 (positive): handler_a's first byte is corrupted, the handler is
;       installed on vector 8 (IRQ0) with int 21h AH=25h - the InterruptVector
;       candidate stays pending on the int node because its bytes are no longer
;       trusted. The byte is then restored; the dump-time retry sees trusted
;       bytes and decodes the handler (it becomes a generation root).
;   S2 (negative): neg_handler is installed on vector 60h with its first byte
;       permanently zeroed - the dump-time retry finds untrusted bytes and the
;       address stays undiscovered (golden-only negative).
;   S3 (half-written far cell): only the OFFSET word of vector 9's IVT entry is
;       written. The dump-time far-cell read pairs it with the old segment
;       word, but the evaluation carries no program trigger instruction and is
;       refused before any exploration (golden-only).
;
; Result word at DS:0x40 (physical 0x20040); DS = SS = 2000h.

use16

start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h

    cli
    mov al, 0FFh
    out 21h, al                      ; no hardware deliveries during the run

    xor ax, ax
    mov es, ax                       ; the IVT always sits at 0000:0000

    ; int 21h AH=25h takes the handler from DS:DX; the code lives in CS.
    push ds
    push cs
    pop ds

    ; S1: corrupt the handler's first byte, install, then restore it.
    mov byte [cs:handler_a], 90h
    mov ah, 25h
    mov al, 08h
    mov dx, handler_a
    int 21h
    mov byte [cs:handler_a], 0C7h    ; original first byte of handler_a

    ; S2: the target's first byte stays zeroed; the candidate never decodes.
    mov byte [cs:neg_handler], 0
    mov ah, 25h
    mov al, 60h
    mov dx, neg_handler
    int 21h

    pop ds

    ; S3: half-write the vector 9 far pointer - offset word only.
    mov word [es:24h], half_target

    mov word [0x40], 1
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

handler_a:
    mov word [0x40], 0AAh            ; C7 06 40 00 AA 00
    iret

neg_handler:
    iret

half_target:
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
