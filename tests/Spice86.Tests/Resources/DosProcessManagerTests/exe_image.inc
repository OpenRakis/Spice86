; Minimal MZ executable image for the DOS process manager tests: a four paragraph header,
; then INT 21h AH=4Ch in the load image at offset 40h, padded to one 512-byte page. The
; test loads the image and checks the allocation DOS gives the child against the min and
; max extra paragraph fields of the header.
; Parameters: min_extra_paragraphs and max_extra_paragraphs are the MZ header fields stored
; at header offsets 0Ah and 0Ch.
format binary

    db 'MZ'                    ; signature
    dw 0                       ; bytes in last page
    dw 1                       ; pages
    dw 0                       ; relocations
    dw 4                       ; header paragraphs
    dw min_extra_paragraphs    ; min extra paragraphs
    dw max_extra_paragraphs    ; max extra paragraphs
    dw 0                       ; SS
    dw 0FFFEh                  ; SP
    dw 0                       ; checksum
    dw 0                       ; IP
    dw 0                       ; CS
    dw 40h                     ; relocation table offset
    dw 0                       ; overlay number
    times 40h - $ db 0         ; pad up to the four paragraph header
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
    times 512 - $ db 0         ; pad the image to one 512-byte page
