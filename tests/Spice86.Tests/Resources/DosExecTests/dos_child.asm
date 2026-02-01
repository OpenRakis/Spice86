; Child process for EXEC integration tests.
; Prints 'J' via INT 10h teletype then exits with code 0x42.

        BITS 16
        ORG 0x100

        mov     ah, 0x0E
        mov     al, 'J'
        mov     bh, 0
        mov     bl, 0x07
        int     0x10

        mov     ax, 0x4C42          ; exit with AL=0x42
        int     0x21
