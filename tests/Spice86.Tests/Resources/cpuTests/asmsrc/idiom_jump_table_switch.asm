; Build: fasm idiom_jump_table_switch.asm ../idiom_jump_table_switch.bin
; an indexed jump table lowered to a switch. One indirect jump site executes both table
; entries in a loop, so the switch keeps two cases (a single-case switch simplifies away).
use16

start:
    mov byte [0x0600], 0x01
    mov word [0x0620], 0
loop_start:
    mov bx, [0x0620]
    cmp bx, 2
    jae done
    shl bx, 1
    jmp word [cs:table+bx]
target0:
    mov byte [0x0601], 0xD0
    jmp next
target1:
    mov byte [0x0602], 0xD1
    jmp next
next:
    inc word [0x0620]
    jmp loop_start
done:
    hlt
table:
    dw target0
    dw target1

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
