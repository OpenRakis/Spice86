; Build: nasm -f bin fpuinit.asm -o ../fpuinit.bin
bits 16
org 0

control equ 0x0100
stored equ 0x0102

start:
    cli
    mov ax, 0x1000
    mov ds, ax
    mov ss, ax
    mov sp, 0xFFFE

    ; Zortech-style detection must stop at the 8087 interrupt-mask test.
    fninit
    mov cx, 20
init_delay:
    loop init_delay
    fnstcw [control]
    mov cx, 10
store_delay:
    loop store_delay
    xor bx, bx
    mov ax, [control]
    and ah, 0x0F
    cmp ah, 3
    jne failure
    fstcw [control]
    inc bx
    fwait
    and byte [control], 0x7F
    fldcw [control]
    fdisi
    fstcw [control]
    fwait
    test byte [control], 0x80
    jz failure

    ; Load/store must preserve the control word and leave adjacent memory alone,
    ; even with an operand-size prefix.
    mov word [control], 0x0B7F
    mov word [stored + 2], 0xA55A
    o32 fldcw [control]
    o32 fnstcw [stored]
    cmp word [stored], 0x0B7F
    jne failure
    cmp word [stored + 2], 0xA55A
    jne failure
    fndisi
    fnstcw [stored]
    cmp word [stored], 0x0BFF
    jne failure

    ; Segment overrides and 32-bit effective addresses still load a 16-bit word.
    mov ax, 0x2000
    mov es, ax
    mov edi, control
    mov word [es:di], 0x077F
    fldcw [es:edi]
    fnstcw [stored]
    cmp word [stored], 0x077F
    jne failure

    fninit
    fnstcw [stored]
    cmp word [stored], 0x03FF
    jne failure
    fldcw [control]
    finit
    fstcw [stored]
    cmp word [stored], 0x03FF
    jne failure
    mov ax, 0x600D
    hlt

failure:
    mov ax, 0xBAD
    hlt

times 0xFFF0 - ($ - $$) db 0
    jmp start
times 0xFFFF - ($ - $$) db 0
    db 0xFF
