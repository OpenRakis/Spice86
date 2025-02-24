;
; Tests 16-bit and 32-bit PUSH/POP for general purpose registers.
; 50+rw PUSH r16
; 50+rd PUSH r32
; 58+rw POP r16
; 58+rd POP r32
;
; %1: register to be tested, 16-bit name; one of the following:
;     ax, bx, cx, dx, bp, si, di, sp
; %2: stack address size: 16 or 32
;
%macro testPushPopR 2
	%if %1 = bp
		%define sptr eax
	%else
		%define sptr ebp
	%endif

	%if %2 = 16
		%define push16esp 0x2fffe
		%define push32esp 0x2fffc
	%else
		%define push16esp 0x1fffe
		%define push32esp 0x1fffc
	%endif

	%define r16 %1
	%define r32 e%1

	mov    esp, 0x20000          ; ESP := 0x20000
	mov    r32, 0x20000
	lea    sptr, [esp-4]         ; sptr := ESP - 4
	%if %2 = 16
	and    sptr, 0xffff          ; sptr := 0x0FFFC (sptr now mirrors SP instead of ESP)
	%endif

	mov    [sptr], dword 0xdeadbeef
	push   r32                   ; 32-bit PUSH
	cmp    [sptr], dword 0x20000 ; was the push 32-bit and did it use the correct eSP?
	jne    error                 ; no, error
	cmp    esp, push32esp        ; did the push update the correct eSP?
	jne    error                 ; no, error

	mov    [sptr], dword 0xdeadbeef
	pop    r32                   ; 32-bit POP
	cmp    r32, dword 0xdeadbeef
	jne    error

	%if r16 <> sp
	cmp    esp, 0x20000          ; did the pop update the correct eSP?
	jne    error                 ; no, error
	%endif

	mov    r32, 0x20000

	mov    [sptr], dword 0xdeadbeef
	push   r16                   ; 16-bit PUSH
	cmp    [sptr], dword 0x0000beef ; was the push 16-bit and did it use the correct eSP?
	jne    error                 ; no, error
	cmp    esp, push16esp        ; did the push update the correct eSP?
	jne    error                 ; no, error

	mov    [sptr], dword 0xdeadbeef
	pop    r16                   ; 16-bit POP
	cmp    r16, 0xdead
	jne    error

	%if r16 <> sp
	cmp    esp, 0x20000          ; did the pop update the correct eSP?
	jne    error                 ; no, error
	%endif
%endmacro

;
; Tests 16-bit and 32-bit PUSH/POP for segment registers.
;   0E PUSH CS
;   1E PUSH DS
;   1F POP DS
;   16 PUSH SS
;   17 POP SS
;   06 PUSH ES
;   07 POP ES
; 0FA0 PUSH FS
; 0FA1 POP FS
; 0FA8 PUSH GS
; 0FA9 POP GS
;
; %1: register to be tested, one of the following:
;     cs, ds, ss, es, fs, gs
; %2: stack address size: 16 or 32
;
%macro testPushPopSR 2
	%if %2 = 16
		%define push16esp 0x2fffe
		%define push32esp 0x2fffc
	%else
		%define push16esp 0x1fffe
		%define push32esp 0x1fffc
	%endif

	mov    dx, %1                ; save segment register value
	mov    esp, 0x20000
	lea    ebp, [esp-4]
	%if %2 = 16
	and    ebp, 0xffff           ; EBP now mirrors SP instead of ESP
	%endif

	mov    [ebp], dword 0xdeadbeef ; put control dword on stack
	o32 push %1                  ; 32-bit PUSH
	cmp    [ebp], dx             ; was the least significant word correctly written?
	jne    error                 ; no, error
	%if TEST_UNDEF
	; 80386, 80486 perform a 16-bit move, leaving the upper portion of the stack
	; location unmodified (tested on real hardware). Probably all 32-bit Intel
	; CPUs behave in this way, but this behaviour is not specified in the docs
	; for older CPUs and is cited in the most recent docs like this:
	; "If the source operand is a segment register (16 bits) and the operand
	; size is 32-bits, either a zero-extended value is pushed on the stack or
	; the segment selector is written on the stack using a 16-bit move. For the
	; last case, all recent Core and Atom processors perform a 16-bit move,
	; leaving the upper portion of the stack location unmodified."
	cmp    [ebp+2], word 0xdead  ; has the most significant word been overwritten?
	jne    error                 ; yes, error
	%endif
	cmp    esp, push32esp        ; did the push update the correct stack pointer reg?
	jne    error                 ; no, error

	%if %1 <> cs
	mov    [ebp], dword DTEST_SEG_PROT ; write test segment on stack
	o32 pop %1                   ; 32-bit POP
	mov    ax, %1
	cmp    ax, DTEST_SEG_PROT      ; is the popped segment the one on the stack?
	jne    error                 ; no, error
	cmp    esp, 0x20000          ; did the pop update the correct stack pointer reg?
	jne    error                 ; no, error
	mov    %1, dx                ; restore segment
	%else
	mov    esp, 0x20000
	%endif

	mov    [ebp], dword 0xdeadbeef
	o16 push %1                  ; 16-bit PUSH
	cmp    [ebp+2], dx           ; was the push 16-bit and did it use the correct stack pointer reg?
	jne    error                 ; no, error
	cmp    esp, push16esp        ; did the push update the correct stack pointer reg?
	jne    error                 ; no, error

	%if %1 <> cs
	mov    [ebp+2], word DTEST_SEG_PROT ; write test segment on stack
	o16 pop %1                   ; 16-bit POP
	mov    ax, %1
	cmp    ax, DTEST_SEG_PROT      ; is the popped segment the one on the stack?
	jne    error                 ; no, error
	cmp    esp, 0x20000          ; did the pop update the correct stack pointer reg?
	jne    error                 ; no, error
	mov    %1, dx                ; restore segment
	%else
	mov esp, 0x20000
	%endif

%endmacro

;
; Tests 16-bit and 32-bit PUSH/POP with memory operand.
; FF /6 PUSH r/m16
; FF /6 PUSH r/m32
; 8F /0 POP r/m16
; 8F /0 POP r/m32
;
; %1: stack address size: 16 or 32
;
%macro testPushPopM 1

	%if %1 = 16
		%define push16esp 0x2fffe
		%define push32esp 0x2fffc
	%else
		%define push16esp 0x1fffe
		%define push32esp 0x1fffc
	%endif

	mov    esp, 0x20000
	lea    ebp, [esp-4]
	%if %1 = 16
	and    ebp, 0xffff             ; EBP now mirrors SP instead of ESP
	%endif

	lea    esi, [esp-8]            ; init pointer to dword operand in memory
	mov    [esi], dword 0x11223344 ; put test dword in memory

	mov    [ebp], dword 0xdeadbeef ; put control dword on stack
	push   dword [esi]             ; 32-bit PUSH
	cmp    [ebp], dword 0x11223344 ; was it a 32-bit push? did it use the correct pointer?
	jne    error                   ; no, error
	cmp    esp, push32esp          ; did the push update the correct stack pointer reg?
	jne    error                   ; no, error

	mov    [esi], dword 0xdeadbeef ; put control dword in memory
	pop    dword [esi]             ; 32-bit POP
	cmp    [esi], dword 0x11223344 ; was it a 32-bit pop? did it use the correct pointer?
	jne    error                   ; no, error
	cmp    esp, 0x20000            ; did the pop update the correct eSP?
	jne    error                   ; no, error

	mov    [ebp], dword 0xdeadbeef ; put control dword on stack
	push   word [esi]              ; 16-bit PUSH
	cmp    [ebp], dword 0x3344beef ; was it a 16-bit push? did it use the correct pointer?
	jne    error                   ; no, error
	cmp    esp, push16esp          ; did the push update the correct pointer?
	jne    error                   ; no, error

	mov    [esi], dword 0xdeadbeef ; put control dword in memory
	pop    word [esi]              ; 16-bit POP
	cmp    [esi], dword 0xdead3344 ; was it a 16-bit pop? did it use the correct pointer?
	jne    error                   ; no, error
	cmp    esp, 0x20000            ; did the pop update the correct pointer?
	jne    error                   ; no, error
%endmacro

;
; Tests 16-bit PUSHA/POPA
; 60 PUSHA
; 61 POPA
;
; %1: stack address size: 16 or 32
;
%macro testPushPopAll16 1

	%if %1 = 16
		%define push16esp 0x2fff0
	%else
		%define push16esp 0x1fff0
	%endif

	mov    esp, 0x20000
	lea    ebp, [esp-2]
	%if %1 = 16
	and    ebp, 0xffff ; EBP now mirrors SP instead of ESP
	%endif

	; reset stack memory
	mov    ecx, 8
	mov    edi, ebp
%%initstack:
	mov    [edi], word 0xbeef
	sub    edi, 2
	loop   %%initstack

	; init general registers
	mov    eax, 0x11111111
	mov    ecx, 0x22222222
	mov    edx, 0x33333333
	mov    ebx, 0x44444444
	; esp 0x20000
	; ebp 0x0ffff or 0x1ffff
	mov    esi, 0x77777777
	mov    edi, 0x88888888

	o16 pusha ; 16-bit PUSHA

	; verify result
	; order: AX, CX, DX, BX, SP (original value), BP, SI, and DI
	cmp    [ebp], ax
	jne    error
	cmp    [ebp-2], cx
	jne    error
	cmp    [ebp-4], dx
	jne    error
	cmp    [ebp-6], bx
	jne    error
	cmp    [ebp-8], word 0x0000
	jne    error
	cmp    [ebp-10], bp
	jne    error
	cmp    [ebp-12], si
	jne    error
	cmp    [ebp-14], di
	jne    error

	cmp    esp, push16esp
	jne    error

	; put bogus value for SP in the stack so that we can detect if it'll be popped
	mov    [ebp-8], word 0xbeef

	; reset general registers
	mov    eax, 0xdeadbeef
	mov    ecx, 0xdeadbeef
	mov    edx, 0xdeadbeef
	mov    ebx, 0xdeadbeef
	; esp
	mov    ebp, 0xdeadbeef
	mov    esi, 0xdeadbeef
	mov    edi, 0xdeadbeef

	o16 popa ; 16-bit POPA

	; verify result
	; order: AX, CX, DX, BX, SP (original value), BP, SI, and DI
	cmp    eax, 0xdead1111
	jne    error
	cmp    ecx, 0xdead2222
	jne    error
	cmp    edx, 0xdead3333
	jne    error
	cmp    ebx, 0xdead4444
	jne    error
	cmp    esp, 0x20000
	jne    error
	mov    eax, 0xdead0000
	sub    ax, 2
	cmp    ebp, eax
	jne    error
	cmp    esi, 0xdead7777
	jne    error
	cmp    edi, 0xdead8888
	jne    error

%endmacro

;
; Tests 32-bit PUSHA/POPA
; 60 PUSHAD
; 61 POPAD
;
; %1: stack address size: 16 or 32
;
%macro testPushPopAll32 1

	%if %1 = 16
		%define push32esp 0x2ffe0
	%else
		%define push32esp 0x1ffe0
	%endif

	mov    esp, 0x20000
	lea    ebp, [esp-4]
	%if %1 = 16
	and    ebp, 0xffff ; EBP now mirrors SP instead of ESP
	%endif

	; reset stack memory
	mov    ecx, 8
	mov    edi, ebp
%%initstack:
	mov    [edi], dword 0xdeadbeef
	sub    edi, 4
	loop   %%initstack

	; init general registers
	mov    eax, 0x11111111
	mov    ecx, 0x22222222
	mov    edx, 0x33333333
	mov    ebx, 0x44444444
	; esp 0x20000
	; ebp 0x0ffff or 0x1ffff
	mov    esi, 0x77777777
	mov    edi, 0x88888888

	o32 pusha ; 32-bit PUSHA

	; verify result
	; order: EAX, ECX, EDX, EBX, ESP (original value), EBP, ESI, and EDI
	cmp    [ebp], eax
	jne    error
	cmp    [ebp-4], ecx
	jne    error
	cmp    [ebp-8], edx
	jne    error
	cmp    [ebp-12], ebx
	jne    error
	cmp    [ebp-16], dword 0x20000
	jne    error
	cmp    [ebp-20], ebp
	jne    error
	cmp    [ebp-24], esi
	jne    error
	cmp    [ebp-28], edi
	jne    error

	cmp    esp, push32esp
	jne    error

	; put bogus value for eSP in the stack so that we can detect if it'll be popped
	mov    [ebp-16], dword 0xdeadbeef

	; reset general registers
	mov    eax, 0xdeadbeef
	mov    ecx, 0xdeadbeef
	mov    edx, 0xdeadbeef
	mov    ebx, 0xdeadbeef
	; esp
	mov    ebp, 0xdeadbeef
	mov    esi, 0xdeadbeef
	mov    edi, 0xdeadbeef

	o32 popa ; 32-bit POPA

	; verify result
	; order: EAX, ECX, EDX, EBX, ESP (original value), EBP, ESI, and EDI
	cmp    eax, 0x11111111
	jne    error
	cmp    ecx, 0x22222222
	jne    error
	cmp    edx, 0x33333333
	jne    error
	cmp    ebx, 0x44444444
	jne    error
	cmp    esp, 0x20000
	jne    error
	lea    eax, [esp-4]
	%if %1 = 16
	and    eax, 0xffff
	%endif
	cmp    ebp, eax
	jne    error
	cmp    esi, 0x77777777
	jne    error
	cmp    edi, 0x88888888
	jne    error

%endmacro


;
; Tests for PUSH immediate value
;

; 6A ib PUSH imm8
;
; %1: operand size
; %2: byte test value
; %3: memory expected value
; %4: esp expected value
%macro testBytePush 4
	mov    esp, 0x20000
	mov    [ebp], dword 0xdeadbeef
	%1 push byte %2 ; sign extended byte push
	cmp    [ebp], dword %3
	jne    error
	cmp    esp, %4
	jne    error
%endmacro

; 68 iw PUSH imm16
; 68 id PUSH imm32
;
; %1: stack address size: 16 or 32
%macro testPushImm 1

	mov    esp, 0x20000
	lea    ebp, [esp-4]
	%if %1 = 16
	and    ebp, 0xffff ; EBP now mirrors SP instead of ESP
	%endif

	mov    [ebp], dword 0xdeadbeef

	push   dword 0x11223344 ; 32-bit push

	cmp    [ebp], dword 0x11223344
	jne    error
	%if %1 = 16
	cmp    esp, 0x2fffc
	%else
	cmp    esp, 0x1fffc
	%endif
	jne    error

	mov    esp, 0x20000
	mov    [ebp], dword 0xdeadbeef

	push   word 0x1122 ; 16-bit push

	cmp    [ebp], dword 0x1122beef
	jne    error
	%if %1 = 16
	cmp    esp, 0x2fffe
	%else
	cmp    esp, 0x1fffe
	%endif
	jne error

	%if %1 = 16
		%define push16esp 0x2fffe
		%define push32esp 0x2fffc
	%else
		%define push16esp 0x1fffe
		%define push32esp 0x1fffc
	%endif
	testBytePush o16, 0x11, 0x0011beef, push16esp
	testBytePush o32, 0x11, 0x00000011, push32esp
	testBytePush o16, 0x81, 0xff81beef, push16esp
	testBytePush o32, 0x81, 0xffffff81, push32esp

%endmacro


;
; Tests for PUSHF(D)/POPF(D)
;
; NOTE: these macros do not test protection or flags behaviour, only if the
; stack is propery used.
;

; 9D POPF
;
; %1: operand size: 16 or 32
%macro testPopF 1
	push   dword 0
	popfd
	%if %1 = 16
	mov    [ebp+2], word 0x08D7    ; OF,SF,ZF,AF,PF,CF
	%else
	mov    [ebp], dword 0x000008D7 ; OF,SF,ZF,AF,PF,CF
	%endif
	mov    esp, push%1esp
	o%1 popf
	jno    error           ; OF
	lahf
	cmp    ah, 0xD7        ; SF:ZF:0:AF:0:PF:1:CF
	jne    error
	cmp    esp, 0x20000
	jne    error
%endmacro

; 9C PUSHF
;
; %1: operand size: 16 or 32
%macro testPushF 1
	push dword 0
	popfd        ; put all flags to 0
	jo error
	jc error
	jz error
	js error
	jpe error
	; AF unverified?
	mov    esp, 0x20000
	mov    [ebp], dword 0xdeadbeef
	stc
	std
	sti
	o%1 pushf
	%if %1 = 16
	cmp    [ebp], dword 0x0603beef
	%else
	cmp    [ebp], dword 0x00000603
	%endif
	jne    error
	cmp    esp, push%1esp
	jne    error
%endmacro

; %1: stack size: 16 or 32
%macro testPushPopF 1
	%if %1 = 16
		%define push16esp 0x2fffe
		%define push32esp 0x2fffc
	%else
		%define push16esp 0x1fffe
		%define push32esp 0x1fffc
	%endif

	mov    esp, 0x20000
	lea    ebp, [esp-4]
	%if %1 = 16
	and    ebp, 0xffff ; EBP now mirrors SP instead of ESP
	%endif

	testPushF 16
	testPushF 32
	testPopF 16
	testPopF 32
%endmacro
