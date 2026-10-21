; Calls INT 21h with the given AH function, then writes a sentinel byte directly into
; video cell 0. The test preloads Ctrl-C, so a function that checks for a pending break
; terminates the program first and the sentinel never appears in the cell.
; Parameters: ah_function is the INT 21h function, sentinel is the byte written to cell 0
; when the call returns normally.
use16
org 100h

    ; Call the configured INT 21h function. A function that checks for a pending Ctrl-Break
    ; terminates the program here, so the sentinel write below never runs.
    mov ah, ah_function
    int 21h
    ; Store AL with attribute 07h in the receiving cell of the B800h text segment. AL is
    ; the sentinel that marks a normal return of the call.
    mov al, sentinel
    mov ah, 07h
    mov bx, 0B800h
    mov es, bx
    mov di, 0
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
