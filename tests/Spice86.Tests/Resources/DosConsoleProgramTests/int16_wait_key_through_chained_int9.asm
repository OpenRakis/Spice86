; Build: fasm int16_wait_key_through_chained_int9.asm int16_wait_key_through_chained_int9.com
; Hooks INT 9 with a handler that reads port 60h itself and then chains to the BIOS handler,
; injects a key press for 'A' (scancode 1Eh) through the 8042 "write keyboard output buffer"
; command while interrupts are disabled, then calls the blocking INT 16h AH=00h. The key can
; only reach the BIOS keyboard buffer if INT 16h waits with interrupts enabled, letting IRQ 1
; run the hooked handler and the chained BIOS handler. The program writes the returned AL
; (ASCII code), the returned AH (scancode) and the byte the hooked handler saw to the first
; three B800h text cells with attribute 07h.
use16
org 100h

    ; Disable interrupts while installing the INT 9 vector and injecting the scancode,
    ; so the pending IRQ 1 can only fire inside INT 16h.
    cli
    ; Save the current INT 9 vector (0000:0024) into old9.
    xor ax, ax
    mov es, ax
    mov ax, [es:9*4]
    mov [cs:old9], ax
    mov ax, [es:9*4+2]
    mov [cs:old9+2], ax
    ; Install own_int9 as the INT 9 handler.
    mov word [es:9*4], own_int9
    mov [es:9*4+2], cs
    ; Inject the 'A' make scancode as if typed: command D2h tells the 8042 to place the
    ; next byte written to port 60h into its output buffer and raise IRQ 1.
    mov al, 0D2h
    out 64h, al
    mov al, 1Eh
    out 60h, al
    ; Blocking get keystroke: the injected IRQ 1 is still pending, so the key arrives
    ; only if INT 16h enables interrupts while it waits for a key.
    mov ah, 0
    int 16h
    ; Keep the returned key code, then restore the original INT 9 vector.
    mov bx, ax
    cli
    mov ax, [cs:old9]
    mov [es:9*4], ax
    mov ax, [cs:old9+2]
    mov [es:9*4+2], ax
    ; Write the returned AL, the returned AH and the byte seen by the hooked handler to
    ; cells 0, 1 and 2 of the B800h text segment with attribute 07h.
    mov ax, 0B800h
    mov es, ax
    xor di, di
    mov ah, 07h
    mov al, bl
    stosw
    mov al, bh
    stosw
    mov al, [cs:seen]
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h

; Own INT 9 handler: reads port 60h itself like a program that intercepts keystrokes,
; records the byte, then chains to the previous BIOS INT 9 handler.
own_int9:
    push ax
    in al, 60h
    mov [cs:seen], al
    pop ax
    jmp far dword [cs:old9]

seen db 0
old9 dw 0,0
