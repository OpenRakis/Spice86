; TENTACLE.EXE overlay loader integration test
; -------------------------------------------
; Mirrors a common overlay pattern where the executable uses its own name
; (from the environment block) to derive an overlay filename with extension
; ".000", then invokes INT 21h / 4B01 (load but do not execute). Success
; is reported by printing 'K' via BIOS TTY; failure prints 'k'. Execution
; must resume after the load-only call.
;
; Assembled as a COM and distributed as TENTACLE.EXE for test purposes.
;
; nasm -f bin tentacle.asm -o tentacle.exe

        BITS 16
        ORG 0x100

PSP_ENV_PTR    EQU 0x2C
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

        mov     bx, [PSP_ENV_PTR]
        cmp     bx, 0
        je      load_fail

        mov     es, bx
        xor     di, di
        mov     cx, 2048

find_env_path:
        cmp     cx, 0
        je      load_fail
        dec     cx
        mov     al, [es:di]
        inc     di
        cmp     al, 0
        jne     find_env_path
        cmp     byte [es:di], 0
        jne     find_env_path
        inc     di                     ; skip second null
        add     di, 2                  ; skip WORD count (DOS 3.0+ environment format)
        mov     si, di                ; SI -> program path
        lea     di, [pathBuf]

copy_path:
        mov     al, [es:si]
        mov     [di], al
        inc     si
        inc     di
        cmp     al, 0
        jne     copy_path

        ; replace extension with .000 (or append it)
        mov     di, pathBuf
find_end:
        mov     al, [di]
        cmp     al, 0
        je      search_dot
        inc     di
        jmp     find_end

search_dot:
        cmp     di, pathBuf
        je      append_ext
        dec     di
        mov     al, [di]
        cmp     al, '.'
        je      write_ext
        jmp     search_dot

append_ext:
        ; no dot found, extend at current end (DI points to null)
write_ext:
        mov     byte [di], '.'
        mov     byte [di + 1], '0'
        mov     byte [di + 2], '0'
        mov     byte [di + 3], '0'
        mov     byte [di + 4], 0

        ; set up exec parameter block (inherit environment)
        mov     word [execParam + 0x00], 0
        mov     word [execParam + 0x02], cmdTail
        mov     word [execParam + 0x04], cs
        mov     word [execParam + 0x06], 0xFFFF
        mov     word [execParam + 0x08], 0xFFFF
        mov     word [execParam + 0x0A], 0xFFFF
        mov     word [execParam + 0x0C], 0xFFFF
        mov     word [execParam + 0x0E], 0
        mov     word [execParam + 0x10], 0
        mov     word [execParam + 0x12], 0
        mov     word [execParam + 0x14], 0

        lea     dx, [pathBuf]
        push    cs
        pop     es
        mov     bx, execParam
        mov     ax, 0x4B01
        int     0x21
        jc      load_fail

        PRINT   'K'
        jmp     done

load_fail:
        PRINT   'k'

done:
        hlt

WriteTty:
        mov     ah, TTY_FUNC
        mov     bh, TTY_PAGE
        mov     bl, TTY_ATTR
        int     0x10
        ret

cmdTail:        db 0, 0x0D
pathBuf:        times 128 db 0
execParam:      times 0x16 db 0
