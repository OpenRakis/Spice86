; Build: nasm -f bin find_first_wildcard.asm -o find_first_wildcard.com
bits 16
org 100h

result_port  equ 0999h
details_port equ 0998h
success      equ 00h
failure      equ 0FFh

start:
    ; Set DTA to dta_buffer
    mov ah, 1Ah
    mov dx, dta_buffer
    int 21h

    mov si, test_cases
    mov cx, 0           ; test index counter

test_loop:
    mov bx, [si]        ; pattern ptr
    test bx, bx
    jz all_passed

    push cx
    push si

    mov dx, bx          ; DS:DX = pattern
    mov cx, 0           ; normal file attribute
    mov ah, 4Eh         ; FindFirst
    int 21h

    pop si
    pop cx

    jc no_match_found

match_found:
    cmp byte [si + 2], 0
    jne test_failed
    jmp next_test

no_match_found:
    cmp byte [si + 2], 1
    jne test_failed

next_test:
    inc cx
    add si, 3
    jmp test_loop

test_failed:
    mov al, cl          ; write 0-based test index to details port
    mov dx, details_port
    out dx, al
    mov al, failure
    jmp report

all_passed:
    mov al, success

report:
    mov dx, result_port
    out dx, al
    hlt

; Patterns and expected results (0 = match, 1 = no match)
test_cases:
    dw pat1
    db 0
    dw pat2
    db 0
    dw pat3
    db 0
    dw pat4
    db 0
    dw pat5
    db 1
    dw pat6
    db 0
    dw pat7
    db 0
    dw pat8
    db 0
    dw pat9
    db 0
    dw pat10
    db 0
    dw pat11
    db 0
    dw pat12
    db 0
    dw pat13
    db 0
    dw pat14
    db 0
    dw pat15
    db 0
    dw pat16
    db 0
    dw pat17
    db 0
    dw pat18
    db 0
    dw pat19
    db 0
    dw pat20
    db 0
    dw pat21
    db 0
    dw pat22
    db 0
    dw pat23
    db 1
    dw pat24
    db 1
    dw pat25
    db 0
    dw 0

pat1   db 'README.TXT', 0
pat2   db 'readme.txt', 0
pat3   db 'README', 0
pat4   db 'ReadMe.TxT', 0
pat5   db 'README.MD', 0
pat6   db '?.TXT', 0
pat7   db 'A?.TXT', 0
pat8   db 'A?', 0
pat9   db 'AB???.TXT', 0
pat10  db 'FILE.E??', 0
pat11  db 'FILE.???', 0
pat12  db 'FILE.?X?', 0
pat13  db 'READ*.TXT', 0
pat14  db 'READ*', 0
pat15  db '*.TXT', 0
pat16  db '*.*', 0
pat17  db '*.', 0
pat18  db 'FILE.*', 0
pat19  db 'FILE.T*', 0
pat20  db '*.T*', 0
pat21  db 'FOO', 0
pat22  db 'MY*.C?M', 0
pat23  db 'MY*.TET', 0
pat24  db 'NONEXIST.*', 0
pat25  db 'HIGHSCOR.DAT', 0

dta_buffer times 128 db 0
