PS_CF       equ 0x0001
PS_PF       equ 0x0004
PS_AF       equ 0x0010
PS_ZF       equ 0x0040
PS_SF       equ 0x0080
PS_TF       equ 0x0100
PS_IF       equ 0x0200
PS_DF       equ 0x0400
PS_OF       equ 0x0800
PS_ARITH    equ (PS_CF | PS_PF | PS_AF | PS_ZF | PS_SF | PS_OF)
PS_LOGIC    equ (PS_CF | PS_PF | PS_ZF | PS_SF | PS_OF)
PS_MULTIPLY equ (PS_CF | PS_OF) ; only CF and OF are "defined" following MUL or IMUL
PS_DIVIDE   equ 0 ; none of the Processor Status flags are "defined" following DIV or IDIV
PS_SHIFTS_1 equ (PS_CF | PS_SF | PS_ZF | PS_PF | PS_OF)
PS_SHIFTS_R equ (PS_CF | PS_SF | PS_ZF | PS_PF)

CR0_MSW_PE  equ 0x0001
CR0_PG      equ 0x80000000	; set if paging enabled

ACC_TYPE_GATE386_INT  equ 0x0E00
ACC_TYPE_GATE386_CALL equ 0x0C00
ACC_TYPE_SEG         equ 0x1000
ACC_PRESENT          equ 0x8000
ACC_TYPE_CODE_R      equ 0x1a00
ACC_TYPE_CONFORMING  equ 0x0400
ACC_TYPE_DATA_R      equ 0x1000
ACC_TYPE_DATA_W      equ 0x1200
ACC_TYPE_LDT         equ 0x0200
ACC_TYPE_TSS         equ 0x0900

ACC_DPL_0 equ 0x0000
ACC_DPL_1 equ 0x2000
ACC_DPL_2 equ 0x4000
ACC_DPL_3 equ 0x6000

EXT_NONE  equ 0x0000
EXT_16BIT equ EXT_NONE
EXT_32BIT equ 0x0040 ; size bit
EXT_PAGE  equ 0x0080 ; granularity bit

PTE_FRAME     equ 0xfffff000
PTE_DIRTY     equ 0x00000040 ; page has been modified
PTE_ACCESSED  equ 0x00000020 ; page has been accessed
PTE_USER      equ 0x00000004 ; set for user level (CPL 3), clear for supervisor level (CPL 0-2)
PTE_WRITE     equ 0x00000002 ; set for read/write, clear for read-only (affects CPL 3 only)
PTE_PRESENT   equ 0x00000001 ; set for present page, clear for not-present page

PTE_PRESENT_BIT   equ 0000001b
PTE_WRITE_BIT     equ 0000010b
PTE_USER_BIT      equ 0000100b
PTE_ACCESSED_BIT  equ 0100000b
PTE_DIRTY_BIT     equ 1000000b

EX_DE equ 0
EX_DB equ 1
EX_BP equ 3
EX_OF equ 4
EX_BR equ 5
EX_UD equ 6
EX_NM equ 7
EX_DF equ 8
EX_MP equ 9
EX_TS equ 10
EX_NP equ 11
EX_SS equ 12
EX_GP equ 13
EX_PF equ 14
EX_MF equ 15

