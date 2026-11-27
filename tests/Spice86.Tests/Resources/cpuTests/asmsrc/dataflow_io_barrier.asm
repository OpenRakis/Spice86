; compile it with fasm
; IO barrier sections: a port call can write RAM synchronously - the Sound Blaster DSP
; command E2h registers a DMA callback that writes the computed E2 value into the DMA buffer
; when the channel is unmasked, all inside the mask-register port write - so an Io-writing
; statement is a full memory boundary for forwarding and for moving memory reads.
; DS = SS = 2000h. DMA channel 1 is programmed to page 2 (base 20000h) so its word addresses
; land at ds:[300h]/[301h]. Results go to offsets 40h-43h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx

; program DMA channel 1: page 2, word address 0300h, count 0, single write increment
    mov al, 2
    out 83h, al
    out 0Ch, al
    xor al, al
    out 02h, al
    mov al, 3
    out 02h, al
    xor al, al
    out 03h, al
    out 03h, al
    mov al, 45h
    out 0Bh, al

; DSP command E2h with data byte 0: E2 value = 0AAh - 106 = 40h, callback registered
    mov dx, 22Ch
    mov al, 0E2h
    out dx, al
    xor al, al
    out dx, al

; IOB-1: the unmasking port write runs the DMA callback, which writes 40h over the byte the
; 11h store left - forwarding the store's value into the read loses the DMA write
    mov byte [300h], 11h
    mov al, 1
    out 0Ah, al
    mov cl, [300h]
    mov [bx+40h], cl

; reprogram channel 1 to word address 0301h and register the E2 callback a second time
; (E2 value = 40h + 165 = 0E5h)
    out 0Ch, al
    mov al, 1
    out 02h, al
    mov al, 3
    out 02h, al
    xor al, al
    out 03h, al
    out 03h, al
    mov al, 0E2h
    out dx, al
    xor al, al
    out dx, al

; IOB-2: the compare reads [301h] (22h, seeded before the run) while the unmasking port write
; overwrites it with 0E5h - merging the memory operand into the branch re-reads it after the
; port call and takes the wrong side
    cmp byte [301h], 22h
    mov al, 1
    out 0Ah, al
    jne iob2_done
    mov byte [bx+41h], 33h
iob2_done:
; kill the compare's flag results so its defs die at the branch and the operand merges into it
    xor ax, ax

; dump the DMA targets so the oracle also sees the bytes the port calls left
    mov cl, [300h]
    mov [bx+42h], cl
    mov cl, [301h]
    mov [bx+43h], cl

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
