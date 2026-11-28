; compile it with fasm
; Sections for the unknown-memory-is-RAM relaxation: a proved-RAM store forwards in both
; modes; an unknown-segment store pair joins and an unknown-segment read pair merges only under
; the relaxation; a proved device range stays exact in both modes.
; DS = SS = 2000h. Results go to offsets 40h-45h (physical 20040h-20045h); scratch 20h-23h;
; the unknown ES segment comes from [62h] across a port boundary so the analysis cannot resolve
; it, and it holds 2000h at runtime so the accesses land in the oracle region.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx
    xor si, si

; a store and read proved ordinary RAM forward in both modes
    mov word [20h], 1234h
    mov ax, [20h]
    mov [bx+40h], ax

; a proved device range is never relaxed - the overwrite keeps the first store in both
; modes, exactly like strict (es = A000h)
    mov ax, 0A000h
    mov es, ax
    mov word [es:30h], 0BEEFh
    mov word [es:30h], 1234h

; unknown segment: [62h] holds 2000h and a port write keeps the load of es unfilled - the
; analysis cannot prove its value, the runtime reads and writes real RAM
    mov word [62h], 2000h
    mov al, 0
    out 80h, al
    mov es, [62h]

; two byte stores of an unknown segment join into one word store only when the
; relaxation treats them as ordinary RAM; the word read forwards only then too
    mov byte [es:20h], 0AAh
    mov byte [es:21h], 0BBh
    mov cx, [es:20h]
    mov [bx+42h], cx

; two byte reads of an unknown segment merge only under the relaxation
    mov byte [es:22h], 77h
    mov byte [es:23h], 88h
    mov al, [es:22h]
    mov ah, [es:23h]
    mov [bx+44h], ax

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
