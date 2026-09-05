; Build: nasm -f bin find_first_short_filename.asm -o find_first_short_filename.com
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
    mov bx, [si]        ; search pattern ptr
    test bx, bx
    jz all_passed

    push cx
    push si

    mov al, [si + 4]    ; mode: 0 = FindFirst, 1 = FindNext
    test al, al
    jnz do_find_next

do_find_first:
    mov dx, bx          ; DS:DX = pattern
    mov cx, 0           ; normal file attribute
    mov ah, 4Eh         ; FindFirst
    int 21h
    jmp find_done

do_find_next:
    mov ah, 4Fh         ; FindNext
    int 21h

find_done:
    pop si
    pop cx

    jc find_failed      ; If FindFirst/FindNext failed

    mov di, [si + 2]    ; expected SFN string ptr
    mov bx, dta_buffer + 1Eh ; BX points to DTA filename

compare_loop:
    mov al, [bx]
    mov dl, [di]
    cmp al, dl
    jne string_mismatch
    test al, al
    jz compare_ok
    inc bx
    inc di
    jmp compare_loop

string_mismatch:
    mov al, cl          ; bit 7 clear = string mismatch at test index CL
    jmp report_failure

find_failed:
    mov al, cl
    or al, 80h          ; bit 7 set = find failed

report_failure:
    mov dx, details_port
    out dx, al
    mov al, failure
    jmp report

compare_ok:
    inc cx
    add si, 5           ; 5 bytes per entry (dw, dw, db)
    jmp test_loop

all_passed:
    mov al, success

report:
    mov dx, result_port
    out dx, al
    hlt

; Table of (pattern_ptr, expected_sfn_ptr, is_find_next_flag)
test_cases:
    dw pat1, exp1
    db 0                ; FindFirst README.TXT -> README.TXT
    dw pat2, exp2
    db 0                ; FindFirst VeryLongFileName.txt -> VERYLO~1.TXT
    dw pat3, exp3
    db 0                ; FindFirst LONG*.DOC -> LONGDO~1.DOC
    dw pat3, exp4
    db 1                ; FindNext -> LONGDO~2.DOC
    dw pat3, exp5
    db 1                ; FindNext -> LONGDO~3.DOC
    dw pat6, exp6
    db 0                ; FindFirst readme.text -> README~1.TEX
    dw pat7, exp7
    db 0                ; FindFirst My File.txt -> MYFILE~1.TXT
    dw pat8, exp8
    db 0                ; FindFirst VeryLongFilenameNoExt -> VERYLO~1
    dw pat9, exp9
    db 0                ; FindFirst ABCDEFGH.TXT -> ABCDEFGH.TXT
    dw pat10, exp10
    db 0                ; FindFirst ABCDEFGHI.TXT -> ABCDEF~1.TXT
    dw 0, 0
    db 0                ; Sentinel end of table

pat1  db 'README.TXT', 0
exp1  db 'README.TXT', 0

pat2  db 'VeryLongFileName.txt', 0
exp2  db 'VERYLO~1.TXT', 0

pat3  db 'LONG*.DOC', 0
exp3  db 'LONGDO~1.DOC', 0
exp4  db 'LONGDO~2.DOC', 0
exp5  db 'LONGDO~3.DOC', 0

pat6  db 'readme.text', 0
exp6  db 'README~1.TEX', 0

pat7  db 'My File.txt', 0
exp7  db 'MYFILE~1.TXT', 0

pat8  db 'VeryLongFilenameNoExt', 0
exp8  db 'VERYLO~2', 0

pat9  db 'ABCDEFGH.TXT', 0
exp9  db 'ABCDEFGH.TXT', 0

pat10 db 'XYZDEFGHI.TXT', 0
exp10 db 'XYZDEF~1.TXT', 0

dta_buffer times 128 db 0
