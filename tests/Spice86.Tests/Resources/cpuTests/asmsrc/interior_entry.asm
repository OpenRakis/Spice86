; compile with fasm
use16

hook_count = 0x0400
tail_count = 0x0401
completed = 0x0402
call_mode = 0x0403
target_ss = 0x0404
target_sp = 0x0406
restore_ss = 0x0408
restore_sp = 0x040A
observer_count = 0x040C
bad_stack = 0x040D
observed_ss = 0x040E
observed_sp = 0x0410

start:
    cli
    xor ax, ax
    mov ds, ax
    mov ss, ax
    mov sp, 0x8000
    mov al, 0xFF
    out 0x21, al
    out 0xA1, al

    mov byte [hook_count], 0
    mov byte [tail_count], 0
    mov byte [completed], 0
    mov byte [call_mode], 0
    mov byte [observer_count], 0
    mov byte [bad_stack], 0
    mov word [target_ss], 0
    mov word [target_sp], 0x7FFA
    mov word [restore_ss], 0
    mov word [restore_sp], 0x7FFE
    mov word [observed_ss], 0
    mov word [observed_sp], 0
    mov word [8*4], timer_hook
    mov word [8*4+2], cs

    mov al, 0x30
    out 0x43, al
    mov al, 0xA9
    out 0x40, al
    mov al, 0x04
    out 0x40, al
    mov al, 0xFE
    out 0x21, al
    sti
    nop
wait_first:
    cmp byte [tail_count], 1
    jne wait_first

    cli
    mov word [8*4], timer_tail
    mov al, 0x30
    out 0x43, al
    mov al, 0xA9
    out 0x40, al
    mov al, 0x04
    out 0x40, al
    mov al, 0xFE
    out 0x21, al
    sti
    nop
wait_second:
    cmp byte [tail_count], 2
    jne wait_second

    cli
    mov al, 0xFF
    out 0x21, al
    mov word [8*4], observe_stack
    mov byte [call_mode], 1
    mov word [target_ss], 0x1000
    mov word [target_sp], 0x9000
    mov al, 0x30
    out 0x43, al
    mov al, 0xA9
    out 0x40, al
    mov al, 0x04
    out 0x40, al
    mov al, 0x0A
    out 0x20, al
wait_pending:
    in al, 0x20
    test al, 1
    jz wait_pending

    sti
    nop
    call timer_hook
    cli
    mov byte [completed], 0xA5
    hlt

rb 0x0200-$
timer_hook:
    inc byte [hook_count]
    mov al, 0xFE
    out 0x21, al
    mov ss, [target_ss]
timer_tail:
    mov sp, [target_sp]
    inc byte [tail_count]
    cmp byte [call_mode], 1
    je wait_observer
    push ax
    mov al, 0xFF
    out 0x21, al
    mov al, 0x20
    out 0x20, al
    pop ax
    iret

wait_observer:
    cmp byte [observer_count], 1
    jne wait_observer
    mov ss, [restore_ss]
    mov sp, [restore_sp]
    ret

if timer_tail <> 0x020C
    err
end if

rb 0x0300-$
observe_stack:
    mov [observed_ss], ss
    mov [observed_sp], sp
    push ax
    push bx
    mov ax, [observed_ss]
    cmp ax, 0
    sete bl
    cmp word [observed_sp], 0x7FF8
    sete bh
    and bl, bh
    cmp ax, 0x1000
    sete al
    cmp word [observed_sp], 0x8FFA
    sete ah
    and al, ah
    or al, bl
    xor al, 1
    or byte [bad_stack], al
    inc byte [observer_count]
    mov al, 0xFF
    out 0x21, al
    mov al, 0x20
    out 0x20, al
    pop bx
    pop ax
    iret

rb 65520-$
    jmp start
rb 65535-$
    db 0xFF