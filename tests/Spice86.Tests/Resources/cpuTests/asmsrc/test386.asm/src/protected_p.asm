; Procedures for 32 bit code segment

initIntGateProt:
	initIntGate
	ret

initDescriptorProt:
	initDescriptor
	ret

;
; Defines a Call Gate in GDT
;
;    7                             0 7                             0
;   ╔═══════════════════════════════════════════════════════════════╗
; +7║                  DESTINATION OFFSET 31-16                     ║+6
;   ╟───┬───────┬───┬───────────────┬───────────┬───────────────────╢
; +5║ P │  DPL  │ 0 │ 1   1   0   0 │ x   x   x │  WORD COUNT 4-0   ║+4
;   ╟───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───────────┬───────╢
; +3║                 DESTINATION SELECTOR 15-2             │ x   x ║+2
;   ╟───────────────────────────────┴───────────────────────┴───┴───╢
; +1║                  DESTINATION OFFSET 15-0                      ║ 0
;   ╚═══════════════════════════════╧═══════════════════════════════╝
;    15                                                            0
;
; FS:EBX pointer to the GDT
; EAX GDT selector
; SI  destination selector
; EDI destination offset
; DL word count
; DH DPL (as bit field, use ACC_DPL_* equs on dx)
;
initCallGate:
	and    eax, 0xFFF8
	add    ebx, eax
	mov    word [fs:ebx], di   ; DESTINATION OFFSET 15-0
	mov    word [fs:ebx+2], si ; DESTINATION SELECTOR 15-2
	or     dx, ACC_TYPE_GATE386_CALL | ACC_PRESENT
	mov    word [fs:ebx+4], dx ; ACC byte | WORD COUNT 4-0
	shr    edi, 16
	mov    word [fs:ebx+6], di ; DESTINATION OFFSET 31-16
	ret
