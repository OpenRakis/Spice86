; Build: fasm switch_char_get_set.asm switch_char_get_set.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh

start:
    ; AH=37h AL=00h: get switch character, AL=0, DL='/' like DOS 5.0 and later.
    mov ah, 37h
    mov al, 00h
    int 21h
    mov bl, dl
    cmp al, 00h
    jne failed
    cmp bl, 2Fh
    jne failed
    mov dx, details_port
    out dx, al
    mov al, bl
    out dx, al

    ; AH=37h AL=01h: set switch character, AL=0, DL keeps its value.
    mov ah, 37h
    mov al, 01h
    mov dl, 2Dh
    int 21h
    mov bl, dl
    cmp al, 00h
    jne failed
    cmp bl, 2Dh
    jne failed
    mov dx, details_port
    out dx, al
    mov al, bl
    out dx, al

    ; AH=37h AL=02h: get availdev character, AL=0, DL='/' like DOS 5.0 and later.
    mov ah, 37h
    mov al, 02h
    int 21h
    mov bl, dl
    cmp al, 00h
    jne failed
    cmp bl, 2Fh
    jne failed
    mov dx, details_port
    out dx, al
    mov al, bl
    out dx, al

    ; AH=37h AL=03h: set availdev character, AL=0, DL keeps its value.
    mov ah, 37h
    mov al, 03h
    mov dl, 2Ah
    int 21h
    mov bl, dl
    cmp al, 00h
    jne failed
    cmp bl, 2Ah
    jne failed
    mov dx, details_port
    out dx, al
    mov al, bl
    out dx, al

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
