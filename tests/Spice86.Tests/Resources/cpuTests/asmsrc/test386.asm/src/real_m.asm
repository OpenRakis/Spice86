;
; Advances the base address of data segments used by tests, D1_SEG_REAL and
; D2_SEG_REAL.
;
; Loads DS with D1_SEG_REAL and ES with D2_SEG_REAL.
;
%macro advTestSegReal 0
	advTestBase
	%assign D1_SEG_REAL TEST_BASE1 >> 4
	%assign D2_SEG_REAL TEST_BASE2 >> 4
	mov    dx, D1_SEG_REAL
	mov    ds, dx
	mov    dx, D2_SEG_REAL
	mov    es, dx
%endmacro


;
; Initialises the real mode IDT with C_SEG_REAL:error
;
%macro initRealModeIDT 0
	xor    eax, eax
	mov    ds, ax
	mov    cx, 17
%%loop:
	mov    [eax*4], word error
	mov    [2+eax*4], word C_SEG_REAL
	inc    ax
	loop   %%loop
%endmacro


;
; Exception handling testing in real mode
;

; Initialises an exc handler
; %1: vector
; %2: handler IP
; Trashes AX,DS
%macro realModeExcInit 2
	mov    ax, 0
	mov    ds, ax
	mov    [%1*4], word %2
	mov    [%1*4+2], word C_SEG_REAL
%endmacro

; Checks exc result and restores the default handler
; %1: vector
; %2: expected pushed value of IP
; Trashes AX,DS
%macro realModeExcCheck 2
	cmp    sp, ESP_REAL-6
	jne    error
	cmp    [ss:ESP_REAL-4], word C_SEG_REAL
	cmp    [ss:ESP_REAL-6], word %2
	jne    error
	mov    ax, 0
	mov    ds, ax
	mov    [%1*4], word error
	mov    [%1*4+2], word C_SEG_REAL
%endmacro


; Tests a fault
; %1: vector
; %2: instruction to execute that causes a fault
%macro realModeFaultTest 2+
	realModeExcInit %1, %%continue
	mov    ax, S_SEG_REAL
	mov    ss, ax
	mov    sp, ESP_REAL
%%test:
	%2
	jmp    error
%%continue:
	realModeExcCheck %1, %%test
%endmacro
