; compile it with fasm
; Store forwarding and expression propagation must keep the value a new reader needs:
; EAX is loaded from a RAM input the oracle knows, stored to known RAM, reloaded into EBX,
; stored to the result region, then EAX and EBX are fully overwritten.
; DS = SS = 2000h, SP = 1000h. The result dword is DS:0100 (physical 20100h).
; Initial RAM (fixture-side ConfigureMachine): DS:0800 = 12345678h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    mov eax, [0800h]
    mov [0808h], eax
    mov ebx, [0808h]
    mov [0100h], ebx
    mov eax, 0A5A5A5A5h
    mov ebx, 05A5A5A5Ah
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
