; Build: fasm idiom_relaxation_base.asm ../idiom_relaxation_base.bin
; unknown-memory-is-RAM: an access to the VGA framebuffer segment, memory that is not
; known to be RAM. Strict mode treats it as device memory; the relaxation treats it as RAM.
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 0xA000
    mov es, ax
    mov byte [es:0x0010], 0x42
    mov al, [es:0x0010]
    mov byte [0x0601], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
