; Minimal TSR used to validate INT 21h/31h stay-resident behavior.
; Installs INT 60h handler that returns AL=0x5A, then stays resident.

        BITS 16
        ORG 0x100

        push    cs
        pop     ds

        mov     dx, handler
        mov     ax, 0x2560          ; set interrupt vector 60h
        int     0x21

        mov     dx, (handler_end - 0x100 + 15) / 16
        mov     ax, 0x3100          ; terminate and stay resident
        int     0x21

handler:
        mov     al, 0x5A
        iret
handler_end:
