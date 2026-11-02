; Build: fasm overlay_exe_image_with_relocation.asm overlay_exe_image_with_relocation.bin
; Minimal MZ executable image with one relocation entry, used by the DOS overlay loader
; test. The test loads the image at a load segment of its choice and checks that the word
; at relocation_target_offset inside the load image gained the relocation factor rather
; than the load segment.
; Parameters: relocation_target_offset is the offset of the relocated word inside the load
; image, initial_word_value is the word stored there before relocation.
format binary

relocation_target_offset equ 0h
initial_word_value       equ 42h

    db 'MZ'                    ; signature
    dw 0                       ; bytes in last page
    dw 1                       ; pages
    dw 1                       ; relocations
    dw 2                       ; header paragraphs
    dw 0                       ; min extra paragraphs
    dw 0                       ; max extra paragraphs
    dw 0                       ; SS
    dw 0                       ; SP
    dw 0                       ; checksum
    dw 0                       ; IP
    dw 0                       ; CS
    dw 1Ch                     ; relocation table offset
    dw 0                       ; overlay number
    dw relocation_target_offset ; relocation entry: offset
    dw 0                       ; relocation entry: segment
    times 20h - $ db 0         ; the load image starts at 20h
    times relocation_target_offset db 0 ; the word sits at 20h + relocation_target_offset
    dw initial_word_value      ; word the relocation entry points at
    times 512 - $ db 0         ; pad the image to one 512-byte page
