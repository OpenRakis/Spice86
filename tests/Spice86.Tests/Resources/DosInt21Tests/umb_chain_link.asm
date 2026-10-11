; Build from WSL: nasm -f bin umb_chain_link.asm -o umb_chain_link.com
bits 16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    mov ax, 0040h
    mov es, ax
    cmp word [es:0013h], 0280h
    jne failed

    mov ax, 5200h
    int 21h
    mov dx, [es:bx - 2]
    cmp dx, 015Fh
    jne failed
    cmp word [es:bx + 68h], dx
    jne failed
    mov es, dx
    cmp byte [es:0], 4Dh
    je sysvars_mcb_is_valid
    cmp byte [es:0], 5Ah
    jne failed

sysvars_mcb_is_valid:

    mov ax, 5200h
    int 21h
    cmp word [es:bx + 66h], 9FFFh
    jne failed
    test byte [es:bx + 63h], 01h
    jnz failed

    mov ax, 9FFFh
    mov es, ax
    cmp byte [es:0], 4Dh
    jne failed
    cmp word [es:1], 0008h
    jne failed
    cmp word [es:3], 3000h
    jne failed
    cmp byte [es:8], 'S'
    jne failed
    cmp byte [es:9], 'C'
    jne failed
    cmp byte [es:15], ' '
    jne failed

    mov ax, 0D000h
    mov es, ax
    cmp byte [es:0], 5Ah
    jne failed
    cmp word [es:1], 0000h
    jne failed
    cmp word [es:3], 1FFFh
    jne failed

    mov ax, 5800h
    int 21h
    cmp ax, 0000h
    jne failed

    mov ax, 5802h
    int 21h
    jc failed
    cmp al, 00h
    jne failed

    mov ax, 5803h
    mov bx, 0001h
    int 21h
    jc failed

    mov ax, 5200h
    int 21h
    mov ax, [es:bx - 2]
    mov es, ax
    mov cx, 20h

find_umb_bridge:
    mov ax, es
    cmp ax, 9FFFh
    je found_umb_bridge
    cmp byte [es:0], 4Dh
    jne failed
    mov dx, es
    add dx, [es:3]
    inc dx
    mov es, dx
    loop find_umb_bridge
    jmp failed

found_umb_bridge:
    cmp byte [es:0], 4Dh
    jne failed

    mov ax, 5802h
    int 21h
    jc failed
    cmp al, 01h
    jne failed

    mov ax, 5803h
    xor bx, bx
    int 21h
    jc failed

    mov ax, 5802h
    int 21h
    jc failed
    cmp al, 00h
    jne failed

    mov al, success
    jmp report

failed:
    mov al, failure

report:
    mov dx, result_port
    out dx, al
    hlt