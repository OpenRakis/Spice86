; compile it with fasm
; Data-modelling fixture: globals in the data segment. DS = SS = 2000h. Results go to offsets
; 40h-52h (physical 20040h-20052h); offsets 10h-32h hold the globals.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; byte, word and dword globals: each accessor reads exactly its own width - wrong rewrite: a dword
; model that merges the byte and word globals into one slot
    mov byte  [20h], 42h
    mov word  [22h], 1234h
    mov dword [24h], 0AABBCCDDh
    mov al, [20h]
    mov [40h], al
    mov ax, [22h]
    mov [41h], ax
    mov eax, [24h]
    mov [43h], eax

; a byte and a word read at the same offset (two accessors over one slot) - wrong rewrite: dropping
; the byte accessor because the word one covers it
    mov word [28h], 0BEEFh
    mov al, [28h]
    mov [47h], al
    mov ax, [28h]
    mov [48h], ax

; overlapping word accesses [10h] and [11h] - wrong rewrite: modelling [11h] as a separate word
; global instead of a view over the bytes at 11h-12h
    mov byte [10h], 11h
    mov byte [11h], 22h
    mov byte [12h], 33h
    mov ax, [10h]
    mov [4Ah], ax
    mov ax, [11h]
    mov [4Ch], ax

; the same global read through ES after push ds; pop es - wrong rewrite: keying the global on the
; segment register name instead of the linear address
    push ds
    pop es
    mov word [30h], 7777h
    mov ax, [es:30h]
    mov [4Eh], ax

; a global written in one function and read in another - wrong rewrite: a function-local variable
; model that drops the write because writer never reads it
    call writer
    call reader

; DS changed between two accesses: the same offset is another variable - wrong rewrite: a global
; keyed on the offset alone, which would read 99h from the 2000h variable
    mov ax, 2001h
    mov ds, ax
    mov byte [20h], 99h
    mov al, [20h]
    mov bx, 2000h
    mov es, bx
    mov [es:52h], al

    hlt

writer:
    mov word [32h], 5555h
    ret

reader:
    mov ax, [32h]
    mov bx, 2000h
    mov es, bx
    mov [es:50h], ax
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
