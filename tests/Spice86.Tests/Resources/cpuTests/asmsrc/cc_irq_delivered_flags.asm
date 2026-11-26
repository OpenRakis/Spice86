; compile with fasm
; A program-installed IRQ 12 handler observes the FLAGS image the delivery pushed.
; The program pokes the int 74h vector at one of two handlers (the variant flag at
; 0000h:0500h selects which), sets a dead flag pattern (ZF = 1 through sub, then
; CF = 1 through stc: dead work generated code drops while no event check reads
; the flags), then spins until the delivery the machine posted before the run.
;   flag clear - frame_reader: push bp; mov bp,sp; mov ax,[bp+6] reads the pushed
;     FLAGS word (IP at +0, CS at +2, FLAGS at +4), keeps CF|ZF and stores it.
;   flag 1     - flag_branch: jnc on the delivered CF selects the stored word.
;   flag 2     - flag_patcher: or word [bp+6],1 sets CF in the pushed image; the
;     interrupted code cleared CF before sti and stores 1 when the patched CF
;     reaches its post-delivery jc.
;   flag 3     - sti then hlt: the delivery the wait hlt takes counts one, then a
;     cli; hlt pair ends the run.
; Result region (DS = SS = 2000h): 0080h = masked FLAGS image, 0082h = arm,
; 0084h = patched-arm word, 0086h = delivery count.
use16
result = 0x0080
arm = 0x0082
patched = 0x0084
deliveries = 0x0086

start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov ax, cs
    cmp byte [es:0500h], 0
    jne .not_reader
    mov word [es:01D0h], frame_reader
    jmp .vector_set
.not_reader:
    cmp byte [es:0500h], 1
    jne .not_branch
    mov word [es:01D0h], flag_branch
    jmp .vector_set
.not_branch:
    cmp byte [es:0500h], 3
    je hlt_path
    jmp patch_path
.vector_set:
    mov word [es:01D2h], ax
    mov bx, 1
    sub bx, 1       ; ZF = 1, dead: nothing in the program reads it
    stc             ; CF = 1
    sti
    mov cx, 0400h
spin:
    loop spin
    cli
    hlt
patch_path:
    mov word [es:01D0h], flag_patcher
    mov word [es:01D2h], ax
    clc             ; CF = 0 when the delivery hits; the patch sets it in the image
    sti
    mov cx, 0400h
pspin:
    loop pspin
    cli
    mov ax, 0
    jnc .store
    mov ax, 1
.store:
    mov [patched], ax
    hlt

hlt_path:
    mov word [es:01D0h], delivery_counter
    mov word [es:01D2h], ax
    sti
    hlt             ; waits for the posted IRQ 12; the delivery counts one
    cli
    hlt             ; interrupts disabled: ends the run

frame_reader:
    push bp
    mov bp, sp
    mov ax, [bp+6]
    and ax, 0041h
    push ds
    mov dx, 2000h
    mov ds, dx
    mov [result], ax
    pop ds
    pop bp
    mov al, 20h     ; non-specific EOI, slave then master
    out 0A0h, al
    out 20h, al
    iret

flag_branch:
    mov ax, 0
    jnc .store
    mov ax, 1
.store:
    push ds
    push bx
    mov bx, 2000h
    mov ds, bx
    mov [arm], ax
    pop bx
    pop ds
    mov al, 20h
    out 0A0h, al
    out 20h, al
    iret

flag_patcher:
    push bp
    mov bp, sp
    or word [bp+6], 0001h   ; sets CF in the FLAGS image iret restores
    pop bp
    mov al, 20h
    out 0A0h, al
    out 20h, al
    iret

delivery_counter:
    push ds
    push ax
    mov ax, 2000h
    mov ds, ax
    inc word [deliveries]
    pop ax
    pop ds
    mov al, 20h
    out 0A0h, al
    out 20h, al
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
