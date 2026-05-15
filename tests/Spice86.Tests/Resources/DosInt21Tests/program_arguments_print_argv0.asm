; Build: fasm program_arguments_print_argv0.asm program_arguments_print_argv0.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Read argv[0] from the environment block and print its first character through AH=02h.
    push cs
    pop ds
    call find_program_path
    cmp word [es:di], 1
    jne failed
    add di, 2
    mov dl, [es:di]
    push cs
    pop ds
    mov ah, 02h
    int 21h

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

get_environment_segment:
    mov ah, 62h
    int 21h
    mov es, bx
    mov ax, [es:002Ch]
    test ax, ax
    jz failed
    mov es, ax
    ret

find_program_path:
    call get_environment_segment
    xor di, di
.find_double_null:
    cmp byte [es:di], 0
    jne .next_char
    cmp byte [es:di+1], 0
    je .found
.next_char:
    inc di
    jmp .find_double_null
.found:
    add di, 2
    ret
