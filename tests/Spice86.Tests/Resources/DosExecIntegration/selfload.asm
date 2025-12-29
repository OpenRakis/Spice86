; SELFLOAD.EXE load-only integration test
; -------------------------------------
; Tests INT 21h / 4B01 (load but do not execute) when loading the same
; executable that's currently running. Success is reported by printing 'O'
; via BIOS TTY; failure prints 'o'. Execution must resume after the load-only call.
;
; Assembled as a COM and distributed as SELFLOAD.EXE for test purposes.
;
; nasm -f bin selfload.asm -o selfload.exe

        BITS 16
        ORG 0x100

TTY_FUNC       EQU 0x0E
TTY_ATTR       EQU 0x07
TTY_PAGE       EQU 0x00

%macro PRINT 1
        mov     al, %1
        call    WriteTty
%endmacro

start:
        ; DS = ES = CS
        push    cs
        pop     ds
        push    ds
        pop     es

        ; set up exec parameter block (inherit environment)
        lea     dx, [filename]
        mov     bx, execParam
        mov     ax, 0x4B01              ; INT 21h AH=4B AL=01 (load only)
        int     0x21
        jc      load_fail

        PRINT   'O'
        jmp     done

load_fail:
        PRINT   'o'

done:
        hlt

WriteTty:
        mov     ah, TTY_FUNC
        mov     bh, TTY_PAGE
        mov     bl, TTY_ATTR
        int     0x10
        ret

; ---------------------------------------------------------------------------
; Data
; ---------------------------------------------------------------------------
execParam:
        dw 0                            ; environment segment (0 = inherit)
        dw 0                            ; command tail offset
        dw 0                            ; command tail segment
        dw 0                            ; FCB1 offset
        dw 0                            ; FCB1 segment
        dw 0                            ; FCB2 offset
        dw 0                            ; FCB2 segment
        dw 0                            ; SS after load
        dw 0                            ; SP after load
        dw 0                            ; CS after load
        dw 0                            ; IP after load

filename        db "SELFLOAD.EXE", 0
