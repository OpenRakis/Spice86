; Speculative CFG test fixture: stop speculative decoding at zero-filled memory.
;
; Conditional branches into regions of 00 bytes. A chain of zero instructions marks a
; zero-filled buffer: the explorer must not decode it, so the generated source keeps a
; FailAsUntested for that arm. A single 00 00 followed by real code is still decoded.
; The observed path patches code over a previously zero region and runs it, proving the
; skipped bytes are not remembered.
;
; Selector byte at [0x0500]:
;   0x00 = discovery path: both JNZ not taken -> patch_region is patched and run
;   (the other arms are explored only by the speculative decoder)
;
; Expected memory layout:
;   [0x0400] = 0x01 (init marker)
;   [0x0401] = 0xDD (patch_region ran: mov byte [0x0401], 0xDD)
;   [0x0402] = 0xEE (done marker)

use16

start:
    mov byte [0x0400], 0x01

    mov al, byte [0x0500]
    test al, al
    jnz zero_region
    test al, al
    jnz single_zero

    ; Observed path: write "mov byte [0x0401], 0xDD" (C6 06 01 04 DD) over the zero bytes of
    ; patch_region, then run it. Discovery explored patch_region before the write and must not
    ; have decoded it.
    mov byte [cs:patch_region], 0xC6
    mov byte [cs:patch_region+1], 0x06
    mov byte [cs:patch_region+2], 0x01
    mov byte [cs:patch_region+3], 0x04
    mov byte [cs:patch_region+4], 0xDD
    jmp patch_region

single_zero:
    db 0x00, 0x00
    mov byte [0x0403], 0x77
    jmp done

zero_region:
    db 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    jmp done

patch_region:
    db 0x00, 0x00, 0x00, 0x00, 0x00
done:
    mov byte [0x0402], 0xEE
    hlt

rb 65520-$
jmp start
rb 65535-$
db 0ffh
