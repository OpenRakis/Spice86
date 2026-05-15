; Build: fasm cds_initialized_known_location.asm cds_initialized_known_location.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Get List of Lists (SysVars) pointer via INT 21h/52h
    mov ah, 52h
    int 21h
    ; ES:BX now points to the SysVars (List of Lists)

    ; CDS pointer is a 32-bit far pointer at offset 0x16 from SysVars
    les si, [es:bx+16h]    ; Load CDS far pointer (offset into SI, segment into ES)
    push es
    pop ds                 ; DS = CDS segment

    ; DS:SI now points to the first CDS entry
    ; Verify it contains "C:\"
    lodsb
    cmp al, 'C'
    jne failed
    lodsb
    cmp al, ':'
    jne failed
    lodsb
    cmp al, '\'
    jne failed
    lodsb
    cmp al, 0
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
