; Build: fasm environment_block_contains_program_path.asm environment_block_contains_program_path.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Program path is stored after environment variables: double null, word count, ASCIZ path.
    call find_program_path
    cmp word [es:di], 1
    jne failed
    add di, 2
    cmp byte [es:di], 'C'
    jne failed
    cmp byte [es:di+1], ':'
    jne failed
    cmp byte [es:di+2], '\'
    jne failed

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
