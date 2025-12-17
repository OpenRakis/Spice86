; OPL Simple Tone Test
; Tests basic OPL register writes and tone generation
; Writes OPL registers to generate a 440Hz tone and reports success via port 0x999

use16
org 0x100

start:
    ; Setup test environment
    mov ax, cs
    mov ds, ax
    
    ; Enable waveform selection (register 0x01)
    mov dx, 0x388           ; OPL address port
    mov al, 0x01
    out dx, al
    call opl_delay
    inc dx                  ; OPL data port (0x389)
    mov al, 0x20
    out dx, al
    call opl_delay
    dec dx
    
    ; Configure operator 0 (modulator) - Tremolo/vibrato/sustain/KSR/multiply
    mov al, 0x20
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x01            ; Multiplier = 1
    out dx, al
    call opl_delay
    dec dx
    
    ; Configure operator 0 - Key scale level / output level
    mov al, 0x40
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x10            ; Output level
    out dx, al
    call opl_delay
    dec dx
    
    ; Configure operator 0 - Attack rate / decay rate
    mov al, 0x60
    out dx, al
    call opl_delay
    inc dx
    mov al, 0xF0            ; Fast attack
    out dx, al
    call opl_delay
    dec dx
    
    ; Configure operator 0 - Sustain level / release rate
    mov al, 0x80
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x77            ; Medium sustain/release
    out dx, al
    call opl_delay
    dec dx
    
    ; Configure operator 1 (carrier) - same as operator 0
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
    mov al, 0x00            ; Maximum volume for carrier
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
    
    ; Set frequency for 440 Hz (A4)
    ; F-number low 8 bits (register 0xA0)
    mov al, 0xA0
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x41            ; F-number low
    out dx, al
    call opl_delay
    dec dx
    
    ; F-number high 2 bits + octave + key on (register 0xB0)
    mov al, 0xB0
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x32            ; Key on + octave 3 + F-number high
    out dx, al
    call opl_delay
    
    ; Small delay to let tone play
    mov cx, 0x1000
delay_loop:
    loop delay_loop
    
    ; Key off
    mov dx, 0x388
    mov al, 0xB0
    out dx, al
    call opl_delay
    inc dx
    mov al, 0x12            ; Key off
    out dx, al
    
    ; Report success
    mov dx, 0x999
    mov al, 0x00            ; Success
    out dx, al
    
    ; Halt
    hlt

; OPL delay routine (needed after register writes)
opl_delay:
    push cx
    mov cx, 6               ; Short delay
.delay:
    loop .delay
    pop cx
    ret
