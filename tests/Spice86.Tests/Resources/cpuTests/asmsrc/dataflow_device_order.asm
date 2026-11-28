; compile it with fasm
; A device read keeps its order with the next device read. Planar memory at
; A000h loads the 4 latches on every read and a write-mode-1 write copies the latches back out.
; The read of es:[0] loads the latches with the plane bytes written there; the read of es:[1]
; then loads es:[1]'s planes. Merging ebx = es:[0] into its RAM consumer moves that read past
; the read of es:[1] - the two reads share one memory version since nothing writes between -
; so the mode-1 store to es:[2] then copies es:[0]'s latches instead of es:[1]'s.
; DS = SS = 2000h. Results go to offsets 40h-4Bh (physical 20040h-2004Bh).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx
    xor si, si

; VGA setup: sequencer map mask all planes, graphics controller maps memory at A000h,
; bit mask FFh.
    mov dx, 03C4h
    mov ax, 0F02h
    out dx, ax
    mov ax, 0604h
    out dx, ax
    mov dx, 03CEh
    mov ax, 0006h
    out dx, ax
    mov ax, 0FF08h
    out dx, ax
    mov ax, 0A000h
    mov es, ax
; plane bytes at es:[0] = 11h/22h/44h/88h, at es:[1] = 55h/66h/77h/99h
    mov dx, 03C4h
    mov ax, 0102h
    out dx, ax
    mov byte [es:0], 11h
    mov byte [es:1], 55h
    mov ax, 0202h
    out dx, ax
    mov byte [es:0], 22h
    mov byte [es:1], 66h
    mov ax, 0402h
    out dx, ax
    mov byte [es:0], 44h
    mov byte [es:1], 77h
    mov ax, 0802h
    out dx, ax
    mov byte [es:0], 88h
    mov byte [es:1], 99h
    mov ax, 0F02h
    out dx, ax
; write mode 1: a write copies the latches, not the data byte
    mov dx, 03CEh
    mov ax, 0105h
    out dx, ax
; DRO-1: the single-use read of es:[0] must not move past the read of es:[1]
    mov ebx, [es:0]
    mov cl, [es:1]
    mov [si+40h], ebx
    mov ebx, 0
    mov byte [es:2], 0
    mov [si+44h], cl
; read es:[2]'s four planes back through the read map select
    mov ax, 0005h
    out dx, ax
    mov ax, 0004h
    out dx, ax
    mov al, [es:2]
    mov [si+48h], al
    mov ax, 0104h
    out dx, ax
    mov al, [es:2]
    mov [si+49h], al
    mov ax, 0204h
    out dx, ax
    mov al, [es:2]
    mov [si+4Ah], al
    mov ax, 0304h
    out dx, ax
    mov al, [es:2]
    mov [si+4Bh], al
; restore the boot map (B800h window) so the end-of-run RAM dump stays inside it
    mov ax, 0E06h
    out dx, ax

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
