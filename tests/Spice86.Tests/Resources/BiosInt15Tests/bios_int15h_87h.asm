; Build: nasm -f bin bios_int15h_87h.asm -o bios_int15h_87h.com
;
; Regression test for BIOS INT 15h, AH=87h (COPY EXTENDED MEMORY -
; SystemBiosInt15Handler.CopyExtendedMemory, a reimplementation of SeaBIOS's handle_1587). This
; function always read/wrote through the shared Memory bus directly (no private XMS/EMS-style
; array), so it transparently gained the ability to address the full unified extended-memory pool
; once the XMS/EMS unification plan's Phase 1 grew Ram beyond the old ~1.06MB conventional+HMA
; ceiling. This test proves that by round-tripping a marker word through a linear address well
; beyond that old ceiling (3MB), safely inside the new default 16MB pool.
cpu 386
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
far_address equ 0300000h  ; 3MB - unreachable before Ram was grown past ~0x110000

start:
    mov ax, cs
    mov es, ax

    mov word [srcBuffer], 1234h

    ; Zero the 48-byte GDT structure once; only the handle/address fields change per call.
    mov di, gdt
    mov cx, 30h
    xor al, al
    cld
    rep stosb

    ; --- Copy conventional (srcBuffer) -> extended (far_address) ---
    mov word [gdt+14h], 0              ; SourceHandle = 0 (conventional memory)
    xor eax, eax
    mov ax, cs
    shl eax, 16
    mov dword [gdt+16h], eax
    mov word [gdt+16h], srcBuffer      ; SourceOffsetOrAddress = CS:srcBuffer

    mov word [gdt+1Ah], 1              ; DestinationHandle != 0 (extended memory)
    mov dword [gdt+1Ch], far_address

    mov cx, 1                          ; 1 word = 2 bytes
    mov si, gdt
    mov ah, 87h
    int 15h
    jc failed
    mov al, 1
    mov dx, details_port
    out dx, al

    ; --- Copy extended (far_address) -> conventional (verifyBuffer) ---
    mov word [gdt+14h], 1              ; SourceHandle != 0 (extended memory)
    mov dword [gdt+16h], far_address

    mov word [gdt+1Ah], 0              ; DestinationHandle = 0 (conventional memory)
    xor eax, eax
    mov ax, cs
    shl eax, 16
    mov dword [gdt+1Ch], eax
    mov word [gdt+1Ch], verifyBuffer   ; DestinationOffsetOrAddress = CS:verifyBuffer

    mov cx, 1
    mov si, gdt
    mov ah, 87h
    int 15h
    jc failed
    mov al, 2
    mov dx, details_port
    out dx, al

    cmp word [verifyBuffer], 1234h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

align 4
gdt: times 30h db 0
srcBuffer: dw 0
verifyBuffer: dw 0
