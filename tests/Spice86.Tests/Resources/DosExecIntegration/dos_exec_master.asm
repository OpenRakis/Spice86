; Comprehensive DOS EXEC integration test harness
; Exercises load/execute modes, child process return codes, TSR installation,
; environment block presence, memory allocations, overlay loading, and a stub
; "audio" overlay contained in the same executable image.
;
; All status is reported through BIOS INT 10h teletype output so the emulator
; video buffer can be inspected by tests. Each successful milestone appends a
; single character to the screen:
;   S - start banner
;   E - environment block detected and double-null terminated
;   M - INT 21h allocate/free after load succeeds
;   J - child process output (printed by child.com)
;   C - child process exit code verified via INT 21h/4D
;   T - TSR installed via INT 21h/31h and callable at INT 60h
;   L - load-only mode (INT 21h/4B01) produced entry registers
;   O - overlay payload executed after INT 21h/4B03 load
;   A - audio driver stub inside overlay executed
;   V - overlay call returned control to caller
;
; Expected final string in video memory: "SEMJCTLOAV"
;
; NASM syntax, assembled with: nasm -f bin dos_exec_master.asm -o dos_exec_master.com

        BITS 16
        ORG 0x100

PSP_ENV_PTR        EQU 0x2C
TTY_FUNC           EQU 0x0E
TTY_ATTR           EQU 0x07
TTY_PAGE           EQU 0x00

%macro PRINT 1
        mov     al, %1
        call    WriteTty
%endmacro

start:
        ; ensure segment registers point to PSP/code
        push    cs
        pop     ds
        push    ds
        pop     es

        PRINT   'S'

; ---------------------------------------------------------------------------
; Environment block verification: PSP:002Ch holds segment of environment.
; Walk until a double NULL terminator to ensure block is present.
; ---------------------------------------------------------------------------
        mov     bx, [PSP_ENV_PTR]
        cmp     bx, 0
        je      env_fail
        mov     es, bx
        xor     di, di
        mov     cx, 1024                ; guard against malformed env blocks
env_scan:
        cmp     cx, 0
        je      env_fail
        dec     cx
        mov     al, [es:di]
        inc     di
        cmp     al, 0
        jne     env_scan
        cmp     byte [es:di], 0
        jne     env_fail
        push    ds
        pop     es
        PRINT   'E'
        jmp     mem_test
env_fail:
        push    ds
        pop     es
        PRINT   'e'

; ---------------------------------------------------------------------------
; INT 21h/48h allocation after load, immediately freed via 49h.
; ---------------------------------------------------------------------------
mem_test:
        mov     ah, 0x48
        mov     bx, 0x20                ; request 0x20 paragraphs
        int     0x21
        jc      mem_fail
        mov     [allocSeg], ax
        mov     es, ax
        mov     ah, 0x49
        int     0x21
        jc      mem_fail
        push    ds
        pop     es
        PRINT   'M'
        jmp     child_test
mem_fail:
        push    ds
        pop     es
        PRINT   'm'

; ---------------------------------------------------------------------------
; Load and execute CHILD.COM (AL=00). Child prints 'J' and exits with code 0x42.
; Parent validates return code via INT 21h/4D.
; ---------------------------------------------------------------------------
child_test:
        call    InitExecParamBlock
        lea     dx, [childName]
        mov     ax, 0x4B00
        push    cs
        pop     es
        mov     bx, execParam
        int     0x21
        jc      child_fail
        mov     ah, 0x4D
        int     0x21
        cmp     al, 0x42
        jne     child_fail
        PRINT   'C'
        jmp     tsr_test
child_fail:
        PRINT   'c'
        jmp     tsr_test

; ---------------------------------------------------------------------------
; Install TSR (tsr_hook.com) which exposes INT 60h returning AL=0x5A.
; ---------------------------------------------------------------------------
tsr_test:
        call    InitExecParamBlock
        lea     dx, [tsrName]
        mov     ax, 0x4B00
        push    cs
        pop     es
        mov     bx, execParam
        int     0x21
        jc      tsr_fail
        int     0x60
        cmp     al, 0x5A
        jne     tsr_fail
        PRINT   'T'
        jmp     loadonly_test
tsr_fail:
        PRINT   't'
        jmp     loadonly_test

; ---------------------------------------------------------------------------
; Load-only mode against overlay_driver.exe (AL=01) should populate entry regs.
; ---------------------------------------------------------------------------
loadonly_test:
        call    InitExecParamBlock
        lea     dx, [overlayName]
        mov     ax, 0x4B01
        push    cs
        pop     es
        mov     bx, execParam
        int     0x21
        jc      loadonly_fail
        mov     ax, [execParam + 0x12]  ; Initial CS
        cmp     ax, 0
        je      loadonly_fail
        mov     ax, [execParam + 0x14]  ; Initial IP
        cmp     ax, 0
        je      loadonly_fail
        PRINT   'L'
        jmp     overlay_test
loadonly_fail:
        PRINT   'l'
        jmp     overlay_test

; ---------------------------------------------------------------------------
; Overlay load (AL=03) into freshly allocated memory, then far call entry.
; Overlay prints 'O' and 'A' to represent driver + audio overlay.
; ---------------------------------------------------------------------------
overlay_test:
        mov     ah, 0x48
        mov     bx, 0x40                ; paragraphs for overlay target
        int     0x21
        jc      overlay_fail
        mov     [overlaySeg], ax
        mov     word [overlayParam], ax
        mov     word [overlayParam + 2], 0

        lea     dx, [overlayName]
        mov     ax, 0x4B03
        push    cs
        pop     es
        mov     bx, overlayParam
        int     0x21
        jc      overlay_fail

        mov     word [overlayEntry], 0
        mov     ax, [overlaySeg]
        mov     word [overlayEntry + 2], ax
        ; manual far call using RETF so overlay can RETF back here
        push    word overlay_return       ; return IP
        push    cs                   ; return CS
        push    word [overlayEntry + 2] ; dest CS
        push    word [overlayEntry]  ; dest IP
        retf
overlay_return:

        PRINT   'V'
        jmp     done
overlay_fail:
        PRINT   'v'

done:
        hlt

; ---------------------------------------------------------------------------
; Helpers
; ---------------------------------------------------------------------------
InitExecParamBlock:
        mov     word [execParam + 0x00], 0          ; inherit environment
        mov     word [execParam + 0x02], cmdTail
        mov     word [execParam + 0x04], cs
        mov     word [execParam + 0x06], 0xFFFF     ; FCB1 offset ignored
        mov     word [execParam + 0x08], 0xFFFF     ; FCB1 segment ignored
        mov     word [execParam + 0x0A], 0xFFFF     ; FCB2 offset ignored
        mov     word [execParam + 0x0C], 0xFFFF     ; FCB2 segment ignored
        mov     word [execParam + 0x0E], 0
        mov     word [execParam + 0x10], 0
        mov     word [execParam + 0x12], 0
        mov     word [execParam + 0x14], 0
        ret

WriteTty:
        mov     ah, TTY_FUNC
        mov     bh, TTY_PAGE
        mov     bl, TTY_ATTR
        int     0x10
        ret

; ---------------------------------------------------------------------------
; Data
; ---------------------------------------------------------------------------
allocSeg        dw 0
overlaySeg      dw 0
overlayEntry    dd 0

execParam       times 0x16 db 0
overlayParam    dw 0, 0

cmdTail:
        db 0                       ; length = 0
        db 0x0D                    ; terminating carriage return

childName       db "CHILD.COM", 0
tsrName         db "TSR_HOOK.COM", 0
overlayName     db "OVERLAY_DRIVER.EXE", 0
