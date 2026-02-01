; build: nasm -f bin tsr22h.com.asm -o tsr22h.com

BITS 16

PSP_SIZE        EQU 100h        ; DOS PSP size (256 bytes)

STACK_SIZE      EQU 100h        ; Loader stack (256 bytes)
TSR_STACK_SIZE  EQU 20h         ; TSR stack (32 bytes)

TSR_RET_CODE    EQU 0AAh        ; TSR return code (AL for INT 21h/31h)

ORG PSP_SIZE

; Loader image size (includes embedded TSR blob + loader stack)
LOADER_IMAGE_BYTES   EQU (end_of_image - $$)
LOADER_TOTAL_BYTES   EQU (PSP_SIZE + LOADER_IMAGE_BYTES)
LOADER_IMAGE_PARAS   EQU ((LOADER_TOTAL_BYTES + 15) / 16)

; Embedded TSR payload size
TSR_IMAGE_BYTES      EQU (tsr_end - tsr_start)               ; TSR payload without PSP
TSR_TOTAL_BYTES      EQU (PSP_SIZE + TSR_IMAGE_BYTES)        ; PSP + TSR payload
TSR_IMAGE_PARAS      EQU ((TSR_TOTAL_BYTES + 15) / 16)       ; paragraphs to keep resident

; Offset of TSR stack top inside the allocated TSR block
TSR_STACK_TOP_OFF    EQU (PSP_SIZE + (tsr_stack_top - tsr_start))

; PSP offsets for terminate address (INT 22h)
PSP_TERM_IP_OFF      EQU 0Ah
PSP_TERM_CS_OFF      EQU 0Ch

; Exit codes
EXIT_OK         EQU 0
ERR_SETBLOCK    EQU 1
ERR_ALLOC       EQU 2
ERR_CREATE_PSP  EQU 3
ERR_TSR_RETURN  EQU 4
ERR_FREE        EQU 5
ERR_BAD_TSR_RC  EQU 6

start:
    ; Setup segments and a known-good stack for the loader
    push cs
    pop  ax
    mov  ds, ax
    mov  es, ax
    mov  ss, ax
    mov  sp, stack_top

    ; Shrink loader memory block
    mov  bx, LOADER_IMAGE_PARAS     ; BX = PSP + loader image in paragraphs
    mov  ah, 4Ah                    ; DOS: Resize memory block (ES = PSP segment)
    int  21h
    jc   exit_setblock_failed

    ; Allocate a new block for the TSR (PSP + TSR payload)
    mov  bx, TSR_IMAGE_PARAS
    mov  ah, 48h                    ; DOS: Allocate memory
    int  21h
    jc   exit_alloc_failed
    mov  [tsr_seg], ax

    ; Create a PSP in the allocated block
    mov  dx, ax                     ; DX = new PSP segment
    mov  ah, 26h                    ; DOS: Create PSP at segment DX
    int  21h
    jc   exit_create_psp_failed

    ; Copy TSR payload (without PSP placeholder) to new block at offset PSP_SIZE
    push ds
    mov  ax, [tsr_seg]
    mov  es, ax
    pop  ds                         ; DS = CS
    cld
    mov  si, tsr_start
    mov  di, PSP_SIZE
    mov  cx, TSR_IMAGE_BYTES
    rep  movsb

    ; Patch INT 22h (terminate address) in the TSR PSP to return to after_tsr_start
    mov  ax, [tsr_seg]
    mov  es, ax
    mov  word [es:PSP_TERM_IP_OFF], after_tsr_start
    push cs
    pop  ax
    mov  word [es:PSP_TERM_CS_OFF], ax

    ; Make the TSR PSP the current PSP
    mov  bx, [tsr_seg]
    mov  ah, 50h                    ; DOS: Set current PSP
    int  21h

    ; Configure segments + stack for TSR entry before transferring control
    mov  ax, [tsr_seg]
    mov  ds, ax
    mov  es, ax
    mov  ss, ax
    mov  sp, TSR_STACK_TOP_OFF

    ; Far-transfer into TSR entry point at new_seg:PSP_SIZE
    push ax
    push PSP_SIZE
    retf

after_tsr_start:
    ; Restore loader context
    push cs
    pop  ax
    mov  ds, ax
    mov  es, ax
    mov  ss, ax
    mov  sp, stack_top

    ; Query return code (DOS: Get return code of last terminated child)
    mov  ah, 4Dh
    int  21h
    cmp  al, TSR_RET_CODE
    jne  exit_bad_tsr_rc

    ; Restore current PSP back to loader (COM PSP segment = CS)
    mov  bx, cs
    mov  ah, 50h                    ; DOS: Set current PSP
    int  21h

    ; Free the allocated TSR block (this removes the resident TSR by design)
    mov  ax, [tsr_seg]
    mov  es, ax
    mov  ah, 49h                    ; DOS: Free memory block
    int  21h
    jc   exit_free_failed

    mov  ax, 4C00h | EXIT_OK
    int  21h

exit_bad_tsr_rc:
    mov  ax, 4C00h | ERR_BAD_TSR_RC
    int  21h

exit_free_failed:
    mov  ax, 4C00h | ERR_FREE
    int  21h

exit_setblock_failed:
    mov  ax, 4C00h | ERR_SETBLOCK
    int  21h

exit_alloc_failed:
    mov  ax, 4C00h | ERR_ALLOC
    int  21h

exit_create_psp_failed:
    mov  ax, 4C00h | ERR_CREATE_PSP
    int  21h


; ---------------------------------------------------------------------------
; Embedded TSR "COM-like" blob (PSP placeholder + TSR payload)
; ---------------------------------------------------------------------------

tsr_program:
    times PSP_SIZE db 0             ; PSP placeholder (not copied)

tsr_start:
    mov  dx, TSR_IMAGE_PARAS        ; DX = paragraphs to keep (includes PSP)
    mov  ax, (3100h | TSR_RET_CODE) ; DOS: TSR, return code in AL
    int  21h                        ; returns via PSP INT 22h

tsr_stack_area:
    times TSR_STACK_SIZE db 0
tsr_stack_top:

tsr_end:


; ---------------------------------------------------------------------------
; Loader stack space
; ---------------------------------------------------------------------------

stack_area:
    times STACK_SIZE db 0
stack_top:

tsr_seg dw 0

end_of_image:
