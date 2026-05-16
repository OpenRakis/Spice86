; compile it with fasm
; Test: STI/CLI block boundary detection.
;
; STI is a Block_Terminator and CLI is a Block_Starter, so the linker must:
;   - End the block containing STI on STI itself.
;   - Start a new block at the mov ax, 0x1234 that follows STI.
;   - End that middle block before CLI (since CLI is a Block_Starter).
;   - Open a third block whose entry is CLI.
;
; Flow:
;   mov ax, 0x1000   ; setup DS
;   mov ds, ax
;   sti              ; Block_Terminator — ends first block
;   mov ax, 0x1234   ; middle block (single instruction)
;   cli              ; Block_Starter — starts third block
;   mov [si], ax     ; writes 0x1234 to DS:SI (1000:0000)
;   hlt              ; stop
use16

start:
    mov ax, 0x1000
    mov ds, ax
    sti
    mov ax, 0x1234
    cli
    mov [si], ax
    hlt

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
