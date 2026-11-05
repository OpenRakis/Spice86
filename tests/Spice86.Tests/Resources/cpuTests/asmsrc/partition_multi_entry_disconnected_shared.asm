; Two roots branch into the same two shared entries, and both shared entries branch to the same
; two shared exits. Every shared block is reached from both roots, so the four blocks form one
; synthetic partition with two entries. Neither entry dominates the exits, and the exits do not
; reach each other, so the residual partition left after splitting off the entries is made of
; two unconnected blocks.
use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor bx, bx
    xor cx, cx
    mov al, 0
    call root_a
    mov al, 1
    call root_a
    mov al, 0
    call root_b
    mov al, 1
    call root_b
    hlt

root_a:
    cmp al, 0
    je shared_entry_a
    jmp shared_entry_b

root_b:
    cmp al, 0
    jne shared_entry_a
    jmp shared_entry_b

shared_entry_a:
    cmp al, 0
    je shared_exit_x
    jmp shared_exit_y

shared_entry_b:
    cmp al, 0
    je shared_exit_x
    jmp shared_exit_y

shared_exit_x:
    add bx, 1
    ret

shared_exit_y:
    add cx, 10h
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
