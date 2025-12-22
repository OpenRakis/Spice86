; AdLib Gold Stereo Control Test
; Tests AdLib Gold stereo control register writes
; Uses OPL3 extended ports (0x38A/0x38B) for AdLib Gold features

use16
org 0x100

start:
    ; Setup test environment
    mov ax, cs
    mov ds, ax
    
    ; Enable OPL3 mode first (register 0x105)
    mov dx, 0x38A           ; AdLib Gold address port
    mov al, 0x05
    out dx, al
    call opl_delay
    inc dx                  ; AdLib Gold data port (0x38B)
    mov al, 0x01            ; Enable OPL3 mode
    out dx, al
    call opl_delay
    dec dx
    
    ; Configure stereo panning for channel 0
    ; Register 0xC0 controls feedback and algorithm
    mov dx, 0x388           ; Primary OPL port
    mov al, 0xC0
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x30            ; Both left and right channels
    out dx, al
    call opl_delay
    dec dx
    
    ; Configure a simple tone on channel 0
    ; Operator 0 settings
    mov al, 0x20
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x01
    out dx, al
    call opl_delay
    dec dx
    
    mov al, 0x40
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x10
    out dx, al
    call opl_delay
    dec dx
    
    mov al, 0x60
    out dx, al
    call opl_delay
    inc dx
    mov al, 0xF0
    out dx, al
    call opl_delay
    dec dx
    
    mov al, 0x80
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x77
    out dx, al
    call opl_delay
    dec dx
    
    ; Operator 1 (carrier)
    mov al, 0x23
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x01
    out dx, al
    call opl_delay
    dec dx
    
    mov al, 0x43
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x00
    out dx, al
    call opl_delay
    dec dx
    
    mov al, 0x63
    out dx, al
    call opl_delay
    inc dx
    mov al, 0xF0
    out dx, al
    call opl_delay
    dec dx
    
    mov al, 0x83
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x77
    out dx, al
    call opl_delay
    dec dx
    
    ; Set frequency
    mov al, 0xA0
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x41
    out dx, al
    call opl_delay
    dec dx
    
    ; Key on
    mov al, 0xB0
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x32
    out dx, al
    call opl_delay
    
    ; Small delay
    mov cx, 0x1000
delay_loop:
    loop delay_loop
    
    ; Key off
    mov dx, 0x388
    mov al, 0xB0
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x12
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
