; OPL Rhythm Mode Test
; Tests OPL percussion/rhythm mode functionality
; Writes OPL registers to enable rhythm mode and trigger percussion instruments

use16
org 0x100

start:
    ; Setup test environment
    mov ax, cs
    mov ds, ax
    
    ; Configure percussion operators first
    ; Bass drum uses operators 12 and 15
    mov dx, 0x388
    mov al, 0x30            ; Operator 12 multiply
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x01
    out dx, al
    call opl_delay
    dec dx
    
    mov al, 0x50            ; Operator 12 level
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x00
    out dx, al
    call opl_delay
    dec dx
    
    mov al, 0x70            ; Operator 12 attack/decay
    out dx, al
    call opl_delay
    inc dx
    mov al, 0xF0
    out dx, al
    call opl_delay
    dec dx
    
    mov al, 0x90            ; Operator 12 sustain/release
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x77
    out dx, al
    call opl_delay
    dec dx
    
    ; Enable rhythm mode (register 0xBD)
    mov al, 0xBD
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x20            ; Enable rhythm mode, no instruments yet
    out dx, al
    call opl_delay
    dec dx
    
    ; Trigger all rhythm instruments
    mov al, 0xBD
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x3F            ; All rhythm instruments on (BD, SD, TT, CY, HH)
    out dx, al
    call opl_delay
    
    ; Small delay
    mov cx, 0x1000
delay_loop:
    loop delay_loop
    
    ; Turn off rhythm instruments
    mov dx, 0x388
    mov al, 0xBD
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x20            ; Rhythm mode on, instruments off
    out dx, al
    
    ; Report success
    mov dx, 0x999
    mov al, 0x00            ; Success
    out dx, al
    
    ; Halt
    hlt

; OPL delay routine
opl_delay:
    push cx
    mov cx, 6
.delay:
    loop .delay
    pop cx
    ret
