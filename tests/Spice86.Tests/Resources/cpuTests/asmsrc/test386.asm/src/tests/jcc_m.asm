;
; Tests conditional relative jumps.
; Uses: AX, ECX, Flags
;
; Opcodes tested, with positive and negative offsets:
;
; rel8  rel16/32 mnemonic condition
; 70    0F80     JO       OF=1
; 71    0F81     JNO      OF=0
; 72    0F82     JC       CF=1
; 73    0F83     JNC      CF=0
; 74    0F84     JZ       ZF=1
; 75    0F85     JNZ      ZF=0
; 76    0F86     JBE      CF=1 || ZF=1
; 77    0F87     JA       CF=0 && ZF=0
; 78    0F88     JS       SF=1
; 79    0F89     JNS      SF=0
; 7A    0F8A     JP       PF=1
; 7B    0F8B     JNP      PF=0
; 7C    0F8C     JL       SF!=OF
; 7D    0F8D     JNL      SF=OF
; 7E    0F8E     JLE      ZF=1 || SF!=OF
; 7F    0F8F     JNLE     ZF=0 && SF=OF
; E3             JCXZ     CX=0
; E3             JECXZ    ECX=0
;
%macro testJcc 1
	mov    ah, PS_CF
	sahf         ; don't use the stack (pushf/popf)
	jnc   %%err1 ; 73 / 0F83   JNC  CF=0
	jc    %%jcok ; 72 / 0F82   JC   CF=1
	hlt
%%jz:
	mov    ah, PS_ZF
	sahf
	jnz   %%err1 ; 75 / 0F85   JNZ  ZF=0
	jz    %%jzok ; 74 / 0F84   JZ   ZF=1
	hlt
%%jp:
	mov    ah, PS_PF
	sahf
	jnp   %%err1 ; 7B / 0F8B   JNP  PF=0
	jp    %%jpok ; 7A / 0F8A   JP   PF=1
	hlt
%%js:
	mov    ah, PS_SF
	sahf
	jns   %%err1 ; 79 / 0F89   JNS  SF=0
	js    %%jsok ; 78 / 0F88   JS   SF=1
	hlt
%%jna:
	mov    ah, PS_ZF|PS_CF
	sahf
	ja    %%err1  ; 77 / 0F87   JA   CF=0 && ZF=0
	jna   %%jnaok ; 76 / 0F86   JBE  CF=1 || ZF=1
%%next1:
	jmp %%jnc

%if %1==16
	times  128 hlt
%endif

%%err1:
	hlt

; test negative offsets
%%jcok:   jc   %%jz
%%jzok:   jz   %%jp
%%jpok:   jp   %%js
%%jsok:   js   %%jna
%%jnaok:  jna  %%next1


%%jnc:
	mov    ah, PS_SF|PS_ZF|PS_AF|PS_PF
	sahf
	mov    ax, 0
	sahf
	jnc   %%jncok ; 73 / 0F83   JNC  CF=0
	hlt
%%jnz:
	mov    ah, PS_SF|PS_AF|PS_PF|PS_CF
	sahf
	jnz   %%jnzok ; 75 / 0F85   JNZ  ZF=0
	hlt
%%jnp:
	mov    ah, PS_SF|PS_ZF|PS_AF|PS_CF
	sahf
	jnp   %%jnpok ; 7B / 0F8B   JNP  PF=0
	hlt
%%jns:
	mov    ah, PS_ZF|PS_AF|PS_PF|PS_CF
	sahf
	jns   %%jnsok ; 79 /  0F89  JNS  SF=0
	hlt
%%ja:
	mov    ah, PS_SF|PS_AF|PS_PF
	sahf
	ja    %%jaok  ; 77 / 0F87   JA   CF=0 && ZF=0
	hlt
%%next2:
	jmp   %%jo

%if %1==16
	times  128 hlt
%endif

; test negative offsets
%%jncok:  jnc  %%jnz
%%jnzok:  jnz  %%jnp
%%jnpok:  jnp  %%jns
%%jnsok:  jns  %%ja
%%jaok:   ja   %%next2

%%jo:
	mov    ah, 0
	sahf
	mov   al, 1000000b
	shl   al, 1    ; OF = high-order bit of AL <> (CF), ZF=0,SF=1,OF=1
	jno   %%err2
	jo    %%jook
	hlt
%%jnl:
	jl    %%err2   ; 7C / 0F8C   JL   SF!=OF
	jnl   %%jnlok  ; 7D / 0F8D   JNL  SF=OF
	hlt
%%jnle:
	jle   %%err2   ; 7E / 0F8E   JLE  ZF=1 || SF!=OF
	jnle  %%jnleok ; 7F / 0F8F   JNLE ZF=0 && SF=OF
	hlt
%%jl:
	mov ah, PS_ZF
	sahf           ; ZF=1,SF=0,OF=1
	jl    %%jlok   ; 7C / 0F8C   JL   SF!=OF
	hlt
%%jle:
	jle   %%jleok  ; 7E / 0F8E   JLE  ZF=1 || SF!=OF
	hlt
%%jcxz:
%if %1==8
	mov ecx, 1
	jcxz %%err2      ; E3   JCXZ  CX=0
	mov ecx, 0x10000
	jcxz %%jcxzok
%%jecxz:
	jecxz %%err2
	mov ecx, 0
	jecxz %%jecxzok ; E3   JECXZ   ECX=0
%%jecxze:
%endif
	jmp %%exit

%if %1==16
	times  128 hlt
%endif

%%err2:
	hlt

; test negative offsets
%%jook:   jo   %%jnl
%%jnlok:  jnl  %%jnle
%%jnleok: jnle %%jl
%%jlok:   jl   %%jle
%%jleok:  jle  %%jcxz
%if %1==8
%%jcxzok:  jcxz  %%jecxz
%%jecxzok: jecxz %%jecxze
%endif

%%exit:
%endmacro

