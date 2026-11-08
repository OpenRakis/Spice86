; Build: nasm -f bin bios_disk_parameters.asm -o bios_disk_parameters.com
; INT 13h AH=08h drive parameters through the installed BIOS stub, in three command-tail
; modes: 'h' HDD (DL=80h), 'f' mounted 1.44M floppy, 'a' absent floppy. Also exercises the
; direct INT 11h / INT 12h equipment and memory calls and the signed movsx consumers of the
; DOS version call. A failing check reports its number on the detail port before failing.
bits 16
org 100h

result_port equ 0999h
detail_port equ 0998h
success equ 00h
failure equ 0FFh

start:
    ; First non-space command-tail character at PSP:81h selects the mode.
    mov si, 81h
.scan:
    mov al, [si]
    cmp al, ' '
    jne .have_mode
    inc si
    jmp .scan
.have_mode:
    mov [cs:mode], al
    mov dl, 80h
    cmp al, 'h'
    je .drive_ready
    mov dl, 00h
    cmp al, 'f'
    je .drive_ready
    cmp al, 'a'
    je .drive_ready
    mov al, 'm'
    jmp fail_with

.drive_ready:
    ; INT 11h equipment list: the BIOS reports an empty equipment word.
    int 11h
    test ax, ax
    mov al, 1
    jnz fail_with

    ; INT 12h conventional memory: the fixture gets the normal 640 KB.
    int 12h
    cmp ax, 640
    mov al, 2
    jne fail_with

    ; DL from the mode, then INT 13h AH=08h with seeded preserved inputs AL=5Ah,
    ; BX=A5C3h, CX=1234h, DH=56h.
    mov dl, 80h
    cmp byte [cs:mode], 'h'
    je .have_dl
    mov dl, 00h
.have_dl:
    mov al, 5Ah
    mov bx, 0A5C3h
    mov cx, 1234h
    mov dh, 56h
    mov ah, 08h
    mov [cs:sp_before], sp
    mov [cs:ds_before], ds
    mov [cs:es_before], es
    mov [cs:si_before], si
    mov [cs:di_before], di
    mov [cs:bp_before], bp
    int 13h
    pushf
    pop word [cs:out_flags]
    mov [cs:r_ax], ax
    mov [cs:r_bx], bx
    mov [cs:r_cx], cx
    mov [cs:r_dx], dx

    ; SP, DS, ES, SI, DI and BP are untouched by the service.
    mov al, 3
    cmp sp, [cs:sp_before]
    jne fail_with
    mov ax, ds
    cmp ax, [cs:ds_before]
    jne fail_with
    mov ax, es
    cmp ax, [cs:es_before]
    jne fail_with
    cmp si, [cs:si_before]
    jne fail_with
    cmp di, [cs:di_before]
    jne fail_with
    cmp bp, [cs:bp_before]
    jne fail_with

    mov al, [cs:mode]
    cmp al, 'h'
    je expect_hdd
    cmp al, 'f'
    je expect_floppy

expect_absent:
    ; No drive: CF set, AH=80h status, AL/CX/BX preserved, DX=DH<<8|DL.
    mov al, 4
    test word [cs:out_flags], 1
    jz fail_with
    mov al, 5
    cmp word [cs:r_ax], 805Ah
    jne fail_with
    mov al, 6
    cmp word [cs:r_bx], 0A5C3h
    jne fail_with
    mov al, 7
    cmp word [cs:r_cx], 1234h
    jne fail_with
    mov al, 8
    cmp word [cs:r_dx], 5600h
    jne fail_with
    jmp signed_sequence

expect_hdd:
    ; Not a floppy: CF clear, AX=005Ah, CX=FE3Fh, DX=FF01h, BX preserved.
    mov al, 9
    test word [cs:out_flags], 1
    jnz fail_with
    mov al, 10
    cmp word [cs:r_ax], 005Ah
    jne fail_with
    mov al, 11
    cmp word [cs:r_bx], 0A5C3h
    jne fail_with
    mov al, 12
    cmp word [cs:r_cx], 0FE3Fh
    jne fail_with
    mov al, 13
    cmp word [cs:r_dx], 0FF01h
    jne fail_with
    jmp signed_sequence

expect_floppy:
    ; 1.44M floppy: CF clear, AX=0, CH=79 cylinders-1, CL=18 sectors, DH=1 head, DL=1 drive,
    ; BL=4 media type.
    mov al, 14
    test word [cs:out_flags], 1
    jnz fail_with
    mov al, 15
    cmp word [cs:r_ax], 0000h
    jne fail_with
    mov al, 16
    cmp word [cs:r_bx], 0A504h
    jne fail_with
    mov al, 17
    cmp word [cs:r_cx], 4F12h
    jne fail_with
    mov al, 18
    cmp word [cs:r_dx], 0101h
    jne fail_with

signed_sequence:
    ; DOS saves the caller SS:SP into PSP+2Eh on service entry; the direct call records the
    ; same SS and SP-6 (the six bytes the interrupt frame would have pushed).
    mov ax, sp
    sub ax, 6
    mov [cs:psp_expected_sp], ax
    mov [cs:psp_expected_ss], ss
    ; The DOS version result feeds signed consumers: generated code must type the
    ; service-result local unsigned or the movsx pair fails to compile.
    mov ah, 30h
    int 21h
    movsx ecx, ax
    movsx edx, ax
    mov eax, 0
    mov al, 19
    cmp ecx, 5
    jne fail_with
    mov al, 20
    cmp edx, 5
    jne fail_with
    mov ax, [cs:2Eh]
    mov [cs:psp_saved_sp], ax
    mov ax, [cs:30h]
    mov [cs:psp_saved_ss], ax
    mov al, 21
    mov ax, [cs:psp_expected_sp]
    cmp [cs:psp_saved_sp], ax
    jne fail_with
    mov al, 22
    mov ax, [cs:psp_expected_ss]
    cmp [cs:psp_saved_ss], ax
    jne fail_with

    mov al, success
    jmp write_result

fail_with:
    mov dx, detail_port
    out dx, al
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

mode       db 0
psp_expected_sp dw 0
psp_expected_ss dw 0
psp_saved_sp dw 0
psp_saved_ss dw 0
sp_before  dw 0
ds_before  dw 0
es_before  dw 0
si_before  dw 0
di_before  dw 0
bp_before  dw 0
out_flags  dw 0
r_ax       dw 0
r_bx       dw 0
r_cx       dw 0
r_dx       dw 0
