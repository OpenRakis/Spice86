; compile it with fasm to a .com program (org 100h)
; Two callees leak the address of a saved stack slot into a register through an
; xchg temporary, and a called writer stores through the leaked address. S1 uses
; the short accumulator form "xchg ax, bp" (opcode 95h): the temp variable holds
; BP, so AX gets the stack address without any statement reading SP or BP
; directly. S2 uses the modrm form with BP as the r operand (87h /r, reg = BP):
; SI gets it the same way. Neither callee writes the saved register itself, so
; under the saved-slots relaxation the only thing keeping the pop honest is the
; escape check: it must see the slot address reaching the writer through the
; temporary. A .com program keeps the DOS loader's SS:SP, so the pop's stack
; read is RAM-proved and the relaxation's fold is observable.
; DS is set to 2000h; results go to offsets 40h-43h (physical 20040h-20043h).
org 100h
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

    mov cx, 1111h
    call callee_xchg_acc
    mov [40h], cx

    mov dx, 2222h
    call callee_xchg_modrm
    mov [42h], dx

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

; S1: pushes cx, points bp at the pushed word, then "xchg ax, bp" gives ax the
; slot address through a temporary. writer_ax stores 0BEEFh into the slot, so
; the pop must see 0BEEFh, not the pushed 1111h.
callee_xchg_acc:
    push cx
    mov bp, sp
    db 95h          ; xchg ax, bp
    call writer_ax
    db 95h          ; xchg ax, bp: bp = slot address, ax = incoming ax
    pop cx
    ret

; S2: the same leak through the modrm form xchg bp, si, with BP as the r operand.
callee_xchg_modrm:
    push dx
    mov bp, sp
    db 87h, 0EEh    ; xchg bp, si (modrm: reg = bp)
    call writer_si
    db 87h, 0EEh    ; xchg bp, si: bp = slot address, si = incoming si
    pop dx
    ret

writer_ax:
    mov bx, ax
    mov word [ss:bx], 0BEEFh
    ret

writer_si:
    mov bx, si
    mov word [ss:bx], 0BEEFh
    ret
