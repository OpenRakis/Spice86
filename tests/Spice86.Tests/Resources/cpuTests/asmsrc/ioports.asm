; compile it with fasm
use16
start:
    mov ax,0
    mov ss,ax
    mov sp,0x100
    mov ds,ax
    mov es,ax
    cld
    ; byte, immediate port
    mov al,0x5A
    out 0xE0,al
    mov al,0
    in al,0xE0
    mov [0x00],al
    ; word, immediate port
    mov ax,0xA55A
    out 0xE0,ax
    mov ax,0
    in ax,0xE0
    mov [0x02],ax
    ; dword, immediate port
    mov eax,0x12345678
    out 0xE0,eax
    mov eax,0
    in eax,0xE0
    mov [0x04],eax
    ; byte, DX port
    mov dx,0xE0
    mov al,0xC3
    out dx,al
    mov al,0
    in al,dx
    mov [0x08],al
    ; word, DX port
    mov ax,0x3CC3
    out dx,ax
    mov ax,0
    in ax,dx
    mov [0x0A],ax
    ; dword, DX port
    mov eax,0x87654321
    out dx,eax
    mov eax,0
    in eax,dx
    mov [0x0C],eax
    ; insb, insw, insd: read the latch into ES:DI
    mov al,0x11
    out dx,al
    mov di,0x10
    insb
    mov ax,0x2B1A
    out dx,ax
    mov di,0x12
    insw
    mov eax,0x0D0C0B0A
    out dx,eax
    mov di,0x14
    insd
    mov al,0xEE
    out dx,al
    mov di,0x18
    mov cx,4
    rep insb
    ; outsb, outsw, outsd: write DS:SI to the latch, read it back with in
    mov byte [0x80],0x9A
    mov si,0x80
    outsb
    in al,dx
    mov [0x1C],al
    mov word [0x82],0xB7A6
    mov si,0x82
    outsw
    in ax,dx
    mov [0x1E],ax
    mov dword [0x84],0xF1E2D3C4
    mov si,0x84
    outsd
    in eax,dx
    mov [0x20],eax
    mov byte [0x88],0x01
    mov byte [0x89],0x02
    mov byte [0x8A],0x03
    mov si,0x88
    mov cx,3
    rep outsb
    in al,dx
    mov [0x24],al
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0ffh
