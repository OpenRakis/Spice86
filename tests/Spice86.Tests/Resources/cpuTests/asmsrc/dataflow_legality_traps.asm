; compile it with fasm
; Sections for the data-flow legality conditions no other fixture traps: a temporary moved past a
; write to a register its initializer reads, an expression merged across a redefined operand, a
; memory read moved past a store, a merge whose target address reads the merged register, a flag
; setter dropped while a later pushf still reads its flags, a masked shift with a live flag, a flag
; setter feeding a jump past a call, an les/lds pair whose segment read uses the written register,
; and a nested test combined across a goto target.
; Second half: a memory-reading temporary inside xadd, a register written then read inside loopne,
; a dead pop, a dead popfd, two flag setters joined into one jcc, a register written by several
; statements of one node, an all-constant product whose temporary folds away, a timer handler
; that runs between std and movsb, a register write a timer interrupt reads at a wait loop,
; a memory flag operand with a memory-reading pop between the setter and the jump, a byte write
; overwritten by a later write inside a loop, and a full-register write whose upper half a
; low-word write keeps.
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
    mov word [0F00h], 2
    mov ax, 0
    mov es, ax
    mov word [es:61h*4], ssa8_handler
    mov [es:61h*4+2], cs
    mov word [es:8*4], df_timer_handler
    mov [es:8*4+2], cs
    mov ax, 2000h
    mov es, ax

pass_loop:
; PT-3: a temporary's initializer reads a register written before the temporary's last read
    mov ax, [cs:si+2]
    mul word [cs:si+4]
    mov [bx+00h], dx

; EP-1: an operand register redefined between the write and the only read blocks the merge
    mov cx, [cs:si+20h]
    sub cx, [cs:si+22h]
    mov dx, cx
    mov cx, [cs:si+8]
    mov ax, dx
    mov [bx+02h], ax
    pushf
    pop di
    mov [bx+18h], di
    mov edx, 0

; EP-5: a memory read must not move past a store that may write its address
    mov ax, [cs:si+0Ah]
    mov [0F30h], ax
    mov dx, [0F30h]
    mov word [0F30h], 9
    mov cx, dx
    mov [bx+04h], cx
    mov edx, 0

; EP-7b: the merge target's address reads the merged register, so the address keeps the old value
    mov di, 74h
    mov di, [cs:si+1Eh]
    mov [di], di
    mov edi, 0

; FP-1: a flag the rewritten jump consumed is still read by the pushf that follows
    mov ax, [cs:si+0Ch]
    cmp ax, [cs:si+0Eh]
    jae .fp1zero
    mov byte [bx+08h], 1
    jmp .fp1done
.fp1zero:
    mov byte [bx+08h], 0
.fp1done:
    pushf
    pop ax
    mov [bx+0Ah], al

; AA-4: a masked shift emits no flag write, so the call form stays while a flag is live
    mov dx, [cs:si+10h]
    mov cx, [cs:si+12h]
    shl dx, cl
    jc .aa4carry
    mov word [bx+0Ch], 0
    jmp .aa4done
.aa4carry:
    mov word [bx+0Ch], 1
.aa4done:
    mov [bx+0Eh], dx

; SSA-1: a call rewrites the flags, so a setter before it must not feed a jump after it
    mov ax, [cs:si+14h]
    mov cx, [cs:si+16h]
    sub ax, cx
    call set_carry_flag
    jae .ssa1zero
    mov word [bx+10h], 1
    jmp .ssa1done
.ssa1zero:
    mov word [bx+10h], 0
.ssa1done:

; ID-2: les/lds evaluate the segment part's address before the value part writes the register
    mov word [0F60h], 0AAAAh
    mov word [0F62h], 0BEEFh
    mov di, 0F60h
    les di, [di]
    push es
    pop cx
    mov [bx+12h], cx
    mov cx, 2000h
    mov es, cx

; CC-2: a nested test moved out of a boundary a goto lands on must keep the first test
    mov ax, [cs:si+18h]
    mov bp, [cs:si+1Ah]
    mov dx, [cs:si]
    test dl, 8
    jnz .cc2inner
    cmp ax, 1
    jne .cc2none
.cc2inner:
    cmp bp, 2
    jne .cc2none
    mov word [bx+14h], 1
    jmp .cc2done
.cc2none:
    mov word [bx+14h], 0
.cc2done:

; RC-phi: a byte of a join constant is known only when every path agrees on it
    mov ax, [cs:si+1Ch]
    test al, 1
    jz .rcphi2
    mov eax, 00001105h
    jmp .rcphijoin
.rcphi2:
    mov eax, 00009905h
.rcphijoin:
    mov [bx+16h], ah

; PT-1: xadd swaps a memory value with a register; its memory temp is used where it reads
    mov ax, [cs:si+32h]
    mov [0F40h], ax
    mov cx, [cs:si+34h]
    xadd word [0F40h], cx
    mov [bx+06h], cx

; CR-2: a register written then read inside one instruction keeps the incoming constant out of the read
    mov cx, 1
    or ax, 1
    loopne .cr2fwd
    mov word [bx+1Ah], 0BBBBh
.cr2fwd:

; DW-1: a dead pop still moves the stack pointer
    mov ax, [cs:si+2Ah]
    mov dx, [cs:si+2Ch]
    push ax
    push dx
    pop cx
    mov cx, 0
    pop di
    mov [bx+1Eh], di
    mov edi, 0

; DF-2: a dead flag write whose value is a call still moves the stack pointer
    pushfd
    popfd
    mov ax, [cs:si+2Eh]
    cmp ax, [cs:si+30h]

; FP-4: two flag setters on two paths join into one conditional jump
    mov ax, [cs:si+24h]
    test al, 10h
    jnz .fp4b
    cmp word [cs:si+26h], 7
    jmp .fp4j
.fp4b:
    test byte [cs:si+28h], 4
.fp4j:
    je .fp4zero
    mov word [bx+22h], 1
    jmp .fp4done
.fp4zero:
    mov word [bx+22h], 0
.fp4done:

; RC-4: a node writing a register in several statements seeds no constant
    mov ax, 4433h
    xchg al, ah
    mov [bx+26h], ax
    mov eax, 0

; SSA-8: an iret exit uses the registers the handler writes, so the add survives
    mov cx, 41h
    int 61h
    mov [bx+28h], cx

; DS-4: a timer handler (which sets DF itself) runs between std and movsb; the interrupt pushes
; FLAGS and the iret restores them, so the step stays -1. The PIT is armed and interrupts are
; enabled only for this section so the handler cannot disturb the other traps; the mask keeps
; IRQ0 only.
    mov byte [0E60h], 0A5h
    mov byte [0E06h], 0
    mov al, 00110110b
    out 43h, al
    mov al, 51h
    out 40h, al
    mov al, 22h
    out 40h, al
    mov al, 0FEh
    out 21h, al
    std
    sti
wait_df:
    cmp byte [0E06h], 0
    je wait_df
    cli
    push si
    mov si, 0E60h
    mov di, 0E70h
    movsb
    pop si
    cld
    mov [bx+2Ah], di
    mov dl, [0E70h]
    mov dh, 0
    mov [bx+2Ch], dx
    mov al, 0FFh
    out 21h, al

; SSA-9: a register written before a wait loop is read by the timer handler that runs at the loop
; head's event check; the write is dead in the main code (overwritten after the loop) but must stay.
    mov word [0E08h], 0
    mov byte [0E0Ah], 0
    push es
    mov ax, 0
    mov es, ax
    mov word [es:8*4], ssa9_timer_handler
    pop es
    mov al, 0FEh
    out 21h, al
    mov ebp, 00001234h
    sti
.ssa9wait:
    cmp byte [0E0Ah], 0
    je .ssa9wait
    mov ebp, 0
    cli
    mov ax, [0E08h]
    mov [bx+2Eh], ax
    mov al, 0FFh
    out 21h, al
    push es
    mov ax, 0
    mov es, ax
    mov word [es:8*4], df_timer_handler
    pop es

; FP-M: a memory flag operand with a typed statement (pop) between the setter and the jump; the pop
; reads memory, so the operand is not re-read at the jump
    mov word [bx+3Ah], 7
    mov cx, 1234h
    push cx
    mov cx, 0
    cmp word [bx+3Ah], 7
    pop cx
    jne .fpm_skip
    mov [bx+38h], cx
.fpm_skip:

; DW-3: a byte write fully overwritten by a later write is dead, even in a loop whose phi
; joins both versions into one web; a full-register write whose upper half the low-word write
; keeps stays
    mov eax, [cs:dw3_init]
    mov cx, 2
.dw3loop:
    mov al, 5
    mov ax, 9
    loop .dw3loop
    mov [bx+3Ch], eax
    mov eax, 0
    mov edx, [cs:dw3_init]
    mov edx, 0AABBCCDDh
    mov dx, [cs:dw3_low]
    mov [bx+30h], edx
    mov edx, 0

    add bx, 40h
    add si, 54
    dec word [0F00h]
    jnz pass_loop
    hlt

set_carry_flag:
    stc
    ret

ssa8_handler:
    add cx, 1
    iret

df_timer_handler:
    push ax
    mov byte [0E06h], 1
    std
    mov al, 20h
    out 20h, al
    pop ax
    iret

ssa9_timer_handler:
    mov [0E08h], bp
    mov byte [0E0Ah], 1
    push ax
    mov al, 20h
    out 20h, al
    pop ax
    iret

; per pass: sel for the CC-2 goto, mul a/b, EP-1 cx2, EP-5 value, FP-1 a/b, AA-4 dx/cl,
; SSA-1 a/b, CC-2 ax/bp, RC-phi selector, EP-7b di, EP-1 cx init/subtrahend,
; FP-4 sel/x/y, DW-1 a/b, DF-2 cmp pair, PT-1 xadd mem/reg
inputs:
    dw 8, 8000h, 2, 11h, 22h, 4, 1, 2, 8001h, 1, 2, 1, 0, 5, 1, 76h, 0100h, 11h
    dw 0, 7, 4, 0A1h, 0B2h, 3, 5, 41h, 7
    dw 0, 0C000h, 3, 33h, 44h, 8, 2, 1, 4000h, 2, 1, 2, 1, 2, 0, 76h, 0200h, 33h
    dw 10h, 7, 4, 0C3h, 0D4h, 9, 4, 55h, 3

dw3_init:
    dd 12345678h
dw3_low:
    dw 0077h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
