; compile it with fasm
; 386 and 486 instructions that no other generated golden contains, each run once with its result stored.
; DS = SS = 2000h. Results at offset 0 (physical 20000h).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx

; bt / bts / btr / btc
    mov dx, 00F0h
    bt dx, 4
    setc byte [bx+00h]
    bts dx, 0
    setc byte [bx+01h]
    btr dx, 4
    setc byte [bx+02h]
    btc dx, 8
    setc byte [bx+03h]
    mov [bx+04h], dx

; bsf / bsr
    mov dx, 0A00h
    bsf cx, dx
    mov [bx+06h], cx
    bsr cx, dx
    mov [bx+08h], cx

; shld / shrd
    mov ax, 1234h
    mov dx, 0ABCDh
    shld ax, dx, 4
    mov [bx+0Ah], ax
    mov ax, 1234h
    shrd ax, dx, 4
    mov [bx+0Ch], ax

; xadd
    mov ax, 5
    mov cx, 7
    xadd ax, cx
    mov [bx+0Eh], ax
    mov [bx+10h], cx

; cmpxchg with equal values, then with different values
    mov ax, 3
    mov cx, 3
    mov dx, 9
    cmpxchg cx, dx
    mov [bx+12h], cx
    mov ax, 4
    cmpxchg cx, dx
    mov [bx+14h], ax

; bswap
    mov edx, 12345678h
    bswap edx
    mov [bx+16h], edx

; enter / leave
    mov bp, 1234h
    mov ax, sp
    enter 4, 0
    mov cx, bp
    sub cx, sp
    leave
    mov dx, sp
    sub dx, ax
    mov [bx+1Ah], cx
    mov [bx+1Ch], dx
    mov [bx+1Eh], bp

; bound with an index in range
    mov word [0E00h], 0
    mov word [0E02h], 10
    mov dx, 5
    bound dx, [0E00h]
    mov word [bx+20h], 1

; movzx / movsx
    mov dl, 85h
    movzx cx, dl
    mov [bx+22h], cx
    movsx cx, dl
    mov [bx+24h], cx
    mov dx, 8001h
    movsx ecx, dx
    mov [bx+26h], ecx
    movzx ecx, dx
    mov [bx+2Ah], ecx

; cwde / cdq
    mov ax, 8000h
    cwde
    mov [bx+2Eh], eax
    mov eax, 80000000h
    cdq
    mov [bx+32h], edx

; pusha / popa
    mov ax, 1
    mov cx, 2
    mov dx, 3
    mov bp, 5
    mov di, 7
    pusha
    mov bp, sp
    mov ax, [bp+6]
    mov [bx+3Ch], ax
    xor ax, ax
    xor cx, cx
    xor dx, dx
    popa
    mov [bx+36h], ax
    mov [bx+38h], cx
    mov [bx+3Ah], dx
    mov [bx+3Eh], bp

; setcc after one cmp (1 - 2)
    mov ax, 1
    mov cx, 2
    cmp ax, cx
    setb byte [bx+40h]
    setae byte [bx+41h]
    setbe byte [bx+42h]
    seta byte [bx+43h]
    setl byte [bx+44h]
    setge byte [bx+45h]
    setle byte [bx+46h]
    setg byte [bx+47h]
    sets byte [bx+48h]
    setns byte [bx+49h]
    seto byte [bx+4Ah]
    setno byte [bx+4Bh]
    setp byte [bx+4Ch]
    setnp byte [bx+4Dh]
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
