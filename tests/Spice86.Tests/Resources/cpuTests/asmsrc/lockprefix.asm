; lockprefix.asm
;
; Validates that invalid LOCK prefix usage triggers INT 6 (#UD / Invalid Opcode),
; while valid LOCK usage completes without interruption.
;
; This is a 64 KB BIOS image loaded at physical 0xF0000 (CS=0xF000, IP=0x0000).
;
; Memory layout (DS=0x0000, flat low memory):
;   [0x0000] = invalid_lock_count (word) -- incremented by INT 6 handler each time it fires
;   [0x0002] = valid_lock_count   (word) -- written as 3 after the three valid LOCK tests pass
;   [0x0004] = next_test_ip       (word) -- return target written before each invalid instruction;
;                                            the INT 6 handler patches the stack with this value
;   [0x0010] = scratch            (word) -- memory operand for LOCK tests
;
; IVT entry for INT 6 (6 * 4 = 0x18):
;   [0x0018] = int6_handler offset (within CS=0xF000)
;   [0x001A] = int6_handler segment (0xF000)
;
; Expected memory after HLT:
;   [0x0000] = 3  (LOCK MOV [mem], LOCK ADD reg, LOCK INC reg each trigger INT 6 once)
;   [0x0002] = 3  (LOCK ADD [mem], LOCK INC [mem], LOCK DEC [mem] complete without INT 6)

use16
org 0

start:
    ; Establish flat segment registers
    xor ax, ax
    mov ds, ax
    mov ss, ax
    mov sp, 0x8000

    ; Install INT 6 (#UD) handler in the IVT (entry 6, physical address 0x0018)
    mov word [0x18], int6_handler
    mov word [0x1A], cs

    ; Initialise counters to zero
    mov word [0x0000], 0
    mov word [0x0002], 0

    ; -------------------------------------------------------------------
    ; Valid LOCK uses -- FASM assembles these directly.
    ; Each has a memory destination, so the LOCK prefix is architecturally
    ; legal. None should trigger INT 6.
    ; -------------------------------------------------------------------

    ; V1: LOCK ADD [scratch], ax  (ADD with memory dest -- allowed)
    mov word [0x0010], 0x0001
    lock add [0x0010], ax

    ; V2: LOCK INC word [scratch]  (INC with memory dest -- allowed)
    lock inc word [0x0010]

    ; V3: LOCK DEC word [scratch]  (DEC with memory dest -- allowed)
    lock dec word [0x0010]

    ; All three valid tests completed without INT 6
    mov word [0x0002], 3

    ; -------------------------------------------------------------------
    ; Invalid LOCK uses -- encoded as raw bytes because FASM refuses to
    ; assemble them.
    ; Before each invalid instruction the address of the following label
    ; is stored in [0x0004] so the INT 6 handler can patch the return IP.
    ; -------------------------------------------------------------------

    ; I1: LOCK MOV [scratch], ax
    ;   MOV is not in the LOCK-allowed instruction set -> INT 6 (#UD)
    ;   Bytes: F0 89 06 10 00  (LOCK prefix + MOV [0x0010], AX)
    mov word [0x0004], after_lock_mov_mem
    db 0xF0, 0x89, 0x06, 0x10, 0x00
after_lock_mov_mem:

    ; I2: LOCK ADD ax, bx
    ;   ADD is in the allowed set but the destination is a register, not
    ;   memory -> INT 6 (#UD)
    ;   Bytes: F0 03 C3  (LOCK prefix + ADD AX, BX)
    mov word [0x0004], after_lock_add_reg
    db 0xF0, 0x03, 0xC3
after_lock_add_reg:

    ; I3: LOCK INC ax
    ;   INC is in the allowed set but the destination is a register, not
    ;   memory -> INT 6 (#UD)
    ;   Bytes: F0 FF C0  (LOCK prefix + INC AX)
    mov word [0x0004], after_lock_inc_reg
    db 0xF0, 0xFF, 0xC0
after_lock_inc_reg:

done:
    hlt
    jmp done

; -------------------------------------------------------------------
; INT 6 Handler (#UD -- Invalid Opcode / Invalid LOCK)
;
; Stack on entry (real mode):
;   [SP+0] = return IP  (points to the faulting instruction)
;   [SP+2] = return CS
;   [SP+4] = FLAGS
;
; Strategy: patch the return IP to the address stored in [0x0004]
; (written by the caller just before the invalid instruction), then
; increment the invalid counter and return via IRET.
; -------------------------------------------------------------------
int6_handler:
    push bp
    mov bp, sp
    ; After PUSH BP:
    ;   [bp+0] = saved BP
    ;   [bp+2] = return IP   <-- patch target
    ;   [bp+4] = return CS
    ;   [bp+6] = FLAGS
    mov ax, [0x0004]
    mov [bp+2], ax
    pop bp
    inc word [0x0000]
    iret

; -------------------------------------------------------------------
; BIOS padding and reset vector
; -------------------------------------------------------------------
rb 65520 - $
    jmp start
    dw 0x0000

rb 65534 - $
    dw 0xFFFF
