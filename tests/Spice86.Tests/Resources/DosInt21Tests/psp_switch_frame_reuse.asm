; Build: fasm psp_switch_frame_reuse.asm psp_switch_frame_reuse.com
; A PSP made current by 50h/55h terminates and the parent's INT 22h resume pops the interrupt
; frame a stack-saving or same-SP typed call left at the recorded SS:SP. Section 1 reuses the
; 26h frame slot at the same SP with a typed 50h; section 2 uses a typed 30h at SP = Y and keeps
; every later frame below Y-6..Y-1 on a scratch stack. Each resume stores pushf, SS and SP to
; 3000:0000 so the gate compares the popped FLAGS.
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
psp1_seg equ 2000h
psp2_seg equ 2100h
stk1_seg equ 3000h
stk1_top equ 0F000h
stk2_seg equ 3100h
stk2_top equ 0F000h
result_seg equ 5000h

r_flags1 equ 0
r_ss1 equ 2
r_sp1 equ 4
r_exp_ss1 equ 6
r_exp_sp1 equ 8
r_flags2 equ 10
r_ss2 equ 12
r_sp2 equ 14
r_exp_ss2 equ 16
r_exp_sp2 equ 18

start:
    mov ah, 62h
    int 21h
    mov [own_psp], bx

section1:
    ; 26h (untyped, stack-saving) at SP = S with all status flags clear: its frame FLAGS at
    ; S-2..S-1 stay visible only if the following 50h writes no frame.
    xor ax, ax
    mov ah, 0
    sahf
    cld
    mov dx, psp1_seg
    mov si, 0010h
    mov ah, 26h
    int 21h
    mov [exp_sp1], sp
    mov [exp_ss1], ss
    ; typed 50h at the same SP with OF=1 DF=1 ZF=1 PF=1 CF=1: its frame overwrites S-6..S-1.
    mov al, 7Fh
    add al, 7Fh
    mov ah, 45h
    sahf
    std
    mov bx, psp1_seg
    mov ah, 50h
    int 21h
    mov ax, psp1_seg
    mov es, ax
    mov word [es:000Ah], resume1
    mov [es:000Ch], cs
    cli
    mov ax, stk1_seg
    mov ss, ax
    mov sp, stk1_top
    sti
    mov ax, 4C00h
    int 21h
    jmp failed

resume1:
    pushf
    pop ax
    mov bx, result_seg
    mov es, bx
    mov [es:r_flags1], ax
    mov ax, ss
    mov [es:r_ss1], ax
    mov [es:r_sp1], sp
    mov ax, [cs:exp_ss1]
    mov [es:r_exp_ss1], ax
    mov ax, [cs:exp_sp1]
    mov [es:r_exp_sp1], ax

section2:
    ; 55h creates a child PSP and makes it current; 50h returns to the parent PSP.
    mov dx, psp2_seg
    mov si, 0010h
    mov ah, 55h
    int 21h
    mov bx, [cs:own_psp]
    mov ah, 50h
    int 21h
    ; typed 30h (saves the caller stack) at SP = Y with OF=1 DF=1 AF=1 CF=1.
    mov al, 7Fh
    add al, 7Fh
    mov ah, 11h
    sahf
    std
    ; seed the frame area: anything left after termination means no frame was written.
    mov bp, sp
    mov word [bp-6], 1111h
    mov word [bp-4], 2222h
    mov word [bp-2], 5A5Ah
    mov ah, 30h
    int 21h
    mov [exp_ss2], ss
    mov [exp_sp2], sp
    ; keep every later frame off SS:[Y-6..Y-1]: run on a scratch stack and push four words.
    cli
    mov ax, stk2_seg
    mov ss, ax
    mov sp, stk2_top
    sti
    push ax
    push ax
    push ax
    push ax
    ; 50h to the child with other flags (PF=1 only), then its INT 22h and 4Ch.
    xor ax, ax
    mov ah, 04h
    sahf
    mov bx, psp2_seg
    mov ah, 50h
    int 21h
    mov ax, psp2_seg
    mov es, ax
    mov word [es:000Ah], resume2
    mov [es:000Ch], cs
    mov ax, 4C00h
    int 21h
    jmp failed

resume2:
    pushf
    pop ax
    mov bx, result_seg
    mov es, bx
    mov [es:r_flags2], ax
    mov ax, ss
    mov [es:r_ss2], ax
    mov [es:r_sp2], sp
    mov ax, [cs:exp_ss2]
    mov [es:r_exp_ss2], ax
    mov ax, [cs:exp_sp2]
    mov [es:r_exp_sp2], ax
    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

own_psp dw 0
exp_ss1 dw 0
exp_sp1 dw 0
exp_ss2 dw 0
exp_sp2 dw 0
