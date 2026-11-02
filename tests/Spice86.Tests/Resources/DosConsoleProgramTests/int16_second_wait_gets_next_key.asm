; Build: fasm int16_second_wait_gets_next_key.asm int16_second_wait_gets_next_key.com
; Exercises two consecutive blocking INT 16h AH=00h calls. The first injects an 'A' key press
; (scancode 1Eh) through the 8042 "write keyboard output buffer" command and reads it. Before
; the second call the program arms its own INT 1Ch handler, so that while INT 16h waits with an
; empty BIOS keyboard buffer, the next timer tick injects a 'B' key press (scancode 30h). The
; second call must return 'b', proving the wait loop delivers the next real key instead of
; re-reading the stale port 60h byte of the previous key. The program writes both returned
; ASCII codes to the first two B800h text cells with attribute 07h.
use16
org 100h

    ; Save the current INT 1Ch vector (0000:0070) into old1c. The BIOS timer tick calls
    ; INT 1Ch on every IRQ 0.
    xor ax, ax
    mov es, ax
    mov ax, [es:1Ch*4]
    mov [cs:old1c], ax
    mov ax, [es:1Ch*4+2]
    mov [cs:old1c+2], ax
    ; Install own_1c as the INT 1Ch handler.
    mov word [es:1Ch*4], own_1c
    mov [es:1Ch*4+2], cs
    sti
    ; Inject the 'A' make scancode as if typed: command D2h tells the 8042 to place the
    ; next byte written to port 60h into its output buffer and raise IRQ 1.
    mov al, 0D2h
    out 64h, al
    mov al, 1Eh
    out 60h, al
    ; Blocking get keystroke: reads the injected 'a'.
    mov ah, 0
    int 16h
    mov [cs:first], al
    ; Arm the INT 1Ch handler so the next timer tick injects 'B' while the following
    ; call waits on an empty buffer.
    mov byte [cs:armed], 1
    ; Blocking get keystroke again: must return 'b' once the tick injects it.
    mov ah, 0
    int 16h
    mov [cs:second], al
    ; Restore the original INT 1Ch vector.
    cli
    mov ax, [cs:old1c]
    mov [es:1Ch*4], ax
    mov ax, [cs:old1c+2]
    mov [es:1Ch*4+2], ax
    ; Write both returned ASCII codes to cells 0 and 1 of the B800h text segment with
    ; attribute 07h.
    mov ax, 0B800h
    mov es, ax
    xor di, di
    mov ah, 07h
    mov al, [cs:first]
    stosw
    mov al, [cs:second]
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h

; Own INT 1Ch handler: on the first tick after being armed, injects a 'B' make scancode
; through the 8042 "write keyboard output buffer" command, then chains to the previous
; INT 1Ch handler.
own_1c:
    cmp byte [cs:armed], 0
    je .done
    mov byte [cs:armed], 0
    push ax
    mov al, 0D2h
    out 64h, al
    mov al, 30h
    out 60h, al
    pop ax
.done:
    jmp far dword [cs:old1c]

armed db 0
first db 0
second db 0
old1c dw 0,0
