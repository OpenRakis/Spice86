; compile it with fasm
; Safe device access patterns that generated code may merge into device operation calls (CMOS index/data,
; PIT control + two channel writes, VGA DAC triplet and index/data pairs, OPL address/data pairs) and the
; hazardous neighbours that must stay uncombined: an unrelated port write, a memory access and a call
; between the two halves of a CMOS read.
; DS = SS = 0. Results are stored sequentially at offsets 0000h..000Eh.
use16
start:
    mov ax, 0
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 0100h
    cld
    xor bx, bx

; ---- CMOS: 0x70 write + 0x71 read is a safe register read -> [0] = StatusB & 7Fh = 02h
    mov al, 08Bh
    out 070h, al
    in al, 071h
    and al, 07Fh
    mov [bx + 00h], al

; ---- CMOS: index + data write of a RAM register, then a fresh index + read returns it -> [1] = 45h
    mov al, 0A0h
    out 070h, al
    mov al, 045h
    out 071h, al
    mov al, 0A0h
    out 070h, al
    in al, 071h
    mov [bx + 01h], al

; ---- Hazard: an unrelated port write between select and read must keep the sequence uncombined.
;     The read still returns the selected StatusB -> [2] = 02h
    mov al, 08Bh
    out 070h, al
    out 080h, al
    in al, 071h
    and al, 07Fh
    mov [bx + 02h], al

; ---- Hazard: a memory access between select and read -> [3] = 02h
    mov al, 08Bh
    out 070h, al
    mov cl, [bx]
    in al, 071h
    and al, 07Fh
    mov [bx + 03h], al

; ---- Hazard: a call between select and read; the subroutine reselects a CMOS RAM register, so the
;     read returns the byte written in the CMOS write section - and the sequence must stay
;     uncombined or the merge's own select would return StatusB instead -> [4] = 45h
    mov al, 08Bh
    out 070h, al
    call empty_sub
    in al, 071h
    and al, 07Fh
    mov [bx + 04h], al

; ---- PIT: control word (channel 0, low/high, mode 2) + two channel-0 writes is a safe program.
;     Read-back command then reports the programmed mode in the status byte -> [5] = 34h
    mov al, 034h
    out 043h, al
    mov al, 005h
    out 040h, al
    mov al, 000h
    out 040h, al
    mov al, 0C2h
    out 043h, al
    in al, 040h
    and al, 03Fh
    mov [bx + 05h], al

; ---- PIT channel 2 (low/high, mode 3), divisor 1234h -> [6] = 36h
    mov al, 0B6h
    out 043h, al
    mov al, 034h
    out 042h, al
    mov al, 012h
    out 042h, al
    mov al, 0C8h
    out 043h, al
    in al, 042h
    and al, 03Fh
    mov [bx + 06h], al

; ---- VGA DAC: write index 10h + three data writes is a safe palette set. A fourth data write lands on
;     entry 11h red. Read both entries back via 0x3C7/0x3C9 -> [7,8,9] = 01,02,03 ; [0Ah] = 3Fh
    mov dx, 03C8h
    mov al, 010h
    out dx, al
    mov dx, 03C9h
    mov al, 001h
    out dx, al
    mov al, 002h
    out dx, al
    mov al, 003h
    out dx, al
    mov al, 03Fh
    out dx, al
    mov dx, 03C7h
    mov al, 010h
    out dx, al
    mov dx, 03C9h
    in al, dx
    mov [bx + 07h], al
    in al, dx
    mov [bx + 08h], al
    in al, dx
    mov [bx + 09h], al
    mov dx, 03C7h
    mov al, 011h
    out dx, al
    mov dx, 03C9h
    in al, dx
    mov [bx + 0Ah], al

; ---- VGA sequencer: 0x3C4 index + 0x3C5 data pair -> [0Bh] = 0Fh
    mov dx, 03C4h
    mov al, 002h
    out dx, al
    mov dx, 03C5h
    mov al, 00Fh
    out dx, al
    mov dx, 03C4h
    mov al, 002h
    out dx, al
    mov dx, 03C5h
    in al, dx
    mov [bx + 0Bh], al

; ---- VGA graphics controller: one word write at 0x3CE programs index + data -> [0Ch] = 40h
    mov ax, 04005h
    mov dx, 03CEh
    out dx, ax
    mov dx, 03CEh
    mov al, 005h
    out dx, al
    mov dx, 03CFh
    in al, dx
    mov [bx + 0Ch], al

; ---- OPL: 0x388 address + 0x389 data pair. The test machine replaces the 0x388 handler with a
;     latch, so this pair must stay uncombined and the read sees the latch's last write (20h); a
;     wrongly merged pair would read the real chip's zeroed status instead -> [0Dh] = 20h
    mov dx, 0388h
    mov al, 020h
    out dx, al
    mov dx, 0389h
    mov al, 0F1h
    out dx, al
    mov dx, 0388h
    in al, dx
    mov [bx + 0Dh], al

; ---- OPL: the 0x38A bank alias pair keeps its real handler and combines; on OPL3 a status read
;     of a non-zero port pair returns FFh -> [0Eh] = C0h
    mov dx, 038Ah
    mov al, 004h
    out dx, al
    mov dx, 038Bh
    mov al, 021h
    out dx, al
    mov dx, 038Ah
    in al, dx
    and al, 0C0h
    mov [bx + 0Eh], al
    hlt

; Selects a CMOS RAM register so a read after the call observes whether the select was preserved.
empty_sub:
    mov al, 0A0h
    out 070h, al
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0ffh
