; compile with fasm
; A PIT IRQ0 handler reads AH after writing AL, BH through a branch join, and
; CX in JCXZ after writing CL. The caller sets the inputs in an IF=1 loop.
; DS = SS = 2000h. The handler stores 5Ah, 6Bh and 1 at offsets 40h-42h,
; records one delivery at 50h, then clears EAX/EBX/ECX. Masking IRQ0 prevents
; another timer delivery from replacing the results.
use16

start:
    cli
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:8*4], irq0_handler
    mov [es:8*4+2], cs
; PIT channel 0, mode 3, count 0x100: fires during the delay loop below
    mov al, 00110110b
    out 43h, al
    mov al, 0
    out 40h, al
    mov al, 1
    out 40h, al
; unmask IRQ0
    mov al, 0
    out 21h, al
; long delay with IF set: the pending IRQ fires at a check inside it, while the
; input writes of the current iteration are the live register versions
    mov dx, 8000h
    sti
delay:
    mov eax, 5A00h
    mov ebx, 6B00h
    mov ecx, 0100h
    dec dx
    jnz delay
    cli
    hlt

irq0_handler:
    inc byte [50h]
    mov al, 20h
    out 20h, al
; mask IRQ0 again: the PIT is periodic, and a second delivery after the wipe
; below would store zeros over the observed inputs
    mov al, 1
    out 21h, al
    mov [40h], ah
    cmp byte [80h], 0
    je keep_bh
    mov bh, 0
keep_bh:
    mov [41h], bh
    mov cl, 0
    jcxz zero_cx
    mov byte [42h], 1
    jmp wipe
zero_cx:
    mov byte [42h], 2
wipe:
    xor eax, eax
    xor ebx, ebx
    xor ecx, ecx
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
