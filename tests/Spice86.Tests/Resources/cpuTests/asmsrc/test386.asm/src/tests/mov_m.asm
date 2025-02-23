;
;   Tests MOV from segment registers in real mode
;
;   %1 the segment register to test
;
%macro testMovSegR_real 1
	%if %1 = cs
	mov    dx, C_SEG_REAL
	%else
	mov    dx, D1_SEG_REAL
	%endif

	; MOV reg to Sreg
	%if %1 = cs
	realModeFaultTest EX_UD, mov %1,dx ; test for #UD
	%else
	mov    %1, dx
	%endif

	; MOV Sreg to 16 bit reg
	xor    ax, ax
	mov    ax, %1
	cmp    ax, dx
	jne    error

	; MOV Sreg to 32 bit reg
	mov    eax, -1
	mov    eax, %1
	; bits 31:16 are undefined for Pentium and earlier processors.
	; TODO: verify on real hw and check TEST_UNDEF
	cmp    ax, dx
	jne    error

	; MOV Sreg to word mem
	mov    [0], word 0xbeef
	mov    [0], %1
	cmp    [0], dx
	jne    error

	; MOV word mem to Sreg
	%if %1 = cs
	realModeFaultTest EX_UD, mov %1,[0] ; test for #UD
	%else
	mov    cx, ds ; save current DS in CX
	xor    ax, ax
	mov    %1, ax
	%if %1 = ds
	mov    es, cx
	mov    %1, [es:0]
	%else
	mov    %1, [0]
	%endif
	mov    ax, %1
	cmp    ax, dx
	jne    error
	%endif

%endmacro


%macro testMovSegR_prot 1
	mov    edx, -1
	%if %1 = cs
	mov    dx, C_SEG_PROT32
	%else
	mov    dx, D1_SEG_PROT
	%endif

	; MOV reg to Sreg
	%if %1 = cs
	loadProtModeStack
	protModeFaultTest EX_UD, 0, mov %1,dx ; #UD: attempt is made to load the CS register.
	%else
	mov    %1, dx
	%endif

	; MOV Sreg to 16 bit reg
	xor    ax, ax
	mov    ax, %1
	cmp    ax, dx
	jne    error

	; MOV Sreg to 32 bit reg
	mov    eax, -1
	mov    eax, %1
	; bits 31:16 are undefined for Pentium and earlier processors.
	; TODO: verify on real hw and check TEST_UNDEF
	cmp    ax, dx
	jne    error

	; MOV Sreg to word mem
	mov    [0], dword -1
	mov    [0], %1
	cmp    [0], edx
	jne    error

	; MOV word mem to Sreg
	%if %1 = cs
	protModeFaultTest EX_UD, 0, mov %1,[0] ; test for #UD
	%else
	mov    cx, ds ; save current DS in CX
	mov    ax, DTEST_SEG_PROT
	mov    %1, ax
	%if %1 = ds
	mov    es, cx
	mov    %1, [es:0]
	%else
	mov    %1, [0]
	%endif
	mov    ax, %1
	cmp    ax, dx
	jne    error
	%endif

	loadProtModeStack
	%if %1 = ss
	; #GP(0) If attempt is made to load SS register with NULL segment selector.
	mov ax, NULL
	protModeFaultTest EX_GP, 0, mov %1,ax
	; #GP(selector) If the SS register is being loaded and the segment selector's RPL and the segment descriptor’s DPL are not equal to the CPL.
	mov ax, DPL1_SEG_PROT|1
	protModeFaultTest EX_GP, DPL1_SEG_PROT, mov %1,ax
	; #GP(selector) If the SS register is being loaded and the segment pointed to is a non-writable data segment.
	mov ax, RO_SEG_PROT
	protModeFaultTest EX_GP, RO_SEG_PROT, mov %1,ax
	; #SS(selector) If the SS register is being loaded and the segment pointed to is marked not present.
	mov ax, NP_SEG_PROT
	protModeFaultTest EX_SS, NP_SEG_PROT, mov %1,ax
	%endif
	%if %1 != cs
	; #GP(selector) If segment selector index is outside descriptor table limits.
	mov ax, 0xFFF8
	protModeFaultTest EX_GP, 0xfff8, mov %1,ax
	%if %1 != ss
	; #NP(selector) If the DS, ES, FS, or GS register is being loaded and the segment pointed to is marked not present.
	mov ax, NP_SEG_PROT
	protModeFaultTest EX_NP, NP_SEG_PROT, mov %1,ax
	; #GP(selector) If the DS, ES, FS, or GS register is being loaded and the segment pointed to is not a data or readable code segment.
	mov ax, SYS_SEG_PROT
	protModeFaultTest EX_GP, SYS_SEG_PROT, mov %1,ax
	; #GP(selector)
	; If the DS, ES, FS, or GS register is being loaded and the segment pointed to is a data or nonconforming code segment, but both the RPL and the CPL are greater than the DPL.
	call switchToRing3 ; CPL=3
	mov ax, DTEST_SEG_PROT|3 ; RPL=3,DPL=0
	protModeFaultTest EX_GP, DTEST_SEG_PROT, mov %1,ax
	call switchToRing0
	%endif
	%endif

%endmacro
