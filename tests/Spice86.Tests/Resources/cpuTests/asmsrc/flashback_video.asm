use16
rb 04000h - $

start:
cli
mov ax, 01000h
mov ss, ax
mov sp, 0FFFEh
mov ax, 0013h
int 10h
cli

mov dx, 03C4h
mov ax, 0604h
out dx, ax
mov ax, 0101h
out dx, ax

mov dx, 03D4h
mov ax, 2C11h
out dx, ax
mov ax, 3F01h
out dx, ax
mov ax, 3E07h
out dx, ax
mov ax, 4109h
out dx, ax
mov ax, 0BF12h
out dx, ax
mov ax, 2013h
out dx, ax
mov ax, 0014h
out dx, ax
mov ax, 0E317h
out dx, ax
mov ax, 0FF18h
out dx, ax

mov ax, 0A000h
mov es, ax
cld
mov dx, 03C4h
mov ax, 0F02h
out dx, ax
xor di, di
xor ax, ax
mov cx, 08000h
rep stosw

; Each plane carries a different pixel in a row-dependent pattern.
mov bp, 1
plane:
mov ax, bp
mov ah, al
mov al, 2
out dx, ax
xor di, di
xor bx, bx
alternate_row:
mov cx, 64
mov ax, bx
add ax, bp
add al, 65
alternate_column:
stosb
inc al
loop alternate_column
inc bx
cmp bx, 225
jb alternate_row
mov di, 03A00h
xor bx, bx
row:
mov cx, 64
mov ax, bx
add al, 1
add ax, bp
column:
stosb
inc al
loop column
inc bx
cmp bx, 225
jb row
shl bp, 1
cmp bp, 16
jb plane

mov dx, 03D4h
xor ax, ax
mov ds, ax
mov ah, [0503h]
mov al, 0Ch
out dx, ax
mov ax, 000Dh
out dx, ax
mov ah, [0501h]
mov al, 09h
out dx, ax
mov ah, [0502h]
mov al, 18h
out dx, ax
mov ah, [0504h]
mov al, 07h
out dx, ax
mov dx, 03DAh
in al, dx
mov dx, 03C0h
mov al, 30h
out dx, al
mov al, 61h
out dx, al
mov al, 33h
out dx, al
mov al, [0500h]
out dx, al
mov al, 20h
out dx, al
hlt

rb 65520 - $
jmp start
rb 65535 - $
db 0FFh
