; compile it with fasm
; Data-modelling fixture: segment values. DS = SS = 2000h. Results go to offsets 40h-48h
; (physical 20040h-20048h).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; code, data and stack segments relative to the load segment - a flat .bin has no `seg`, so they
; are computed from CS - wrong rewrite: a model that fixes the data segment to a constant instead
; of CS + delta
    mov ax, cs
    add ax, 100h
    mov [40h], ax
    mov ax, cs
    add ax, 200h
    mov [42h], ax

; the BIOS data area at 0040:0017 (keyboard shift flags) - wrong rewrite: assuming the byte is a
; known constant instead of memory a device may own
    mov ax, 40h
    mov es, ax
    mov al, [es:17h]
    mov [44h], al

; video memory B800:0000 written then read back (the misc graphics register maps the window to
; B800, the bit mask passes the CPU value and the sequencer map mask opens every text plane) -
; wrong rewrite: dropping the write as dead memory nobody reads, or refusing the read-back as
; unknown device bytes
    mov dx, 03CEh
    mov al, 6
    out dx, al
    mov dx, 03CFh
    mov al, 0Ch
    out dx, al
    mov dx, 03CEh
    mov al, 8
    out dx, al
    mov dx, 03CFh
    mov al, 0FFh
    out dx, al
    mov dx, 03C4h
    mov al, 2
    out dx, al
    mov dx, 03C5h
    mov al, 0Fh
    out dx, al
    mov ax, 0B800h
    mov es, ax
    mov ax, [es:0]
    mov [49h], ax
    mov byte [es:0], 42h
    mov byte [es:1], 24h
    mov al, [es:0]
    mov [45h], al
    mov al, [es:1]
    mov [46h], al

; a segment computed at run time - wrong rewrite: resolving the access against the old ES value
; because the new one is data flow, not a constant
    mov ax, ds
    add ax, 100h
    mov es, ax
    mov word [es:10h], 0ABCDh
    mov ax, [es:10h]
    mov [47h], ax

    mov ax, 2000h
    mov es, ax

    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
