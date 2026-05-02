; tsr_parent_psp_size.asm
; Reproduces the Maupiti Island TSR miscalculation:
;   the game reads parent PSP[0x02] ("top of memory") and uses it as DX.
; In Spice86 the fake COMMAND.COM PSP has CurrentSize = LastFreeSegment (0x9FFF),
; so DX = 0x9FFF which exhausts all conventional memory.
; After the fix, COMMAND.COM PSP[0x02] = 0x70 (just the PSP itself),
; giving DX = 0x70, which leaves nearly all of conventional memory free.
;
; build: nasm -f bin tsr_parent_psp_size.asm -o tsr_parent_psp_size.com

        BITS 16
        ORG 0x100

start:
        ; Set up INT 22h to return here (our HLT) after TSR terminates
        mov     dx, hlt_loc
        mov     ah, 0x25
        mov     al, 0x22
        int     0x21

        ; Get own PSP segment into BX
        mov     ah, 0x62
        int     0x21            ; BX = current PSP segment

        ; Navigate to parent PSP: own PSP[0x16] = parent PSP segment
        mov     es, bx
        mov     es, es:[0x16]   ; ES = parent PSP segment

        ; Read parent PSP[0x02] = "segment of first byte beyond parent's allocation"
        ; In real DOS this is small (COMMAND.COM resident size).
        ; In Spice86 (buggy) this is 0x9FFF = entire conventional memory.
        mov     dx, es:[0x02]   ; DX = parent PSP[0x02]

        ; INT 21h/31h: Terminate and Stay Resident
        ;   AL = return code 0, DX = paragraphs to keep
        mov     ax, 0x3100
        int     0x21

hlt_loc:
        hlt
