; Child process for FCB process isolation test
; This child does nothing except terminate cleanly
; The parent will verify that its FCB files remain open after this child terminates
;
; NASM syntax: nasm -f bin fcbchild.asm -o fcbchild.com

        BITS 16
        ORG 0x100

start:
        ; Child process: just print a marker and exit
        mov     ah, 0x0E
        mov     al, 'X'             ; X marks child execution
        mov     bh, 0
        int     0x10

        ; Exit cleanly
        mov     ax, 0x4C00
        int     0x21
