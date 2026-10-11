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
mov ax, 0A000h
mov es, ax
xor di, di
xor bx, bx
cld
row:
mov cx, 320
mov ax, bx
column:
stosb
inc al
loop column
inc bx
cmp bx, 200
jb row
hlt

rb 65520 - $
jmp start
rb 65535 - $
db 0FFh
