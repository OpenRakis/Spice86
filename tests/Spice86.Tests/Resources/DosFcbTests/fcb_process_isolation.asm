; FCB Process Isolation Test
; Tests that child process termination does NOT close parent's FCB files
; DOSBox Staging behavior: FCB handles are tracked per PSP
;
; Test scenario:
; 1. Parent creates and opens an FCB file
; 2. Parent spawns child process via INT 21h/4Bh
; 3. Child terminates
; 4. Parent verifies its FCB file is still open by reading from it
;
; Status reporting via INT 10h teletype:
;   S - Start
;   C - Parent created FCB file
;   O - Parent opened FCB file via FCB
;   W - Parent wrote data to FCB file
;   E - Child process executed and returned
;   R - Parent successfully read from FCB after child termination (SUCCESS!)
;   F - Any failure occurred
;
; Expected success sequence: "SCOWER"
;
; NASM syntax: nasm -f bin fcb_process_isolation.asm -o fcb_process_isolation.com

        BITS 16
        ORG 0x100

%macro PRINT 1
        mov     al, %1
        call    WriteTty
%endmacro

start:
        push    cs
        pop     ds
        push    ds
        pop     es

        ; Shrink memory for child process
        mov     dx, end_of_program
        sub     dx, 0x100
        add     dx, 0x030F
        shr     dx, 4
        mov     bx, dx
        mov     ah, 0x4A
        int     0x21
        jc      fail

        PRINT   'S'

        ; Create test file using FCB
        ; INT 21h AH=16h - Create file via FCB
        mov     dx, fcb_block
        mov     ah, 0x16
        int     0x21
        cmp     al, 0
        jne     fail
        
        PRINT   'C'

        ; Write test data to FCB file
        ; Set DTA to our data buffer
        mov     dx, test_data
        mov     ah, 0x1A            ; Set DTA
        int     0x21

        ; Sequential write via FCB (INT 21h AH=15h)
        mov     dx, fcb_block
        mov     ah, 0x15
        int     0x21
        cmp     al, 0
        jne     fail

        PRINT   'W'

        ; Close FCB file
        mov     dx, fcb_block
        mov     ah, 0x10
        int     0x21

        ; Reopen FCB file for reading
        mov     dx, fcb_block
        mov     ah, 0x0F            ; Open file via FCB
        int     0x21
        cmp     al, 0
        jne     fail

        PRINT   'O'

        ; Execute child process
        mov     dx, child_filename
        mov     bx, exec_params
        mov     ax, 0x4B00          ; EXEC - Load and Execute
        int     0x21
        jc      fail

        PRINT   'E'

        ; Child has terminated - now verify parent's FCB is still usable
        ; Reset DTA for reading
        mov     dx, read_buffer
        mov     ah, 0x1A
        int     0x21

        ; Try to read from FCB file (should succeed if FCB still open)
        mov     dx, fcb_block
        mov     ah, 0x14            ; Sequential read via FCB
        int     0x21
        cmp     al, 0               ; AL=0 means success
        jne     fail

        ; Verify we read the data we wrote
        mov     si, read_buffer
        mov     di, test_data
        mov     cx, 16
        repe    cmpsb
        jne     fail

        PRINT   'R'

        ; Success! Close FCB and exit
        mov     dx, fcb_block
        mov     ah, 0x10
        int     0x21

        ; Delete test file
        mov     dx, fcb_block
        mov     ah, 0x13            ; Delete file via FCB
        int     0x21

        ; Report success via port (like other integration tests)
        mov     dx, 0xE9            ; Bochs debug port
        mov     al, 'P'             ; P for PASS
        out     dx, al

        mov     ax, 0x4C00          ; Exit with code 0
        int     0x21

fail:
        PRINT   'F'
        
        ; Report failure via port
        mov     dx, 0xE9
        mov     al, 'F'
        out     dx, al

        mov     ax, 0x4C01          ; Exit with code 1
        int     0x21

WriteTty:
        push    ax
        push    bx
        mov     ah, 0x0E
        mov     bh, 0
        int     0x10
        pop     bx
        pop     ax
        ret

; FCB structure (37 bytes for standard FCB)
fcb_block:
        db      0                   ; Drive (0=default)
        db      'FCBTEST '          ; Filename (8 bytes, space-padded)
        db      'DAT'               ; Extension (3 bytes)
        db      0, 0                ; Current block
        db      0, 0                ; Record size
        dd      0                   ; File size
        dw      0                   ; Date
        dw      0                   ; Time
        db      8 dup(0)            ; Reserved
        db      0                   ; Current record
        dd      0                   ; Random record

test_data:
        db      'FCB TEST DATA!!', 128-16 dup(0)

read_buffer:
        times 128 db 0

child_filename:
        db      'FCBCHILD.COM', 0

; EXEC parameter block
exec_params:
        dw      0                   ; Environment segment (0=inherit)
        dw      0, 0                ; Command line pointer (empty)
        dw      0, 0                ; FCB1 pointer
        dw      0, 0                ; FCB2 pointer

end_of_program:
