; Build: fasm child_opens_files_without_closing.asm child_opens_files_without_closing.com
; Child program for child_exec_terminate_releases_handles: it inherits the standard handles, opens three
; files (expecting handles 5, 6 and 7) and exits without closing them. Exit code 0 on success, 1 otherwise.
use16
org 100h

start:
    mov dx, file_a
    mov cx, 5
open_loop:
    mov ax, 3D00h
    int 21h
    jc failed
    cmp ax, cx
    jne failed
    inc cx
    cmp cx, 8
    jne open_loop

    mov ax, 4C00h
    int 21h

failed:
    mov ax, 4C01h
    int 21h

file_a db 'A.TXT', 0
