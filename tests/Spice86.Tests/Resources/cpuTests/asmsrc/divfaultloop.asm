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
; initial registers are zero
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
; the whole sequence with the divisor reloaded from memory.
; mov ax,0xDEAD writes a value dead on every path: the resume target
; (this same block) overwrites EAX entirely, the fault path's handler and
; resume inputs do not read AX, and the normal path reads the quotient.
; The fault-path analysis removes the write. mov bp is the block entry -
; entry statements are never dropped - and writes a value only the fault
; handler reads (it stores BP): the oracle checks it saw 0x5678.
divblock:
mov bp,05678h
mov ax,0xDEAD
mov eax,10
div word[cs:0x102]

; Store results
mov bx,word[cs:0x100]
mov word[0x00],bx
mov word[0x02],ax

hlt

; INT 0 handler: increment retrycount, on 3rd call set divisor to 5,
; and adjust return IP to divblock so the mov bx reload happens.
divhandler:
; Prove the handler saw the BP the faulting block wrote.
; CX is dead (the faulting block's mov cx is never read) so the counter
; lives in memory and no scratch register needs saving.
mov word[0x08],bp
push bp
mov bp,sp
inc word[cs:0x100]
cmp word[cs:0x100],3
jne .skip
mov word[cs:0x102],5
.skip:
; Adjust return IP on stack to divblock entry (mov ax instruction).
; Stack layout: [BP+0]=old BP, [BP+2]=return IP, [BP+4]=return CS, [BP+6]=flags
mov word[bp+2], divblock
pop bp
iret

; bios entry point at offset fff0
rb 65520-$
jmp start
rb 65535-$
db 0ffh
