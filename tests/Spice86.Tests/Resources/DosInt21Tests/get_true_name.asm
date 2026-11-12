; Build: fasm get_true_name.asm get_true_name.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh

; INT 21h AH=60h get true name: DS:SI -> ASCIIZ source path, ES:DI -> ASCIIZ destination.
; C: is the current drive and its current directory is the root.
; Each check writes one detail byte: 0 when the destination buffer equals the
; expected ASCIIZ string, index+1 of the first differing byte on mismatch, the
; DOS error code (AL of AX) of an expected error, 0FFh when an error case did not fail.
; The empty path must fail with error 2 (file not found) and the unmounted Y: drive
; with error 15 (invalid drive); the bootstrap Z: memory drive is mounted and valid.

start:
    ; case 1: relative file name
    mov si, src1
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jnc case1_ok
    mov al, al
    mov byte [bad], 1
    mov dx, details_port
    out dx, al
    jmp next1
case1_ok:
    xor cx, cx
    mov si, dest_buffer
    mov di, exp1
case1_loop:
    lodsb
    cmp al, [di]
    jne case1_bad
    inc di
    test al, al
    jz case1_good
    jmp case1_loop
case1_good:
    xor cx, cx
    jmp case1_report
case1_bad:
    mov byte [bad], 1
    inc cx
case1_report:
    mov al, cl
    mov dx, details_port
    out dx, al
next1:
    ; case 2: drive-qualified file name in the current directory
    mov si, src2
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jnc case2_ok
    mov al, al
    mov byte [bad], 1
    mov dx, details_port
    out dx, al
    jmp next2
case2_ok:
    xor cx, cx
    mov si, dest_buffer
    mov di, exp2
case2_loop:
    lodsb
    cmp al, [di]
    jne case2_bad
    inc di
    test al, al
    jz case2_good
    jmp case2_loop
case2_good:
    xor cx, cx
    jmp case2_report
case2_bad:
    mov byte [bad], 1
    inc cx
case2_report:
    mov al, cl
    mov dx, details_port
    out dx, al
next2:
    ; case 3: relative subdirectory with back slashes
    mov si, src3
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jnc case3_ok
    mov al, al
    mov byte [bad], 1
    mov dx, details_port
    out dx, al
    jmp next3
case3_ok:
    xor cx, cx
    mov si, dest_buffer
    mov di, exp3
case3_loop:
    lodsb
    cmp al, [di]
    jne case3_bad
    inc di
    test al, al
    jz case3_good
    jmp case3_loop
case3_good:
    xor cx, cx
    jmp case3_report
case3_bad:
    mov byte [bad], 1
    inc cx
case3_report:
    mov al, cl
    mov dx, details_port
    out dx, al
next3:
    ; case 4: relative subdirectory with forward slashes
    mov si, src4
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jnc case4_ok
    mov al, al
    mov byte [bad], 1
    mov dx, details_port
    out dx, al
    jmp next4
case4_ok:
    xor cx, cx
    mov si, dest_buffer
    mov di, exp4
case4_loop:
    lodsb
    cmp al, [di]
    jne case4_bad
    inc di
    test al, al
    jz case4_good
    jmp case4_loop
case4_good:
    xor cx, cx
    jmp case4_report
case4_bad:
    mov byte [bad], 1
    inc cx
case4_report:
    mov al, cl
    mov dx, details_port
    out dx, al
next4:
    ; case 5: dot-dot step inside the path
    mov si, src5
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jnc case5_ok
    mov al, al
    mov byte [bad], 1
    mov dx, details_port
    out dx, al
    jmp next5
case5_ok:
    xor cx, cx
    mov si, dest_buffer
    mov di, exp5
case5_loop:
    lodsb
    cmp al, [di]
    jne case5_bad
    inc di
    test al, al
    jz case5_good
    jmp case5_loop
case5_good:
    xor cx, cx
    jmp case5_report
case5_bad:
    mov byte [bad], 1
    inc cx
case5_report:
    mov al, cl
    mov dx, details_port
    out dx, al
next5:
    ; case 6: dot-dot step at the root stays at the root
    mov si, src6
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jnc case6_ok
    mov al, al
    mov byte [bad], 1
    mov dx, details_port
    out dx, al
    jmp next6
case6_ok:
    xor cx, cx
    mov si, dest_buffer
    mov di, exp6
case6_loop:
    lodsb
    cmp al, [di]
    jne case6_bad
    inc di
    test al, al
    jz case6_good
    jmp case6_loop
case6_good:
    xor cx, cx
    jmp case6_report
case6_bad:
    mov byte [bad], 1
    inc cx
case6_report:
    mov al, cl
    mov dx, details_port
    out dx, al
next6:
    ; case 7: the current directory itself
    mov si, src7
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jnc case7_ok
    mov al, al
    mov byte [bad], 1
    mov dx, details_port
    out dx, al
    jmp next7
case7_ok:
    xor cx, cx
    mov si, dest_buffer
    mov di, exp7
case7_loop:
    lodsb
    cmp al, [di]
    jne case7_bad
    inc di
    test al, al
    jz case7_good
    jmp case7_loop
case7_good:
    xor cx, cx
    jmp case7_report
case7_bad:
    mov byte [bad], 1
    inc cx
case7_report:
    mov al, cl
    mov dx, details_port
    out dx, al
next7:
    ; case 8: drive-qualified dot-dot steps cannot leave the root
    mov si, src8
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jnc case8_ok
    mov al, al
    mov byte [bad], 1
    mov dx, details_port
    out dx, al
    jmp next8
case8_ok:
    xor cx, cx
    mov si, dest_buffer
    mov di, exp8
case8_loop:
    lodsb
    cmp al, [di]
    jne case8_bad
    inc di
    test al, al
    jz case8_good
    jmp case8_loop
case8_good:
    xor cx, cx
    jmp case8_report
case8_bad:
    mov byte [bad], 1
    inc cx
case8_report:
    mov al, cl
    mov dx, details_port
    out dx, al
next8:
    ; case 9: an empty path must fail with error 2 (file not found)
    mov si, empty_path
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jc case9_ok
    mov byte [bad], 1
    mov al, failure
    jmp case9_out
case9_ok:
    mov al, al
    cmp al, 2
    je case9_out
    mov byte [bad], 1
case9_out:
    mov dx, details_port
    out dx, al
next9:
    ; case 10: an unmounted drive must fail with error 15 (invalid drive)
    mov si, other_drive
    mov di, dest_buffer
    mov ah, 60h
    int 21h
    jc case10_ok
    mov byte [bad], 1
    mov al, failure
    jmp case10_out
case10_ok:
    mov al, al
    cmp al, 15
    je case10_out
    mov byte [bad], 1
case10_out:
    mov dx, details_port
    out dx, al
next10:
    cmp byte [bad], 0
    jne failed
    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

src1 db "FILE.TXT", 00h
exp1 db "C:\FILE.TXT", 00h
src2 db "C:FILE.TXT", 00h
exp2 db "C:\FILE.TXT", 00h
src3 db "SUB\FILE.TXT", 00h
exp3 db "C:\SUB\FILE.TXT", 00h
src4 db "SUB/FILE.TXT", 00h
exp4 db "C:\SUB\FILE.TXT", 00h
src5 db "SUB\..\FILE.TXT", 00h
exp5 db "C:\FILE.TXT", 00h
src6 db "..\FILE.TXT", 00h
exp6 db "C:\FILE.TXT", 00h
src7 db ".", 00h
exp7 db "C:\", 00h
src8 db "C:SUB\..\..\OTHER.TXT", 00h
exp8 db "C:\OTHER.TXT", 00h
empty_path db 00h
other_drive db "Y:FILE.TXT", 00h
dest_buffer db 128 dup(0)
bad db 0
