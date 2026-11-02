.intel_syntax noprefix
.code16
.section .text

.macro write_reg regno, value
    mov dx, 0x343
    mov al, \regno
    out dx, al
    mov dx, 0x344
    mov ax, \value
    out dx, al
    inc dx
    mov al, ah
    out dx, al
.endm

.macro write_dram_byte address, value
    write_reg 0x43, \address
    write_reg 0x44, 0
    mov dx, 0x347
    mov al, \value
    out dx, al
.endm

start:
    mov dx, 0x999
    mov al, 0xA5
    out dx, al
    write_reg 0x4C, 0
    write_reg 0x4C, 0x0300
    write_reg 0x0E, 0x0D00

    write_dram_byte 0x1000, 0x40
    write_dram_byte 0x1001, 0x20
    write_dram_byte 0x1002, 0x60
    write_dram_byte 0x1003, 0x30
    write_dram_byte 0x2000, 0x00
    write_dram_byte 0x2001, 0x40
    write_dram_byte 0x2002, 0x00
    write_dram_byte 0x2003, 0x20
    write_dram_byte 0x2004, 0x00
    write_dram_byte 0x2005, 0x60
    write_dram_byte 0x2006, 0x00
    write_dram_byte 0x2007, 0x30

    mov dx, 0x342
    mov al, 0
    out dx, al
    write_reg 0x80, 0x0800
    write_reg 0x81, 0xFFFF
    write_reg 0x82, 0x0020
    write_reg 0x83, 0
    write_reg 0x84, 0x0020
    write_reg 0x85, 0x0600
    write_reg 0x86, 0x3F00
    write_reg 0x87, 0xFE00
    write_reg 0x88, 0xFF00
    write_reg 0x89, 0xFE00
    write_reg 0x8A, 0x0020
    write_reg 0x8B, 0
    write_reg 0x8C, 0
    write_reg 0x8D, 0x0800

    mov dx, 0x342
    mov al, 1
    out dx, al
    write_reg 0x80, 0x0C00
    write_reg 0x81, 0xFFFF
    write_reg 0x82, 0x0020
    write_reg 0x83, 0
    write_reg 0x84, 0x0020
    write_reg 0x85, 0x0600
    write_reg 0x86, 0x3F00
    write_reg 0x87, 0xFE00
    write_reg 0x88, 0xFF00
    write_reg 0x89, 0xFE00
    write_reg 0x8A, 0x0020
    write_reg 0x8B, 0
    write_reg 0x8C, 0x0F00
    write_reg 0x8D, 0x0800

    mov dx, 0x999
    mov al, 0x5A
    out dx, al
    mov cx, 5000
delay_loop:
    loop delay_loop

    mov ax, 0x4C00
    int 0x21
