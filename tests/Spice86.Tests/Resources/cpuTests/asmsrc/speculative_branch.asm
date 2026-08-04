; Speculative CFG test fixture T1: simple conditional branch.
;
; Selector byte at memory address 0x0500 (above IVT and BDA) determines branch direction.
; Discovery run: byte at [0x0500] = 0x00 -> AL=0, test sets ZF=1, JNZ not taken -> fallthrough
; Speculative path: byte at [0x0500] = 0x01 -> AL=1, test sets ZF=0, JNZ taken -> alt_path
;
; Expected memory layout after execution:
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = branch result: 0xDD (discovery/fallthrough) or 0xEE (speculative/alt_path)
;   [0x0402] = 0xAA (done marker)

use16

start:
    mov byte [0x0400], 0x01

    ; Read selector byte from well-above IVT/BDA area
    mov al, byte [0x0500]
    test al, al
    jnz alt_path

    ; Discovery path: selector=0 -> JNZ not taken, falls through here
    mov byte [0x0401], 0xDD
    jmp done

alt_path:
    ; Speculative path: selector=1 -> JNZ taken
    mov byte [0x0401], 0xEE
    jmp done

done:
    mov byte [0x0402], 0xAA
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
