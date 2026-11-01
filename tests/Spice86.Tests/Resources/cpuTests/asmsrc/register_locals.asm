; compile it with fasm
; Register-local promotion traps. Each section produces a result byte/word at ds:40h.. that a
; wrong promotion corrupts: a missing write-back (the local holds the value the register reads
; still need), a byte read treated as a whole-register one, a compound assignment that picks the
; wrong version, a swap, a pointer segment. DS = SS = 2000h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:61h*4], store_ax_iret
    mov [es:61h*4+2], cs
    cld
    mov di, 40h
    mov si, inputs

; L1: a web dead after its last explicit use takes no write-back
    mov ax, [cs:si]
    mov cx, ax
    mov dx, ax
    mov [di+00h], cx
    mov [di+0Ch], dx

; L2: a called subroutine reads the register after the last explicit use
    mov ax, [cs:si+2]
    mov cx, ax
    int 61h
    mov [di+0Eh], cx

; L3: a byte read of the defined register cannot print the local
    mov ax, [cs:si+4]
    mov cx, ax
    mov [di+04h], al
    mov [di+10h], cx

; L4: the add's left operand is the version still in the register
    mov ax, [cs:si+6]
    add ax, 1
    mov [di+05h], ax

; L5: a swap right after the last explicit use reads both registers
    mov ax, [cs:si+8]
    mov cx, ax
    mov bx, [cs:si+10]
    xchg ax, bx
    mov [di+07h], ax
    mov [di+12h], cx
    mov [di+16h], bx

; L6: a byte read before a division needs the register back first
    mov ax, [cs:si+12]
    mov cx, ax
    mov [di+14h], al
    xor dx, dx
    div cx
    mov [di+09h], ax

; L7: a segment register used as a pointer segment is implicit. Prepare 3000:0080 = 77h and
; 2000:0080 = 11h, then read through the last loaded DS web.
    mov ax, [cs:si+16]
    mov ds, ax
    mov dx, ds
    mov byte [0080h], 77h
    mov ax, [cs:si+14]
    mov ds, ax
    mov byte [0080h], 11h
    mov ax, [cs:si+16]
    mov ds, ax
    mov dx, ds
    mov cx, [0080h]
    mov ax, [cs:si+14]
    mov ds, ax
    mov [di+0Bh], cl

; L8: a full overwrite kills the web, so no write-back stands between the two loads
    mov ax, [cs:si]
    mov [di+18h], ax
    mov ax, [cs:si+6]
    mov [di+1Ah], ax

; R1: the pops end the region's webs and restore the saved values; a write-back after them would
; overwrite the restored ax or es with the region's local
    push ax
    push es
    mov ax, 1
    mov es, ax
    mov [di+1Ch], ax
    pop es
    pop ax
    mov [di+1Eh], ax
    mov [di+20h], es

; T1: a web whose reads vote signed keeps its unsigned reads exact: 0FFF0h is -16 signed but
; 65520 unsigned, so a signed local must still give the unsigned comparison, the shr and the
; byte stores the register's type
    movsx ax, byte [cs:si+18]
    cmp ax, -5
    jl .belowNegFive
    mov byte [di+22h], 0
    jmp .belowNegFiveDone
.belowNegFive:
    mov byte [di+22h], 1
.belowNegFiveDone:
    jle .notAboveNegFive
    mov byte [di+23h], 0
    jmp .notAboveNegFiveDone
.notAboveNegFive:
    mov byte [di+23h], 1
.notAboveNegFiveDone:
    jg .aboveNegFive
    mov byte [di+24h], 0
    jmp .aboveNegFiveDone
.aboveNegFive:
    mov byte [di+24h], 1
.aboveNegFiveDone:
    jge .notBelowNegFive
    mov byte [di+25h], 0
    jmp .notBelowNegFiveDone
.notBelowNegFive:
    mov byte [di+25h], 1
.notBelowNegFiveDone:
    cmp ax, -20
    jl .belowNegTwenty
    mov byte [di+26h], 0
    jmp .belowNegTwentyDone
.belowNegTwenty:
    mov byte [di+26h], 1
.belowNegTwentyDone:
    jle .notAboveNegTwenty
    mov byte [di+27h], 0
    jmp .notAboveNegTwentyDone
.notAboveNegTwenty:
    mov byte [di+27h], 1
.notAboveNegTwentyDone:
    jg .aboveNegTwenty
    mov byte [di+28h], 0
    jmp .aboveNegTwentyDone
.aboveNegTwenty:
    mov byte [di+28h], 1
.aboveNegTwentyDone:
    cmp ax, 3
    jb .unsignedLow
    mov byte [di+29h], 1
    jmp .unsignedLowDone
.unsignedLow:
    mov byte [di+29h], 0
.unsignedLowDone:
    push ax
    pop bx
    sar bx, 1
    mov [di+2Ah], bx
    push ax
    pop cx
    shr cx, 1
    mov [di+2Ch], cx

    hlt

store_ax_iret:
    mov [di+02h], ax
    iret

inputs:
    dw 1111h, 2222h, 1234h, 4444h, 5555h, 6666h, 0237h, 2000h, 3000h
    db 0F0h

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
