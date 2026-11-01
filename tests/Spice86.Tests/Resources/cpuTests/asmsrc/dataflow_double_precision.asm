; compile it with fasm
; 32-bit arithmetic on the DX:AX register pair: add / adc, sub / sbb, neg / adc / neg, shl / rcl and shr / rcr,
; with carries and borrows across the two halves.
; DS = SS = 2000h. Pass p writes its results at offset 40h * p (physical 20000h + 40h * p).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx
    mov si, inputs
    mov word [0F00h], 4
; inputs for the read sections: known RAM below 0A0000h at 300h and 400h-406h
    mov word [0300h], 1234h
    mov word [0302h], 5678h
    mov dword [0400h], 33221100h
    mov word [0404h], 5544h
    mov word [0406h], 0066h
; data for the over-bound section: FFFC..FFFF, so each word read stays inside the segment
; limit and keeps its own effective offset
    mov dword [0FFFCh], 0A5A55A5Ah

pass_loop:
; add / adc
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov cx, [cs:si+4]
    mov di, [cs:si+6]
    add ax, cx
    adc dx, di
    mov [bx+00h], ax
    mov [bx+02h], dx

; sub / sbb
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    sub ax, cx
    sbb dx, di
    mov [bx+04h], ax
    mov [bx+06h], dx

; neg / adc / neg
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    neg ax
    adc dx, 0
    neg dx
    mov [bx+08h], ax
    mov [bx+0Ah], dx

; shl / rcl
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    shl ax, 1
    rcl dx, 1
    mov [bx+0Ch], ax
    mov [bx+0Eh], dx

; shr / rcr
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    shr dx, 1
    rcr ax, 1
    mov [bx+10h], ax
    mov [bx+12h], dx

; 8-bit halves: add al,cl / adc ah,ch is the 16-bit add of AX and CX
    mov ax, [cs:si]
    mov cx, [cs:si+4]
    add al, cl
    adc ah, ch
    mov [bx+14h], ax

; 32-bit halves: EDX:EAX + EDI:ECX
    mov eax, [cs:si]
    mov edx, [cs:si+4]
    mov ecx, [cs:si+4]
    mov edi, [cs:si]
    add eax, ecx
    adc edx, edi
    mov [bx+16h], eax
    mov [bx+1Ah], edx

; DP-4: the high half reads the low result (adc dx,ax), stays two statements
    mov ax, [cs:si+6]
    mov dx, [cs:si+2]
    mov cx, [cs:si+4]
    add ax, cx
    adc dx, ax
    mov [bx+1Eh], ax
    mov [bx+20h], dx

; DP-1: the carry out of adc is live, stays two statements
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov di, [cs:si+6]
    add ax, cx
    adc dx, di
    setc byte [bx+22h]
    mov [bx+23h], dx

; DP-1: the carry out of the last neg is live, stays three statements
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    neg ax
    adc dx, 0
    neg dx
    setc byte [bx+25h]
    mov [bx+26h], dx

; two adjacent DS word reads: the double-precision arithmetic reads each word separately
    mov di, [0F00h]
    and di, 3
    mov ax, [di+400h]
    mov dx, [di+402h]
    mov cx, [cs:si+4]
    mov bp, [cs:si+6]
    add ax, cx
    adc dx, bp
    mov [bx+28h], ax
    mov [bx+2Ah], dx

; segment-end reads: the low offset reaches FFFC..FFFEh, so every word access fits
; the 64 KB segment limit and each word keeps its own effective offset
    mov di, [0F00h]
    and di, 2
    or di, 0FFFCh
    mov ax, [di]
    mov dx, [di+2]
    mov cx, [cs:si+4]
    mov bp, [cs:si+6]
    add ax, cx
    adc dx, bp
    mov [bx+2Ch], ax
    mov [bx+2Eh], dx

; two CS word reads: ordinary fixture bytes, not a device-side-effect oracle
    mov di, [0F00h]
    and di, 3
    mov ax, [cs:di+inputs]
    mov dx, [cs:di+inputs+2]
    mov cx, [cs:si+4]
    mov bp, [cs:si+6]
    add ax, cx
    adc dx, bp
    mov [bx+34h], ax
    mov [bx+36h], dx

; a store between the two reads touches another address: a disjoint store does not prove alias refusal
    mov di, 0300h
    mov ax, [di]
    mov [0308h], ax
    mov dx, [di+2]
    mov cx, [cs:si+4]
    mov di, [cs:si+6]
    add ax, cx
    adc dx, di
    mov [bx+30h], ax
    mov [bx+32h], dx

    add bx, 40h
    add si, 8
    dec word [0F00h]
    jnz pass_loop
    hlt

; per pass: X, Y (32 bits each)
inputs:
    dd 0000FFFFh, 00000001h
    dd 00010000h, 00000001h
    dd 80000000h, 0FFFFFFFFh
    dd 12348000h, 0000FFFFh

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
