; compile it with fasm
; Speculative catalogue-entry fixture: the symbols file names functions the run
; never calls.
;
;   cat_pos (F000:0100): valid leaf code. Seeded from the catalogue at image
;       load and emitted as a speculative method. Nothing executes it.
;   cat_neg (F000:0120): invalid bytes at image load, so its catalogue
;       candidate is dropped without poisoning. The program patches real code
;       over it at startup; the observed jnz names it as its unexecuted arm,
;       which the unobserved run takes - proving the dropped entry left the
;       address explorable. The jnz sits behind `jmp bx` so speculative decode
;       of its arm happens after the patch, not at image load.
;
; Selector byte at [0x0500]: 0x00 = discovery run, 0x01 = unobserved run.
;
; Expected memory:
;   [0x0400] = 0x01
;   [0x0401] = 0x42 (discovery run only)
;   [0x0402] = 0x77 (unobserved run only: cat_neg marker)

use16

start:
    mov byte [0x0400], 0x01
    mov byte [0x0401], 0x00
    mov byte [0x0402], 0x00

    ; patch cat_neg (0x0120) into: mov byte [0x0402],0x77 ; jmp done
    mov byte [cs:0x0120], 0xC6
    mov byte [cs:0x0121], 0x06
    mov word [cs:0x0122], 0x0402
    mov byte [cs:0x0124], 0x77
    mov byte [cs:0x0125], 0xE9
    mov word [cs:0x0126], done - 0x0128

    ; reach the selector check indirectly so its block - and the jnz's
    ; cat_neg arm - decode only once the patch already ran.
    mov bx, dispatch
    jmp bx

dispatch:
    mov al, [0x0500]
    test al, al
    jnz 0x0120
    mov byte [0x0401], 0x42

done:
    hlt

rb 0x0100-$
cat_pos:
    mov ax, 0x1234
    ret

rb 0x0120-$
cat_neg:
    db 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF

rb 65520-$
jmp start
rb 65535-$
db 0ffh
