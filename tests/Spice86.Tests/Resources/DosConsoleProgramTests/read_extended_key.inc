; Reads the extended keystroke with the given INT 16h function and writes the scan code the
; BIOS returned, copied from AH into AL, into video cell 0. The test preloads an extended
; key and compares the cell with that key's scan code.
; Parameter: ah_function is the INT 16h extended keystroke function. AH=10h is the enhanced
; get keystroke, which dequeues the key; AH=11h is the enhanced keystroke status, which
; reports a pending key without dequeuing any other data.
use16
org 100h

    ; Read the extended keystroke with the configured INT 16h function. AH then holds the
    ; scan code of the key.
    mov ah, ah_function
    int 16h
    ; Copy the scan code from AH into AL before the attribute below overwrites AH.
    mov al, ah
    ; Store AL with attribute 07h in the receiving cell of the B800h text segment.
    mov ah, 07h
    mov bx, 0B800h
    mov es, bx
    mov di, 0
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
