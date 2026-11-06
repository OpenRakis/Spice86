; compile it with fasm
; Typed-service-call fixture: every int 21h site lands in one kind. Results go to
; ES=2000h offsets 40h-84h (physical 20040h-20084h).
org 100h
use16
str_off equ 00E8h
start:
    mov ax, 2000h
    mov ss, ax
    mov sp, 1000h
    mov es, ax

; outputs of a typed call used after it: AH=30h (version) writes AX, BX and CX - wrong rewrite:
; treating a register the service writes as preserved across the call
    mov bx, 1111h
    mov cx, 2222h
    mov ah, 30h
    int 21h
    mov [es:40h], ax
    mov [es:42h], bx
    mov [es:44h], cx

; values preserved across a typed call: AH=02h (print char) writes nothing of SI or BP - wrong
; rewrite: killing a live value because the untyped call could have written it
    mov si, 3333h
    mov bp, 4444h
    mov dl, '*'
    mov ah, 02h
    int 21h
    mov [es:46h], si
    mov [es:48h], bp

; the selector write the helper does: AH reads as the service result after the call, and stays
; the selector value for a service that writes nothing - wrong rewrite: dropping the selector
; write because the program's own mov to AH is dead
    mov ah, 30h
    int 21h
    mov [es:4Ah], ah
    mov ah, 02h
    mov dl, '$'
    int 21h
    mov [es:4Bh], ah

; a carry the service returns: the open of a missing file sets CF and an error code in AX - wrong
; rewrite: a flag write the contract declares treated as a flag the call preserves
    mov dx, missing_name
    mov al, 0
    mov ah, 3Dh
    int 21h
    jc .open_failed
    mov [es:4Ch], ax
    jmp .open_done
.open_failed:
    mov [es:4Ch], ax
    mov word [es:4Eh], 0F00Dh
.open_done:

; a carry set before a service that writes no flags: CF survives the call - wrong rewrite: a
; flag the contract does not write treated as dead
    stc
    mov ah, 02h
    mov dl, '+'
    int 21h
    jc .carry_kept
    mov word [es:50h], 0
    jmp .carry_done
.carry_kept:
    mov word [es:50h], 0BEEFh
.carry_done:

; the interrupt frame below SP: the call pushed FLAGS, CS, IP on the stack - wrong rewrite: a
; typed call that does not build the same frame the int did
    mov ax, 0
    push ax
    popf
    mov ah, 02h
    mov dl, '#'
    int 21h
    mov bp, sp
    mov ax, [bp-6]
    mov [es:52h], ax
    mov ax, [bp-4]
    mov [es:54h], ax
    mov ax, [bp-2]
    mov [es:56h], ax

; a register the service writes, constant before, used in ALU and branches after: AH=30h writes
; AX, BX and CX - wrong rewrite: the constant from before the call substituted across it
    mov bx, 1111h
    mov cx, 2222h
    mov ah, 30h
    int 21h
    add bx, 1
    mov [es:68h], bx
    cmp cx, 2222h
    je .cx_folded
    mov word [es:6Ah], 0
    jmp .cx_done
.cx_folded:
    mov word [es:6Ah], 0BEEFh
.cx_done:
    cmp ch, 22h
    je .ch_folded
    mov word [es:6Ch], 0
    jmp .ch_done
.ch_folded:
    mov word [es:6Ch], 0BEEFh
.ch_done:

; the same for DX through a service that writes it: AH=36h (free disk space on the default
; drive) writes AX, BX, CX and DX
    mov dh, 55h
    mov dl, 0
    mov ah, 36h
    int 21h
    add dx, 1
    mov [es:6Eh], dx
    cmp dx, 5556h
    je .dx_folded
    mov word [es:70h], 0
    jmp .dx_done
.dx_folded:
    mov word [es:70h], 0BEEFh
.dx_done:

; a register the service does NOT write: the constant may fold across the call
    mov si, 3333h
    mov ah, 02h
    mov dl, '~'
    int 21h
    add si, 1
    mov [es:72h], si

; a constant-selector site with mixed targets: do_int30 is called before the hook (the stub
; answers) and after it (the program handler answers) - wrong rewrite: typing a site whose
; observed targets are not all the installed stub
    call do_int30
    mov [es:74h], ax
    mov [es:76h], bx

; a varying selector: one site, two different AH values - must stay InterruptCall - wrong
; rewrite: typing the site with the first value discovery saw
    mov ah, 30h
    call do_int
    mov [es:58h], ax
    mov ah, 02h
    mov dl, 'v'
    call do_int
    mov [es:5Ah], ax

; a hooked vector: the program handler answers AH=30h itself and chains the rest - wrong
; rewrite: typing a site whose observed targets are not all the installed stub
    mov ah, 35h
    mov al, 21h
    int 21h
    mov ax, es
    mov [cs:saved_off], bx
    mov [cs:saved_seg], ax
    mov ax, 2000h
    mov es, ax
    mov dx, handler
    mov ah, 25h
    mov al, 21h
    int 21h
    mov ah, 30h
    int 21h
    mov [es:5Ch], ax
    mov [es:5Eh], bx
    mov ah, 30h
    call do_int
    mov [es:60h], ax
    mov [es:62h], bx
    call do_int30
    mov [es:78h], ax
    mov [es:7Ah], bx
    mov dx, [cs:saved_off]
    mov ax, [cs:saved_seg]
    mov ds, ax
    mov ah, 25h
    mov al, 21h
    int 21h
    push cs
    pop ds
    mov ah, 30h
    int 21h
    mov [es:64h], ax
    mov [es:66h], bx

; a segment register the service writes: AH=35h returns the int 21h vector in ES:BX - wrong
; rewrite: keeping ES = 2000h across a service that writes ES would read 2000h:0 instead of
; F000h:0 (K=0: the byte at the vector segment's start, FEh in this run, differs from the 00h
; of our result segment, which nothing else writes)
    mov ax, 2000h
    mov es, ax
    mov ax, 3521h
    int 21h
    mov bl, [es:0]
    mov ax, 2000h
    mov es, ax
    mov [es:7Ch], bl

; a segment register the service does NOT write: DS=2000h survives AH=09h - right rewrite: the DS
; read below prints through a location class, since the service does not write DS
    mov ax, 2000h
    mov ds, ax
    mov byte [es:0F0h], '$'
    mov dx, 0F0h
    mov ah, 09h
    int 21h
    mov al, [ds:0F0h]
    mov [es:7Eh], al

; a DX output of a typed call read after it: AH=44h AL=00h (IOCTL get device information,
; BX=1 stdout) writes DX with the device information word - wrong rewrite: DX left out of
; the typed call's writes, so the pre-call constant 0BEEFh would be folded into the store
    mov dx, 0BEEFh
    mov ax, 4400h
    mov bx, 1
    int 21h
    mov [es:80h], dx

; a DS that differs per entry into one shared region: str_y is a partition entry (reached
; through str_b with DS=3000h) and is also reached inside the partition through str_w with
; DS=2000h, so the ds of str_y merges the in-partition value with the entry value - wrong
; rewrite: folding one into str_y's string print and read ([82h] = 'Y' from 2000h:E8h,
; [83h] = 'Z' from 3000h:E8h)
    mov ax, 2000h
    mov ds, ax
    mov byte [ds:str_off], 'Y'
    mov byte [ds:str_off+1], '$'
    mov ax, 3000h
    mov ds, ax
    mov byte [ds:str_off], 'Z'
    mov byte [ds:str_off+1], '$'
    mov ax, 2000h
    mov ds, ax
    mov cx, 0
    call str_a
    mov [es:82h], al
    mov cx, 1
    call str_b
    mov [es:83h], al
    mov ax, 2000h
    mov ds, ax

; a constant selector whose contract stays Unknown: AH=0Ch AL=FFh (flush the keyboard
; buffer and run no function) has no precise contract - wrong rewrite: typing the site
; anyway, which calls a helper that does not exist (the generated program no longer compiles)
    mov dx, 0BEEFh
    mov ax, 0CFFh
    int 21h
    mov [es:84h], dx

    call preserved_output

    hlt

; int 21h subroutine: the site sees AH=30h, AH=02h and later the program handler
do_int:
    int 21h
    ret

; int 21h subroutine with a constant selector: the site sees the stub before the hook and the
; program handler after it
do_int30:
    mov ah, 30h
    int 21h
    ret

; program handler for int 21h: answers AH=30h, chains everything else to the saved vector
handler:
    cmp ah, 30h
    je .answer
    jmp dword [cs:saved_off]
.answer:
    mov ax, 1234h
    mov bx, 5678h
    iret

; str_a / str_b are the two call roots; str_x, str_y, str_p and str_w form one shared
; region entered at str_x (through str_a) and at str_y (through str_b), so the ds of
; str_y merges the in-partition value from str_w with the str_b entry's value. The
; dead je from str_y to str_x is load-bearing: without a direct jump between the two
; entry blocks, the partition normalizer splits the shared region into single-entry
; partitions and the ds phi at str_y loses its in-partition operand
str_a:
    jmp str_x
str_b:
    mov ax, 3000h
    mov ds, ax
    jmp str_y
str_x:
    cmp cx, 0
    je str_w
    ret
str_y:
    mov al, [ds:str_off]
    cmp cx, 2
    je str_x
str_p:
    mov dx, str_off
    mov ah, 09h
    int 21h
    cmp cx, 1
    je str_x
    ret
str_w:
    mov ax, 2000h
    mov ds, ax
    jmp str_y

missing_name db 'NOFILE.XYZ', 0
saved_off    dw 0
saved_seg    dw 0

; AH=67h with a handle table size equal to the current one clears CF and preserves AX:
; storing AX and the carry bit afterwards catches a generated call that forgets the
; output registers the service does not write on this path
preserved_output:
    nop
    mov ax, 675Ah
    mov bx, 20
    int 21h
    mov [es:86h], ax
    pushf
    pop ax
    and ax, 1
    mov [es:88h], ax
    ret
