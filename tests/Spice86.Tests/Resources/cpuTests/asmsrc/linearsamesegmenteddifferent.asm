; Expected value in ram first segment is 02
; This test goes through one instruction (A near jump) that is reached with different segmented addresses.
; The jump is supposed to go to 2 different addresses depending on CS:IP. Those are manipulated via stack + far ret. 
; Test makes used of gate A20 to jump after F000:FFFF, so gate should be enabled by config before running it, and is to load as a bios file.

use16
jumptarget1:;F0000
; register jump and load cs:ip with FFEF:000D
mov ax,1
push 0ffefh
push 0000dh
retf
start:
; setup stack
mov sp,1000h
mov bx,1000h
mov ss,bx
; ax contains current test index
mov ax,0
; load cs:ip with F000:FEFD
push 0f000h
push 0fefdh
retf
hlt

; near jump here is at linear address FFF00-3 (F000:FF00-3 =F000:FEFD or FFEF:0010-3 = FFEF:000D which are the same)
; if we jump 100
;  - if we are jumping from F000:FF00, resulting address will be F000:0000 (F0000, segment start, label jumptarget1)
;  - if we are jumping from FFEF:0010, resulting address will be FFEF:0110 (100000, outside of segment start, need to enable A20, label jumptarget2)
rb 65277-$;FEFDh
doubleaddressjump:
db 0e9h,00h,01h
rb 65520-$;FFF0
jmp start
rb 65535-$;FFFF
db 0ffh
rb 65536-$;10000
jumptarget2:
mov ax,2
testend:
mov word[0],ax
hlt