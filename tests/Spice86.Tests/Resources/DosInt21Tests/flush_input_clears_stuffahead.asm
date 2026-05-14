; Build: fasm flush_input_clears_stuffahead.asm flush_input_clears_stuffahead.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h

start:
    ; Position cursor at row 2, col 3 before requesting a Device Status Report.
    mov ah, 02h
    mov bh, 00h
    mov dh, 02h
    mov dl, 03h
    int 10h

    ; ESC[6n injects a DSR response into the stuffahead buffer.
    mov ah, 02h
    mov dl, 1Bh
    int 21h
    mov dl, '['
    int 21h
    mov dl, '6'
    int 21h
    mov dl, 'n'
    int 21h

    ; AH=0Ch with AL=01h flushes input buffers; AL=01h is not an allowed follow-up here.
    mov ah, 0Ch
    mov al, 01h
    int 21h

    ; Stuff Z after the flush. The next DOS read must return Z, not leftover DSR bytes.
    mov ah, 05h
    mov cx, 2C5Ah
    int 16h
    mov ah, 08h
    int 21h
    mov dx, details_port
    out dx, al

    mov al, success
    mov dx, result_port
    out dx, al
    hlt
