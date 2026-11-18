; ega_ril_write_register.asm - INT 10h AH=F1h EGA Raster Image Library test
;
; INT 10h AH=F1h writes one EGA RIL register: DX selects the controller
; (0x00 CRT, 0x08 sequencer, 0x10 graphics, 0x18 attribute), BL is the
; register and BH the value. Each write is verified by reading the
; register back through the controller's index/data ports, the same way
; real EGA software probes the registers after a RIL call. An unknown
; selection (DX=0xFF) must have no effect and must not trap.
;
; Results (reported via ports 0x999/0x998 for Spice86):
;   Port 0x998: number of the failing test, or the total test count on success
;   Port 0x999: 0x00 on success, 0xFF on failure
;
; Assemble:  nasm -f bin -o ega_ril_write_register.com ega_ril_write_register.asm

[bits 16]
[org 0x100]

RESULT_PORT equ 0x999
DETAIL_PORT equ 0x998

start:
    ; --- Test 1: graphics controller register 0x03 = 0x05 ---
    mov ax, 0xF100
    mov dx, 0x10
    mov bl, 0x03
    mov bh, 0x05
    int 0x10
    mov dx, 0x3CE
    mov al, 0x03
    out dx, al
    inc dx                ; 0x3CF
    in al, dx
    cmp al, 0x05
    jne .fail1
    jmp .t2

.fail1:
    mov al, 1
    jmp .fail

    ; --- Test 2: sequencer register 0x04 = 0x03 ---
.t2:
    mov ax, 0xF100
    mov dx, 0x08
    mov bl, 0x04
    mov bh, 0x03
    int 0x10
    mov dx, 0x3C4
    mov al, 0x04
    out dx, al
    inc dx                ; 0x3C5
    in al, dx
    cmp al, 0x03
    jne .fail2
    jmp .t3

.fail2:
    mov al, 2
    jmp .fail

    ; --- Test 3: CRT controller register 0x12 = 0x40 ---
.t3:
    mov ax, 0xF100
    mov dx, 0x00
    mov bl, 0x12
    mov bh, 0x40
    int 0x10
    mov dx, 0x3CC
    in al, dx
    mov dx, 0x3B4         ; default CRT base
    test al, 1
    jz .crt_base_3b4
    mov dx, 0x3D4
.crt_base_3b4:
    mov al, 0x12
    out dx, al
    inc dx
    in al, dx
    cmp al, 0x40
    jne .fail3
    jmp .t4

.fail3:
    mov al, 3
    jmp .fail

    ; --- Test 4: attribute controller register 0x02 = 0x11 ---
.t4:
    mov ax, 0xF100
    mov dx, 0x18
    mov bl, 0x02
    mov bh, 0x11
    int 0x10
    mov dx, 0x3BA
    in al, dx             ; input status 1
    mov dx, 0x3C0
    mov al, 0x02
    out dx, al
    inc dx                ; 0x3C1
    in al, dx
    cmp al, 0x11
    jne .fail4
    jmp .t5

.fail4:
    mov al, 4
    jmp .fail

    ; --- Test 5: an unknown selection must not trap ---
.t5:
    mov ax, 0xF100
    mov dx, 0xFF
    mov bl, 0x01
    mov bh, 0x02
    int 0x10

    ; --- Success: all five checks completed ---
    mov dx, DETAIL_PORT
    mov al, 5
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0x00
    out dx, al
    jmp .exit

.fail:
    mov dx, DETAIL_PORT
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0xFF
    out dx, al

.exit:
    int 0x20
