; compile it with fasm
; convention_return_outputs: a return exit reads the method's outputs and the
; inputs of the entries the ret lands on, not every value.
; 1: add_mem's flags die at the ret because no caller reads them afterwards.
; 2: set_cf's CF stays because the caller does jc right after the call.
; 3: dx_live's DX write stays (the caller stores it); dx_dead's DX write is dead
;    (the caller overwrites DX without reading it).
; 4: s4_shared is reached by a call and by a pushed-address jump: a non-call way
;    in, so every reportable value stays an output.
; 5: s5_dispatcher discards its pushed return address and rets to s5_target, a
;    landing that reads BX which the normal post-call path never reads: the BX
;    write stays.
; 6: s6_si_dead promotes SI to a local and leaves it changed; no caller reads SI
;    afterwards, so it is no code output - but the terminal hlt observes it, so
;    the return exit still writes it back.
; 7: rt1_operand_changed's AX = DX is a single-use value read by the return, but
;    DX is redefined before the ret: the expression cannot move into the return
;    (EP-1), so AX stays a statement.
; 8: rt2_read_crosses_store's AX = [si] is read by the return, but a store to the
;    same [si] lies between: the read cannot cross it (EP-5), so AX stays a
;    statement.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    mov word [0400h], 0100h

    ; 1
    mov ax, 1
    call add_mem
    mov [0200h], ax

    ; 2
    call set_cf
    jc .cf_set
    mov byte [0202h], 0
    jmp .cf_done
.cf_set:
    mov byte [0202h], 1
.cf_done:

    ; 3
    call dx_live
    mov [0204h], dx
    call dx_dead
    mov dx, 9
    mov [0206h], dx

    ; 6
    call s6_si_dead

    ; 4
    call s4_shared
    mov [0208h], cx
    push s4_land
    jmp s4_shared
s4_land:
    mov [020Ah], cx

    ; 7
    call rt1_operand_changed
    mov [0216h], ax
    mov [021Eh], dx

    ; 8
    mov si, 0400h
    mov bx, 0BEEFh
    call rt2_read_crosses_store
    mov [0218h], ax

    ; 5
    call s5_dispatcher
    db 0F4h                 ; wrongly returning here stops the run

s5_target:
    mov [0214h], bx
    hlt

rt1_operand_changed:
    push word [0200h]
    pop dx
    mov ax, dx
    mov dx, 9
    ret

rt2_read_crosses_store:
    mov di, [0200h]
    mov ax, [si]
    mov [si], bx
    ret

add_mem:
    add ax, [0400h]
    ret

set_cf:
    stc
    ret

dx_live:
    mov dx, 5
    ret

dx_dead:
    mov dx, 7
    ret

s4_shared:
    mov cx, 1234h
    ret

s6_si_dead:
    mov si, [0400h]
    mov di, si
    add di, si
    add di, si
    ret

s5_dispatcher:
    pop ax                  ; discard the pushed return address
    mov bx, 55AAh
    mov ax, s5_target
    push ax
    ret

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
