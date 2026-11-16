; compile it with fasm
; One o32 near call and one o32 far call to two leaf functions that return with the
; 32-bit return form: typed signatures carry the operand width like the distance, so
; these callees become typed exactly like the 16-bit ones and the typed helpers push
; and pop the dword frames. near_leaf computes EAX from ECX and EDX and ends with
; o32 ret; far_leaf reads a pushed dword argument at [ss:bp+8] (the o32 far frame is
; 8 bytes) into EAX and ends with o32 retf 4.
; fasm has no o32 form of call/ret, so the 66h-prefixed encodings are written out.
; DS = SS = 2000h, SP = 1000h. Results go to offsets 40h-47h (physical 20040h-20047h);
; SP and SS are stored before hlt so the oracle checks the stack balance.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

    mov ecx, 11112222h
    mov edx, 00003333h
    db 66h, 0E8h            ; o32 call near_leaf (rel32)
    dd near_leaf - $ - 4
    mov [40h], eax

    mov ecx, 00000007h
    push dword 0ABCD1234h
    db 66h, 09Ah            ; o32 call 0F000h:far_leaf (ptr32:16)
    dd far_leaf
    dw 0F000h
    mov [44h], eax

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

near_leaf:
    mov eax, ecx
    add eax, edx
    db 66h, 0C3h            ; o32 ret

far_leaf:
    mov bp, sp
    mov eax, dword [ss:bp+8]
    add eax, ecx
    db 66h, 0CAh            ; o32 retf 4
    dw 4

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
