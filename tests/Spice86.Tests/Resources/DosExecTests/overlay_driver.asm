; Overlay executable used for EXEC AL=03 load tests.
; Minimal MZ header with no relocations, entry at CS:IP = 0:0000.
; When invoked it prints 'O' then 'A' via INT 10h teletype and returns with RETF,
; modelling an audio driver overlay shipped alongside the main executable.

        BITS 16
        ORG 0

%define HEADER_PARAS 4

overlay_header:
        dw      0x5A4D                              ; "MZ" signature
        dw      (overlay_end - $$) % 512            ; bytes on last page
        dw      ((overlay_end - $$) + 511) / 512    ; pages in file
        dw      0                                   ; relocations
        dw      HEADER_PARAS                        ; header size in paragraphs
        dw      0x10                                ; min alloc - request minimal extra memory
        dw      0x10                                ; max alloc - request minimal extra memory
        dw      0                                   ; initial SS (relative)
        dw      0xFFFE                              ; initial SP
        dw      0                                   ; checksum
        dw      0                                   ; initial IP
        dw      0                                   ; initial CS (relative)
        dw      0x1C                                ; relocation table offset
        dw      0                                   ; overlay number

        times   HEADER_PARAS * 16 - ($ - $$) db 0   ; pad header to paragraph boundary

; Program image starts here (offset 0 after header is stripped by loader)
overlay_code:
        push    cs
        pop     ds
        mov     ah, 0x0E
        mov     bh, 0
        mov     bl, 0x07
        mov     al, 'O'
        int     0x10
        mov     al, 'A'
        int     0x10
        retf

overlay_end:
