; Build: nasm -f bin protectedmode_unified_pool.asm -o protectedmode_unified_pool.bin
;
; Phase 1 of the XMS/EMS unified-memory-backing-store plan: proves a flat protected-mode
; descriptor can directly address physical memory well above the old ~1.06MB conventional+HMA
; ceiling (0x500000 = 5MB in, comfortably inside the new unified extended-memory pool but
; unreachable with the old Ram size) with no XMS/EMS API involved at all.
;
; Mirrors protectedmode_entry.bin's proven structure: the GDT is built at runtime via plain
; memory writes to a fixed low-memory scratch address (0x600), not embedded as static data
; relative to this file's own (irrelevant) load offset. The code selector (0x08) stays a
; 16-bit segment based at 0xF0000 (matching where this BIOS-style image is loaded), so no
; operand-size prefixes are needed for the transition jumps - only the data selector (0x10)
; is a true 32-bit flat (base 0, 4GB limit) descriptor, needed to reach 0x500000.
cpu 386
org 0

start:
    mov sp, 0x1000

    ; Descriptor 1 (selector 0x08): 16-bit code, base=0xF0000 (matches this image's load
    ; address), limit=0xFFFF, byte-granular - so EIP can keep using this file's own small,
    ; org-0-relative offsets after the far jump.
    mov word [0x0608], 0xFFFF
    mov word [0x060A], 0x0000
    mov byte [0x060C], 0x0F
    mov byte [0x060D], 0x9A
    mov byte [0x060E], 0x00
    mov byte [0x060F], 0x00

    ; Descriptor 2 (selector 0x10): true flat 32-bit data, base=0, limit=0xFFFFF with 4K
    ; granularity (~4GB reach) - needed to address 0x500000.
    mov word [0x0610], 0xFFFF
    mov word [0x0612], 0x0000
    mov byte [0x0614], 0x00
    mov byte [0x0615], 0x92
    mov byte [0x0616], 0xCF
    mov byte [0x0617], 0x00

    ; GDT pseudo-descriptor at 0x0620: limit (3 entries * 8 - 1), base = 0x000600.
    mov word [0x0620], 0x0017
    mov dword [0x0622], 0x00000600

    lgdt [0x0620]

    mov eax, cr0
    or eax, 1
    mov cr0, eax

    jmp 0x08:protected_entry

protected_entry:
    mov ax, 0x10
    mov ds, ax

    mov byte [dword 0x500000], 0x42

    mov eax, cr0
    and eax, 0xFFFFFFFE
    mov cr0, eax

    jmp 0xF000:real_mode_entry

real_mode_entry:
    mov ax, 0xF000
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 0xFFF0
    hlt

times 0xFFF0 - ($-$$) db 0
    jmp 0xF000:start

times 0x10000 - ($-$$) db 0
