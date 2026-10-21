; Calls IOCTL (INT 21h AH=44h) with an AL subfunction the emulator does not implement and
; writes two video cells: cell 0 gets the carry flag (0 or 1), cell 1 gets the AX low byte
; the call returned. The test checks both cells for the DOS invalid-function contract.
; Parameter: sub_function is the AL subfunction.
use16
org 100h

    ; Call the configured IOCTL subfunction and capture what comes back. A subfunction
    ; DOS does not implement reports the failure in the carry flag.
    mov ah, 44h                ; IOCTL
    mov al, sub_function
    xor bx, bx                 ; handle 0
    int 21h
    ; Keep the AL the call returned in DL, because the flags copy below overwrites AX.
    mov dl, al
    pushf
    pop ax
    ; Reduce the saved flags to the carry flag, which is 1 when the call failed.
    and al, 1
    ; Store AL with attribute 07h in the receiving cell of the B800h text segment.
    mov ah, 07h
    mov bx, 0B800h
    mov es, bx
    mov di, 0
    stosw
    ; Store DL, the low byte the call returned, with attribute 07h in the next cell.
    db 8Ah, 0C2h               ; mov al, dl (8Ah form of the original image; fasm emits 88h)
    mov ah, 07h
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
