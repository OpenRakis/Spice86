; compile it with fasm
; Conditional jumps and ALU operations whose flags are or are not needed afterwards.
; The cases run once per input pair (AX = a, CX = b, four pairs in the "inputs" table).
; Results are at DS = 0: pass p writes byte k at offset 0x40 * p + k.
;   Jump cases write 1 when the jump is taken and 2 when it falls through.
;   Value cases write the value they compute.
; DS:0x0100..0x0107 hold copies of the inputs: b, a, a, b.
use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor ax, ax
    mov ds, ax
    mov es, ax
    cld
    mov bx, 0
    mov si, inputs
    mov bp, 4

pass_loop:
    mov ax, [cs:si]
    mov cx, [cs:si+2]
    mov [0x0100], cx
    mov [0x0102], ax
    mov [0x0104], ax
    mov [0x0106], cx

; cmp followed by each relational jump
    mov byte [bx+0x00], 1
    cmp ax, cx
    jb c00_end
    mov byte [bx+0x00], 2
c00_end:
    mov byte [bx+0x01], 1
    cmp ax, cx
    jae c01_end
    mov byte [bx+0x01], 2
c01_end:
    mov byte [bx+0x02], 1
    cmp ax, cx
    je c02_end
    mov byte [bx+0x02], 2
c02_end:
    mov byte [bx+0x03], 1
    cmp ax, cx
    jne c03_end
    mov byte [bx+0x03], 2
c03_end:
    mov byte [bx+0x04], 1
    cmp ax, cx
    jbe c04_end
    mov byte [bx+0x04], 2
c04_end:
    mov byte [bx+0x05], 1
    cmp ax, cx
    ja c05_end
    mov byte [bx+0x05], 2
c05_end:
    mov byte [bx+0x06], 1
    cmp ax, cx
    jl c06_end
    mov byte [bx+0x06], 2
c06_end:
    mov byte [bx+0x07], 1
    cmp ax, cx
    jge c07_end
    mov byte [bx+0x07], 2
c07_end:
    mov byte [bx+0x08], 1
    cmp ax, cx
    jle c08_end
    mov byte [bx+0x08], 2
c08_end:
    mov byte [bx+0x09], 1
    cmp ax, cx
    jg c09_end
    mov byte [bx+0x09], 2
c09_end:

; cmp with an immediate operand
    mov byte [bx+0x0A], 1
    cmp ax, 2
    jl c0A_end
    mov byte [bx+0x0A], 2
c0A_end:

; cmp with byte operands
    mov byte [bx+0x0B], 1
    cmp al, cl
    jb c0B_end
    mov byte [bx+0x0B], 2
c0B_end:

; test of two registers
    mov byte [bx+0x0C], 1
    test ax, cx
    jz c0C_end
    mov byte [bx+0x0C], 2
c0C_end:

; test of a register with itself
    mov byte [bx+0x0D], 1
    mov dx, ax
    dec dx
    test dx, dx
    jnz c0D_end
    mov byte [bx+0x0D], 2
c0D_end:

; test with an immediate mask
    mov byte [bx+0x0E], 1
    test ah, 0x80
    jnz c0E_end
    mov byte [bx+0x0E], 2
c0E_end:

; cmp with a memory operand, the jump right after it
    mov byte [bx+0x0F], 1
    cmp ax, [0x0100]
    jb c0F_end
    mov byte [bx+0x0F], 2
c0F_end:

; jump on the result of an ALU operation on a register
    mov byte [bx+0x10], 1
    mov dx, ax
    dec dx
    jz c10_end
    mov byte [bx+0x10], 2
c10_end:
    mov byte [bx+0x11], 1
    mov dx, ax
    sub dx, cx
    js c11_end
    mov byte [bx+0x11], 2
c11_end:
    mov byte [bx+0x12], 1
    mov dx, ax
    and dx, cx
    jnz c12_end
    mov byte [bx+0x12], 2
c12_end:
    mov byte [bx+0x13], 1
    mov dx, ax
    xor dx, cx
    jz c13_end
    mov byte [bx+0x13], 2
c13_end:

; jump on the sign of a cmp and of a test
    mov byte [bx+0x14], 1
    cmp ax, cx
    js c14_end
    mov byte [bx+0x14], 2
c14_end:
    mov byte [bx+0x15], 1
    test ax, ax
    js c15_end
    mov byte [bx+0x15], 2
c15_end:

; jump on the sign of an incremented register
    mov byte [bx+0x16], 1
    mov dx, ax
    inc dx
    jns c16_end
    mov byte [bx+0x16], 2
c16_end:

; jump on the result of an ALU operation on memory
    mov byte [bx+0x17], 1
    dec word [0x0102]
    jz c17_end
    mov byte [bx+0x17], 2
c17_end:

; an instruction between the cmp and the jump, touching neither the flags nor the operands
    mov byte [bx+0x18], 1
    cmp ax, cx
    mov dx, ax
    jb c18_end
    mov byte [bx+0x18], 2
c18_end:

; inc between the cmp and the jump: inc does not write the carry flag
    mov byte [bx+0x19], 1
    cmp ax, cx
    inc dx
    jb c19_end
    mov byte [bx+0x19], 2
c19_end:

; an instruction between an ALU operation and the jump on its result
    mov byte [bx+0x1A], 1
    mov dx, ax
    sub dx, cx
    mov di, ax
    jz c1A_end
    mov byte [bx+0x1A], 2
c1A_end:

; the carry flag of the cmp is read again after the jump
    mov byte [bx+0x1B], 1
    xor dx, dx
    cmp ax, cx
    jb c1B_end
    mov byte [bx+0x1B], 2
c1B_end:
    adc dl, 0
    mov [bx+0x1C], dl

; jumps on flags that have no equivalent relation
    mov byte [bx+0x1D], 1
    cmp ax, cx
    jo c1D_end
    mov byte [bx+0x1D], 2
c1D_end:
    mov byte [bx+0x1E], 1
    cmp ax, cx
    jp c1E_end
    mov byte [bx+0x1E], 2
c1E_end:

; shift by CL: no flag is written when CL is 0, so the zero flag of the cmp survives
    mov byte [bx+0x1F], 1
    mov dx, ax
    cmp ax, ax
    shl dx, cl
    jz c1F_end
    mov byte [bx+0x1F], 2
c1F_end:

; an instruction in between writes the low byte of the compared register
    mov byte [bx+0x20], 1
    mov dx, ax
    cmp dx, cx
    mov dl, 0x55
    jb c20_end
    mov byte [bx+0x20], 2
c20_end:

; an instruction in between writes the high byte of the compared register
    mov byte [bx+0x21], 1
    mov dx, ax
    cmp dx, cx
    mov dh, 0
    jb c21_end
    mov byte [bx+0x21], 2
c21_end:

; instructions in between read the flags
    mov byte [bx+0x22], 1
    cmp ax, cx
    pushf
    pop di
    jb c22_end
    mov byte [bx+0x22], 2
c22_end:

; a memory operand with an instruction in between
    mov byte [bx+0x23], 1
    cmp ax, [0x0100]
    mov dx, ax
    jb c23_end
    mov byte [bx+0x23], 2
c23_end:

; a push in between changes the compared stack pointer
    mov byte [bx+0x24], 1
    mov dx, ax
    add dx, 0x00FF
    cmp sp, dx
    push dx
    jz c24_end
    mov byte [bx+0x24], 2
c24_end:
    pop dx

; sti in between
    mov byte [bx+0x25], 1
    cmp ax, cx
    sti
    jb c25_end
    mov byte [bx+0x25], 2
c25_end:
    cli

; the flags come from a called function
    mov byte [bx+0x26], 1
    call c26_function
    jb c26_end
    mov byte [bx+0x26], 2
c26_end:

; the jump is the first instruction of a called function
    mov byte [bx+0x27], 1
    cmp ax, cx
    call c27_function

; the jump is also reached from another place, with the flags of another cmp
    xor di, di
    mov byte [bx+0x28], 1
    cmp ax, cx
c28_jump:
    jb c28_end
    mov byte [bx+di+0x28], 2
c28_end:
    inc di
    cmp di, 2
    jae c28_done
    mov byte [bx+0x29], 1
    cmp cx, ax
    jmp c28_jump
c28_done:

; rep string instructions
    push cx
    push si
    mov byte [bx+0x2B], 1
    mov si, 0x0104
    mov di, 0x0106
    mov cx, 2
    repe cmpsb
    jne c2B_end
    mov byte [bx+0x2B], 2
c2B_end:
    mov byte [bx+0x2D], 1
    mov di, 0x0106
    mov cx, 2
    repne scasb
    mov [bx+0x2C], cl
    jz c2D_end
    mov byte [bx+0x2D], 2
c2D_end:
    mov si, 0x0104
    mov di, 0x0106
    mov cx, 2
    repe cmpsb
    mov [bx+0x2A], cl
    pop si
    pop cx

; ALU operations whose flags are overwritten before any read
    mov dx, ax
    add dx, cx
    mov [bx+0x2E], dx
    mov dx, ax
    sub dx, cx
    mov [bx+0x30], dx
    mov dx, ax
    inc dx
    mov [bx+0x32], dx
    mov dx, cx
    dec dx
    mov [bx+0x34], dx
    mov dx, ax
    neg dx
    mov [bx+0x36], dx
    mov dx, ax
    or dx, cx
    mov [bx+0x38], dx
    mov [bx+0x3A], al
    add byte [bx+0x3A], 5

; only the carry flag of an add is read afterwards
    mov di, ax
    add di, ax
    mov dx, 0
    adc dl, 0
    mov [bx+0x3B], dl

; two increments of the same register
    mov dx, ax
    inc dx
    inc dx
    mov [bx+0x3C], dl

; cmp and test whose flags are never read
    cmp ax, cx
    test ax, ax
    mov byte [bx+0x3D], 7

; cmp with a memory operand whose flags are never read
    cmp ax, [0x0100]
    mov byte [bx+0x3E], 9

    mov dx, bp
    mov [bx+0x3F], dl
    add bx, 0x40
    add si, 4
    dec bp
    jnz pass_loop
    hlt

c26_function:
    cmp ax, cx
    ret

c27_function:
    jb c27_taken
    mov byte [bx+0x27], 2
c27_taken:
    ret

inputs:
    dw 0x0001, 0x0002
    dw 0x0002, 0x0002
    dw 0x8000, 0x0001
    dw 0x0001, 0x8000

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
