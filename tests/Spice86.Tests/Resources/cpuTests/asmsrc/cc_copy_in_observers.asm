; compile it with fasm
; Sections for copy-in elision: each callee is typed with a parameter whose copy-in line one
; specific reader still needs, and each caller passes a value that leaves the machine register
; it no longer sets stale inside the callee when the copy-in is gone.
; S1 the callee reads the parameter register only through a loop phi; S2 it also feeds an
; indirect (untyped) call that reads it; S3 it returns the register unchanged on both paths;
; S4 a partial write (al) carries the untouched AH to a second store; S5 a DsGlobals store
; hides the read of the DS parameter; S6 a typed call's AX output feeds an indirect (untyped)
; call's sink - the output's web needs the register back, so its element keeps the register.
; DS = SS = 2000h, SP = 1000h. Results go to offsets 40h-44h, 48h, 50h, 58h-5Ch, 70h-74h and 80h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; S1: s1_sum sums three words into its AX argument; the entry value is read only through the
; loop phi, so the register must hold it when the loop starts.
    mov word [210h], 3
    mov word [200h], 1
    mov word [202h], 2
    mov word [204h], 4
    mov ax, 7
    mov si, 200h
    call s1_sum
    mov [40h], ax

; S2: s2_body adds 1 to its AX argument and also feeds AX to the indirect call s2_sink reads;
; the call summary reads the machine register and no printed node covers it.
    mov ax, 3
    mov bx, s2_sink
    call s2_body
    mov [42h], ax

; S3: s3_passthrough returns AX unchanged on both paths; the typed return prints the raw
; register. The operand is the previous call's live AX result so the return cannot fold to
; a constant.
    call s3_passthrough
    mov [44h], ax

; S4: s4_high stores its AX argument, writes AL only, then stores AX again; the second store
; reads the carried AH of the entry value through the machine register.
    call s4_caller

; S5: s5_store stores its AX argument to ds:80h through DsGlobals; the caller observes its
; 3000h DS write through ES, so the 2000h write feeding the DS argument is dropped and only
; the copy-in puts 2000h in State.DS.
    mov ax, 3000h
    mov ds, ax
    mov ax, 2000h
    mov es, ax
    mov [es:48h], ds
    mov ds, ax
    mov ax, 7777h
    call s5_store

; S6: s6_leaf returns its AX argument incremented; the caller stores the typed result and
; passes it through the machine register to the indirect call s6_sink reads.
    mov ax, 10h
    call s6_leaf
    mov [5Ah], ax
    mov bx, s6_sink
    call bx
    mov [5Ch], ax

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

s1_sum:
    add ax, [si]
    add si, 2
    dec word [210h]
    jnz s1_sum
    ret

s2_body:
    call bx
    add ax, 8
    ret

s2_sink:
    mov [50h], ax
    ret

s3_passthrough:
    cmp ax, 7
    ja s3_big
    mov ax, 9
    ret
s3_big:
    ret

s4_caller:
    mov ax, 0AB00h
    call s4_high
    ret

s4_high:
    mov [70h], ax
    mov al, 5
    mov [74h], ax
    ret

s5_store:
    mov [80h], ax
    ret

s6_leaf:
    inc ax
    ret

s6_sink:
    mov [58h], ax
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
