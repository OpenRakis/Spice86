; opl_write_delay.asm - OPL port write latency test
;
; Classic Adlib detection: reset timers, set Timer 1=0xFF, start,
; standard post-data delay (35 IO reads), poll until overflow.
;
; No INT 10h before the measurement. Print result after.
;
; Results (reported via ports 0x999/0x998 for Spice86):
;   Port 0x998: iteration count (low byte, then high byte)
;   Port 0x999: exit code (0x00=ok, 0x01=status dirty, 0x02=timeout)
;
; Assemble:  nasm -f bin -o opl_write_delay.com opl_write_delay.asm

[bits 16]
[org 0x100]

OPL_ADDR    equ 0x388
OPL_DATA    equ 0x389
RESULT_PORT equ 0x999
DETAIL_PORT equ 0x998
TIMEOUT     equ 5000

start:
    ; === Reset timers ===
    call opl_write_0x04_0x60
    call opl_write_0x04_0x80

    ; === Verify clean status ===
    mov dx, OPL_ADDR
    in al, dx
    test al, 0xE0
    jz .status_clear

    ; Status not clear — report failure 0x01
    mov dx, DETAIL_PORT
    mov al, 0
    out dx, al
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0x01
    out dx, al
    ; Print error
    mov si, msg_fail
    call print_string
    jmp .exit

.status_clear:
    ; === Set Timer 1 counter = 0xFF (period = 80us) ===
    mov dx, OPL_ADDR
    mov al, 0x02
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0xFF
    out dx, al
    call opl_data_delay

    ; === Start Timer 1 (reg 0x04 = 0x21) ===
    mov dx, OPL_ADDR
    mov al, 0x04
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x21
    out dx, al
    call opl_data_delay

    ; === Poll status until overflow ===
    xor cx, cx
    mov dx, OPL_ADDR
.poll_loop:
    cmp cx, TIMEOUT
    jae .timeout_reached
    in al, dx
    inc cx
    test al, 0xC0
    jnz .timer_fired
    jmp .poll_loop

.timeout_reached:
    ; Report to Spice86
    mov dx, DETAIL_PORT
    mov al, cl
    out dx, al
    mov al, ch
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0x02
    out dx, al
    ; Print for DOSBox
    mov si, msg_timeout
    call print_string
    mov ax, cx
    call print_dec
    mov si, msg_iters
    call print_string
    jmp .cleanup

.timer_fired:
    ; Report to Spice86
    mov dx, DETAIL_PORT
    mov al, cl
    out dx, al
    mov al, ch
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0x00
    out dx, al
    ; Print for DOSBox
    mov si, msg_ok
    call print_string
    mov ax, cx
    call print_dec
    mov si, msg_iters
    call print_string

.cleanup:
    call opl_write_0x04_0x60
    call opl_write_0x04_0x80

.exit:
    int 0x20

; ============================================================
; OPL Helper Subroutines
; ============================================================

opl_write_0x04_0x60:
    mov dx, OPL_ADDR
    mov al, 0x04
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x60
    out dx, al
    call opl_data_delay
    ret

opl_write_0x04_0x80:
    mov dx, OPL_ADDR
    mov al, 0x04
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x80
    out dx, al
    call opl_data_delay
    ret

opl_addr_delay:
    push cx
    push dx
    mov cx, 6
    mov dx, OPL_ADDR
.loop:
    in al, dx
    loop .loop
    pop dx
    pop cx
    ret

opl_data_delay:
    push cx
    push dx
    mov cx, 35
    mov dx, OPL_ADDR
.loop:
    in al, dx
    loop .loop
    pop dx
    pop cx
    ret

; ============================================================
; Print (used AFTER measurement only)
; ============================================================

print_string:
    lodsb
    or al, al
    jz .done
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    jmp print_string
.done:
    ret

print_dec:
    push bx
    push cx
    push dx
    xor cx, cx
    mov bx, 10
.div_loop:
    xor dx, dx
    div bx
    push dx
    inc cx
    or ax, ax
    jnz .div_loop
.print_loop:
    pop ax
    add al, '0'
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    loop .print_loop
    pop dx
    pop cx
    pop bx
    ret

; ============================================================
; Data
; ============================================================

msg_ok:      db 'OK: Timer 1 overflow after ', 0
msg_iters:   db ' iterations', 13, 10, 0
msg_timeout: db 'FAIL: Timeout after ', 0
msg_fail:    db 'FAIL: Status not clear', 13, 10, 0
