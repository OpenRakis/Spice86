; Simple test to print a character
        BITS 16
        ORG 0x100

start:
        mov     ah, 0x0E
        mov     al, 'X'
        mov     bh, 0
        mov     bl, 0x07
        int     0x10
        
        mov     ax, 0x4C00
        int     0x21
