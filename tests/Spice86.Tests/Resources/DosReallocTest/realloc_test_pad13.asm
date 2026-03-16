; Test DOS INT 21h/4Ah (realloc) with 13 byte padding
; This should succeed (CF=0) after realloc

BITS 16
ORG  100h

start:
    mov ax, cs
    mov ss, ax
    mov ds, ax
    mov es, ax
    mov sp, stack_end

    ; Calculate size: align to paragraphs
    mov bx, some_data
    shr bx, 4
    inc bx

    mov ah, 4Ah ; realloc
    int 21h     ; CF should be 0

    ; Write result to video memory
    ; Write 'P' (Pass) if CF=0, 'F' (Fail) if CF=1
    mov ax, 0xB800
    mov es, ax
    mov di, 4
    jc .fail
    mov al, 'P'
    jmp .write
.fail:
    mov al, 'F'
.write:
    mov ah, 0x07  ; White on black
    stosw

    mov ax, 4C00h
    int 21h

times 13 db 0AAh
times 64 db 0BBh
stack_end:

some_data:
times 1000 db 0CCh
