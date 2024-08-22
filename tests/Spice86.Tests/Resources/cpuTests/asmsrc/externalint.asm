;00: 01
; compile it with fasm
use16
start:
mov ax,0
mov ss,ax
mov sp,2
mov dx,0
; setup int 8 handler
mov word[8*4], inthandler
mov word[8*4+2], cs
; setup PIT8254 frequency
MOV AL, 00110110B ; Send setup CW to Counter 0
OUT 43H, AL
MOV AL, 51h ; 1,193,180 / 1000 (1 ms) = ~1193 = 2251h
OUT 40H, AL ; send low byte to counter 0
MOV AL, 22h
OUT 40H, AL ; send high byte to counter 0
; setup PIC to enable external interrupts
MOV AL, 0 ; unmask all interrupts
OUT 21H, AL

; enable ints
sti
mov ecx,0FFFFFFh
waitloopstart:
; add various garbage instructions to test that the cfg graph doenst link the inthandler iret to them
inc ax
mov bx,ax
shr bx,1
loop waitloopstart
; check that int handler was called at least once
cmp dx,word 0
setnz al
movzx ax, al
push ax
hlt

inthandler:
push AX
MOV DX, 1
MOV AL, 20H ; acknowledge int to PIC
OUT 20H, AL
pop AX
iret

; bios entry point at offset fff0
rb 65520-$
jmp start
rb 65535-$
db 0ffh
