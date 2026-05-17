; compile it with fasm
; Tests CPU fault (div by 0) where the fault handler modifies the return
; address to retry from the block entry, creating a CpuFault edge from the
; div instruction.
;
; Expected memory at 00:
;   00: 03 00 - retry count (handler was called 3 times)
;   02: 02 00 - final AX result from the successful division (10 / 5 = 2)
;
use16
start:
mov ax,0
mov ss,ax
mov sp,200h

; set up INT 0 (divide error) handler
mov word[0*4], divhandler
mov word[0*4+2], cs

; retrycount at CS:0x100, divisor at CS:0x102
mov word[cs:0x100], 0
mov word[cs:0x102], 0

; --- Faulting block ---
; The div will fault (divisor=0). INT 0 handler adjusts the return IP
; on the stack to point to divblock (the mov ax) so the CPU re-executes
; the whole sequence with BX reloaded from memory.
divblock:
mov ax,10
mov bx,word[cs:0x102]
div bx

; Store results
mov bx,word[cs:0x100]
mov word[0x00],bx
mov word[0x02],ax

hlt

; INT 0 handler: increment retrycount, on 3rd call set divisor to 5,
; and adjust return IP to divblock so the mov bx reload happens.
divhandler:
push bp
mov bp,sp
push bx
mov bx,word[cs:0x100]
inc bx
mov word[cs:0x100],bx
cmp bx,3
jne .skip
mov word[cs:0x102],5
.skip:
; Adjust return IP on stack to divblock entry (mov ax instruction).
; Stack layout: [BP+0]=old BP, [BP+2]=return IP, [BP+4]=return CS, [BP+6]=flags
mov word[bp+2], divblock
pop bx
pop bp
iret

; bios entry point at offset fff0
rb 65520-$
jmp start
rb 65535-$
db 0ffh
