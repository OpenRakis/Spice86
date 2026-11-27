use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    mov cx, 1
    call partition_2
    mov cx, 2
    call partition_1
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    mov word [ss:604h], ax
    mov word [ss:608h], cx
    hlt

partition_1:
    dec cx
    jz partition_1_done
    jmp partition_2

partition_1_done:
    ret

partition_2:
    call partition_3
    ret

partition_3:
    jmp partition_1

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh