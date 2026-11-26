; compile with fasm
; Software interrupts into a program handler push the caller's FLAGS. The
; handler stores the stacked FLAGS word it finds at [bp+6], so the flag work
; done right before each int must stay exact even though no register read
; after the int sees it (the iret restores the flags from the stack).
; DS = SS = 2000h. Results: 40h-46h = stacked FLAGS of sections 1-4,
; 50h = next result offset.
use16

start:
    cli
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:60h*4], handler
    mov [es:60h*4+2], cs
    mov word [50h], 40h
; section 1: ZF and PF from a subtraction, then CF and DF set explicitly
    mov ax, 1
    sub ax, 1
    stc
    std
    int 60h
    cld
; section 2: CF and SF from a compare whose flags nothing else reads
    mov bx, 5
    cmp bx, 7
    int 60h
; section 3: inside a near call, flags from an add that overflows
    call add_then_int
; section 4: the flags come from the caller; the called method only runs the int
    mov dx, 1
    sub dx, 2
    std
    call int_only
    cld
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

add_then_int:
    mov cx, 7FFFh
    add cx, 1
    int 60h
    ret

int_only:
    int 60h
    ret

handler:
    push bp
    mov bp, sp
    push ax
    push si
    mov si, [50h]
    mov ax, [bp+6]
    mov [si], ax
    add word [50h], 2
    pop si
    pop ax
    pop bp
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
