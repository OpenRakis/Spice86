TYPE_ARITH    equ  0 ; multiple values for eAX and eDX
TYPE_ARITH1   equ  1 ; multiple values for eAX, 1 value for eDX
TYPE_ARITH1D  equ  2 ; 1 value for eAX, multiple values for eDX
TYPE_LOGIC    equ  3 ; multiple values for eAX and eDX
TYPE_LOGIC1   equ  4 ; multiple values for eAX, 1 value for eDX
TYPE_LOGIC1D  equ  5 ; 1 value for eAX, multiple values for eDX
TYPE_MULTIPLY equ  6
TYPE_DIVIDE   equ  7
TYPE_SHIFTS_1 equ  8
TYPE_SHIFTS_R equ  9

SIZE_BYTE     equ  0
SIZE_SHORT    equ  1
SIZE_LONG     equ  2

; Defines a logic/arithmetic operation with 1 to 3 operands
; %1 name string
; %2 mnemonic
; %3 "al" / "ax" / "eax" / "dl" / "dx" / "edx"
; %4 src operand / immediate / "mem" / "none"
; %5 3rd operand / "none"
; %6 type
%macro defOp 6
	%ifidni %3,al
	%assign size SIZE_BYTE
	%define msrc dl
	%elifidni %3,dl
	%assign size SIZE_BYTE
	%elifidni %3,ax
	%assign size SIZE_SHORT
	%define msrc dx
	%elifidni %3,dx
	%assign size SIZE_SHORT
	%else
	%assign size SIZE_LONG
	%define msrc edx
	%endif
	db	%%end-%%beg,%6,size
%%name:
	db	%1,' ',0
%%beg:
	%ifidni %4,none
	%2	%3
	%elifidni %4,mem
	mov [0], msrc
	%2	%3,[0]
	%elifidni %5,none
	%2	%3,%4
	%else
	%2	%3,%4,%5
	%endif
	ret
%%end:
%endmacro

; Defines a logic/arithmetic operation with 0 operands
; %1 name string
; %2 mnemonic
; %3 size as "b", "w", or "d"
; %4 type
%macro defOp0 4
	%ifidni %3,b
	%assign size SIZE_BYTE
	%elifidni %3,w
	%assign size SIZE_SHORT
	%else
	%assign size SIZE_LONG
	%endif
	db %%end-%%beg,%4,size
%%name:
	db %1,' ',0
%%beg:
	%2
	ret
%%end:
%endmacro

; Defines a shift operation
; %1 name string
; %2 mnemonic
; %3 register operand
; %4 "cl" or immediate
; %5 type
%macro defOpSh 5
	%ifidni %3,al
	%assign size SIZE_BYTE
	%elifidni %3,ax
	%assign size SIZE_SHORT
	%else ; eax
	%assign size SIZE_LONG
	%endif
	db	%%end-%%beg,%5,size
%%name:
	db	%1,' ',0
%%beg:
	stc
	%ifidni %4,cl
	xchg cl,dl
	%2	%3,cl
	xchg cl,dl
	%else
	%2	%3,%4
	%endif
	ret
%%end:
%endmacro

; Defines a double precision shift operation
; %1 name string
; %2 mnemonic
; %3 operand 1: "ax" or "eax"
; %4 operand 2
; %5 "cl" or immediate
; %6 type
%macro defOpShD 6
	%ifidni %3,ax
	%assign size SIZE_SHORT
	%else ; eax
	%assign size SIZE_LONG
	%endif
	db	%%end-%%beg,%6,size
%%name:
	db	%1,' ',0
%%beg:
	stc
	%ifidni %5,cl
	mov [0],cl
	mov cl,dl
	%2	%3,%4,cl
	mov cl,[0]
	%else
	%2	%3,%4,%5
	%endif
	ret
%%end:
%endmacro

; Defines a INC or DEC operation
; %1 name string
; %2 mnemonic
; %3 register or "mem"
; %3 size as "byte", "word", or "dword"
%macro	defOpInc 4
	%ifidni %4,byte
	%assign size SIZE_BYTE
	%define eAX al
	%elifidni %4,word
	%assign size SIZE_SHORT
	%define eAX ax
	%else
	%assign size SIZE_LONG
	%define eAX eax
	%endif
	db	%%end-%%beg,TYPE_ARITH1,size
%%name:
	db	%1,' ',0
%%beg:
	%ifidni %3,mem
		mov [0], eAX
		%2	%4 [0]
		mov eAX, [0]
	%else
		xchg eAX, %3
		%2	%3
		xchg eAX, %3
	%endif
	ret
%%end:
%endmacro

ALLOPS equ 1

tableOps:
	defOp0   "98 CBW",cbw,b,TYPE_ARITH1                       ; 66 98
	defOp0   "98 CWDE",cwde,w,TYPE_ARITH1                     ;    98
	defOp0   "99 CWD",cwd,w,TYPE_ARITH1                       ; 66 99
	defOp0   "99 CDQ",cdq,d,TYPE_ARITH1                       ;    99
	defOp    "00 ADD",add,al,dl,none,TYPE_ARITH               ;    00 D0
	defOp    "01 ADD",add,ax,dx,none,TYPE_ARITH               ; 66 01 D0
	defOp    "01 ADD",add,eax,edx,none,TYPE_ARITH             ;    01 D0
	defOp    "04 ADD",add,al,0xFF,none,TYPE_ARITH1            ;    04 FF
	defOp    "05 ADD",add,ax,0x8002,none,TYPE_ARITH1          ; 66 05 0280
	defOp    "05 ADD",add,eax,0x80000002,none,TYPE_ARITH1     ;    05 02000080
	defOp    "83 ADD",add,ax,byte 0xFF,none,TYPE_ARITH1       ; 66 83 C0 FF
	defOp    "83 ADD",add,eax,byte 0xFF,none,TYPE_ARITH1      ;    83 C0 FF
	defOp    "80 ADD",add,dl,0xFF,none,TYPE_ARITH1D           ;    80 C2 FF
	defOp    "81 ADD",add,dx,0x8002,none,TYPE_ARITH1D         ; 66 81 C2 0280
	defOp    "81 ADD",add,edx,0x80000002,none,TYPE_ARITH1D    ;    81 C2 02000080
	defOp    "02 ADD",add,al,mem,none,TYPE_ARITH              ;    02 05 00000000
	defOp    "03 ADD",add,ax,mem,none,TYPE_ARITH              ; 66 03 05 00000000
	defOp    "03 ADD",add,eax,mem,none,TYPE_ARITH             ;    03 05 00000000
	defOp    "08 OR",or,al,dl,none,TYPE_LOGIC                 ;    08 D0
	defOp    "09 OR",or,ax,dx,none,TYPE_LOGIC                 ; 66 09 D0
	defOp    "09 OR",or,eax,edx,none,TYPE_LOGIC               ;    09 D0
	defOp    "0C OR",or,al,0xAA,none,TYPE_LOGIC1              ;    0C AA
	defOp    "0D OR",or,ax,0xAAAA,none,TYPE_LOGIC1            ; 66 0D AAAA
	defOp    "0D OR",or,eax,0xAAAAAAAA,none,TYPE_LOGIC1       ;    0D AAAAAAAA
	defOp    "83 OR",or,ax,byte 0xAA,none,TYPE_LOGIC1         ; 66 83 C8 AA
	defOp    "83 OR",or,eax,byte 0xAA,none,TYPE_LOGIC1        ;    83 C8 AA
	defOp    "80 OR",or,dl,0xAA,none,TYPE_LOGIC1D             ;    80 CA AA
	defOp    "81 OR",or,dx,0xAAAA,none,TYPE_LOGIC1D           ; 66 81 CA AAAA
	defOp    "81 OR",or,edx,0xAAAAAAAA,none,TYPE_LOGIC1D      ;    81 CA AAAAAAAA
	defOp    "0A OR",or,al,mem,none,TYPE_LOGIC                ;    0A 05 00000000
	defOp    "0B OR",or,ax,mem,none,TYPE_LOGIC                ; 66 0B 05 00000000
	defOp    "0B OR",or,eax,mem,none,TYPE_LOGIC               ;    0B 05 00000000
	defOp    "10 ADC",adc,al,dl,none,TYPE_ARITH               ;    10 D0
	defOp    "11 ADC",adc,ax,dx,none,TYPE_ARITH               ; 66 11 D0
	defOp    "11 ADC",adc,eax,edx,none,TYPE_ARITH             ;    11 D0
	defOp    "14 ADC",adc,al,0xFF,none,TYPE_ARITH1            ;    14 FF
	defOp    "15 ADC",adc,ax,0x8002,none,TYPE_ARITH1          ; 66 15 0280
	defOp    "15 ADC",adc,eax,0x80000002,none,TYPE_ARITH1     ;    15 02000080
	defOp    "83 ADC",adc,ax,byte 0xFF,none,TYPE_ARITH1       ; 66 83 D0 FF
	defOp    "83 ADC",adc,eax,byte 0xFF,none,TYPE_ARITH1      ;    83 D0 FF
	defOp    "80 ADC",adc,dl,0xFF,none,TYPE_ARITH1D           ;    80 D2 FF
	defOp    "81 ADC",adc,dx,0x8002,none,TYPE_ARITH1D         ; 66 81 D2 0280
	defOp    "81 ADC",adc,edx,0x80000002,none,TYPE_ARITH1D    ;    81 D2 02000080
	defOp    "12 ADC",adc,al,mem,none,TYPE_ARITH              ;    12 05 00000000
	defOp    "13 ADC",adc,ax,mem,none,TYPE_ARITH              ; 66 13 05 00000000
	defOp    "13 ADC",adc,eax,mem,none,TYPE_ARITH             ;    13 05 00000000
	defOp    "18 SBB",sbb,al,dl,none,TYPE_ARITH               ;    18 D0
	defOp    "19 SBB",sbb,ax,dx,none,TYPE_ARITH               ; 66 19 D0
	defOp    "19 SBB",sbb,eax,edx,none,TYPE_ARITH             ;    19 D0
	defOp    "1C SBB",sbb,al,0xFF,none,TYPE_ARITH1            ;    1C FF
	defOp    "1D SBB",sbb,ax,0x8000,none,TYPE_ARITH1          ; 66 1D 0080
	defOp    "1D SBB",sbb,eax,0x80000000,none,TYPE_ARITH1     ;    1D 00000080
	defOp    "83 SBB",sbb,ax,byte 0xFF,none,TYPE_ARITH1       ; 66 83 D8 FF
	defOp    "83 SBB",sbb,eax,byte 0xFF,none,TYPE_ARITH1      ;    83 D8 FF
	defOp    "80 SBB",sbb,dl,0xFF,none,TYPE_ARITH1D           ;    80 DA FF
	defOp    "81 SBB",sbb,dx,0x8000,none,TYPE_ARITH1D         ; 66 81 DA 0080
	defOp    "81 SBB",sbb,edx,0x80000000,none,TYPE_ARITH1D    ;    81 DA 00000080
	defOp    "1A SBB",sbb,al,mem,none,TYPE_ARITH              ;    1A 05 00000000
	defOp    "1B SBB",sbb,ax,mem,none,TYPE_ARITH              ; 66 1B 05 00000000
	defOp    "1B SBB",sbb,eax,mem,none,TYPE_ARITH             ;    1B 05 00000000
	defOp    "20 AND",and,al,dl,none,TYPE_LOGIC               ;    20 D0
	defOp    "21 AND",and,ax,dx,none,TYPE_LOGIC               ; 66 21 D0
	defOp    "21 AND",and,eax,edx,none,TYPE_LOGIC             ;    21 D0
	defOp    "24 AND",and,al,0xAA,none,TYPE_LOGIC1            ;    24 AA
	defOp    "25 AND",and,ax,0xAAAA,none,TYPE_LOGIC1          ; 66 25 AAAA
	defOp    "25 AND",and,eax,0xAAAAAAAA,none,TYPE_LOGIC1     ;    25 AAAAAAAA
	defOp    "83 AND",and,ax,byte 0xAA,none,TYPE_LOGIC1       ; 66 83 E0 AA
	defOp    "83 AND",and,eax,byte 0xAA,none,TYPE_LOGIC1      ;    83 E0 AA
	defOp    "80 AND",and,dl,0xAA,none,TYPE_LOGIC1D           ;    80 E2 AA
	defOp    "81 AND",and,dx,0xAAAA,none,TYPE_LOGIC1D         ; 66 81 E2 AAAA
	defOp    "81 AND",and,edx,0xAAAAAAAA,none,TYPE_LOGIC1D    ;    81 E2 AAAAAAAA
	defOp    "22 AND",and,al,mem,none,TYPE_LOGIC              ;    22 05 00000000
	defOp    "23 AND",and,ax,mem,none,TYPE_LOGIC              ; 66 23 05 00000000
	defOp    "23 AND",and,eax,mem,none,TYPE_LOGIC             ;    23 05 00000000
	defOp    "28 SUB",sub,al,dl,none,TYPE_ARITH               ;    28 D0
	defOp    "29 SUB",sub,ax,dx,none,TYPE_ARITH               ; 66 29 D0
	defOp    "29 SUB",sub,eax,edx,none,TYPE_ARITH             ;    29 D0
	defOp    "2C SUB",sub,al,0xFF,none,TYPE_ARITH1            ;    2C FF
	defOp    "2D SUB",sub,ax,0x8000,none,TYPE_ARITH1          ; 66 2D 0080
	defOp    "2D SUB",sub,eax,0x80000000,none,TYPE_ARITH1     ;    2D 00000080
	defOp    "83 SUB",sub,ax,byte 0xFF,none,TYPE_ARITH1       ; 66 83 E8 FF
	defOp    "83 SUB",sub,eax,byte 0xFF,none,TYPE_ARITH1      ;    83 E8 FF
	defOp    "80 SUB",sub,dl,0xFF,none,TYPE_ARITH1D           ;    80 EA FF
	defOp    "81 SUB",sub,dx,0x8000,none,TYPE_ARITH1D         ; 66 81 EA 0080
	defOp    "81 SUB",sub,edx,0x80000000,none,TYPE_ARITH1D    ;    81 EA 00000080
	defOp    "2A SUB",sub,al,mem,none,TYPE_ARITH              ;    2A 05 00000000
	defOp    "2B SUB",sub,ax,mem,none,TYPE_ARITH              ; 66 2B 05 00000000
	defOp    "2B SUB",sub,eax,mem,none,TYPE_ARITH             ;    2B 05 00000000
	defOp    "30 XOR",xor,al,dl,none,TYPE_LOGIC               ;    30 D0
	defOp    "31 XOR",xor,ax,dx,none,TYPE_LOGIC               ; 66 31 D0
	defOp    "31 XOR",xor,eax,edx,none,TYPE_LOGIC             ;    31 D0
	defOp    "34 XOR",xor,al,0xAA,none,TYPE_LOGIC1            ;    34 AA
	defOp    "35 XOR",xor,ax,0xAAAA,none,TYPE_LOGIC1          ; 66 35 AAAA
	defOp    "35 XOR",xor,eax,0xAAAAAAAA,none,TYPE_LOGIC1     ;    35 AAAAAAAA
	defOp    "83 XOR",xor,ax,byte 0xAA,none,TYPE_LOGIC1       ; 66 83 F0 AA
	defOp    "83 XOR",xor,eax,byte 0xAA,none,TYPE_LOGIC1      ;    83 F0 AA
	defOp    "80 XOR",xor,dl,0xAA,none,TYPE_LOGIC1D           ;    80 F2 AA
	defOp    "81 XOR",xor,dx,0xAAAA,none,TYPE_LOGIC1D         ; 66 81 F2 AAAA
	defOp    "81 XOR",xor,edx,0xAAAAAAAA,none,TYPE_LOGIC1D    ;    81 F2 AAAAAAAA
	defOp    "32 XOR",xor,al,mem,none,TYPE_LOGIC              ;    32 05 00000000
	defOp    "33 XOR",xor,ax,mem,none,TYPE_LOGIC              ; 66 33 05 00000000
	defOp    "33 XOR",xor,eax,mem,none,TYPE_LOGIC             ;    33 05 00000000
	defOp    "38 CMP",cmp,al,dl,none,TYPE_LOGIC               ;    38 D0
	defOp    "39 CMP",cmp,ax,dx,none,TYPE_LOGIC               ; 66 39 D0
	defOp    "39 CMP",cmp,eax,edx,none,TYPE_LOGIC             ;    39 D0
	defOp    "3C CMP",cmp,al,0xAA,none,TYPE_LOGIC1            ;    3C AA
	defOp    "3D CMP",cmp,ax,0xAAAA,none,TYPE_LOGIC1          ; 66 3D AAAA
	defOp    "3D CMP",cmp,eax,0xAAAAAAAA,none,TYPE_LOGIC1     ;    3D AAAAAAAA
	defOp    "83 CMP",cmp,ax,byte 0xAA,none,TYPE_LOGIC1       ; 66 83 F8 AA
	defOp    "83 CMP",cmp,eax,byte 0xAA,none,TYPE_LOGIC1      ;    83 F8 AA
	defOp    "80 CMP",cmp,dl,0xAA,none,TYPE_LOGIC1D           ;    80 FA AA
	defOp    "81 CMP",cmp,dx,0xAAAA,none,TYPE_LOGIC1D         ; 66 81 FA AAAA
	defOp    "81 CMP",cmp,edx,0xAAAAAAAA,none,TYPE_LOGIC1D    ;    81 FA AAAAAAAA
	defOp    "3A CMP",cmp,al,mem,none,TYPE_LOGIC              ;    3A 05 00000000
	defOp    "3B CMP",cmp,ax,mem,none,TYPE_LOGIC              ; 66 3B 05 00000000
	defOp    "3B CMP",cmp,eax,mem,none,TYPE_LOGIC             ;    3B 05 00000000
	defOp    "84 TEST",test,al,dl,none,TYPE_LOGIC             ;    84 D0
	defOp    "85 TEST",test,ax,dx,none,TYPE_LOGIC             ; 66 85 D0
	defOp    "85 TEST",test,eax,edx,none,TYPE_LOGIC           ;    85 D0
	defOp    "A8 TEST",test,al,0xAA,none,TYPE_LOGIC1          ;    A8 AA
	defOp    "A9 TEST",test,ax,0xAAAA,none,TYPE_LOGIC1        ; 66 A9 AAAA
	defOp    "A9 TEST",test,eax,0xAAAAAAAA,none,TYPE_LOGIC1   ;    A9 AAAAAAAA
	defOp    "F6 TEST",test,dl,0xAA,none,TYPE_LOGIC1D         ;    F6 C2 AA
	defOp    "F7 TEST",test,dx,0xAAAA,none,TYPE_LOGIC1D       ; 66 F7 C2 AAAA
	defOp    "F7 TEST",test,edx,0xAAAAAAAA,none,TYPE_LOGIC1D  ;    F7 C2 AAAAAAAA
	defOpInc "40 INC",inc,ax,word                             ; 66 40
	defOpInc "41 INC",inc,cx,word                             ; 66 41
	defOpInc "42 INC",inc,dx,word                             ; 66 42
	defOpInc "43 INC",inc,bx,word                             ; 66 43
	defOpInc "44 INC",inc,sp,word                             ; 66 44
	defOpInc "45 INC",inc,bp,word                             ; 66 45
	defOpInc "46 INC",inc,si,word                             ; 66 46
	defOpInc "47 INC",inc,di,word                             ; 66 47
	defOpInc "40 INC",inc,eax,dword                           ;    40
	defOpInc "41 INC",inc,ecx,dword                           ;    41
	defOpInc "42 INC",inc,edx,dword                           ;    42
	defOpInc "43 INC",inc,ebx,dword                           ;    43
	defOpInc "44 INC",inc,esp,dword                           ;    44
	defOpInc "45 INC",inc,ebp,dword                           ;    45
	defOpInc "46 INC",inc,esi,dword                           ;    46
	defOpInc "47 INC",inc,edi,dword                           ;    47
	defOpInc "FE INC",inc,mem,byte                            ;    FE 05 00000000
	defOpInc "FF INC",inc,mem,word                            ; 66 FF 05 00000000
	defOpInc "FF INC",inc,mem,dword                           ;    FF 05 00000000
	defOpInc "48 DEC",dec,ax,word                             ; 66 48
	defOpInc "49 DEC",dec,cx,word                             ; 66 49
	defOpInc "4A DEC",dec,dx,word                             ; 66 4A
	defOpInc "4B DEC",dec,bx,word                             ; 66 4B
	defOpInc "4C DEC",dec,sp,word                             ; 66 4C
	defOpInc "4D DEC",dec,bp,word                             ; 66 4D
	defOpInc "4E DEC",dec,si,word                             ; 66 4E
	defOpInc "4F DEC",dec,di,word                             ; 66 4F
	defOpInc "48 DEC",dec,eax,dword                           ;    48
	defOpInc "49 DEC",dec,ecx,dword                           ;    49
	defOpInc "4A DEC",dec,edx,dword                           ;    4A
	defOpInc "4B DEC",dec,ebx,dword                           ;    4B
	defOpInc "4C DEC",dec,esp,dword                           ;    4C
	defOpInc "4D DEC",dec,ebp,dword                           ;    4D
	defOpInc "4E DEC",dec,esi,dword                           ;    4E
	defOpInc "4F DEC",dec,edi,dword                           ;    4F
	defOpInc "FE DEC",dec,mem,byte                            ;    FE 0D 00000000
	defOpInc "FF DEC",dec,mem,word                            ; 66 FF 0D 00000000
	defOpInc "FF DEC",dec,mem,dword                           ;    FF 0D 00000000
	defOp    "F6 NEG",neg,al,none,none,TYPE_ARITH1            ;    F6 D8
	defOp    "F7 NEG",neg,ax,none,none,TYPE_ARITH1            ; 66 F7 D8
	defOp    "F7 NEG",neg,eax,none,none,TYPE_ARITH1           ;    F7 D8
	defOp    "F6 NOT",not,al,none,none,TYPE_LOGIC1            ;    F6 D0
	defOp    "F7 NOT",not,ax,none,none,TYPE_LOGIC1            ; 66 F7 D0
	defOp    "F7 NOT",not,eax,none,none,TYPE_LOGIC1           ;    F7 D0
	defOp    "F6 MUL",mul,dl,none,none,TYPE_MULTIPLY          ;    F6 E2
	defOp    "F7 MUL",mul,dx,none,none,TYPE_MULTIPLY          ; 66 F7 E2
	defOp    "F7 MUL",mul,edx,none,none,TYPE_MULTIPLY         ;    F7 E2
	defOp    "F6 IMUL",imul,dl,none,none,TYPE_MULTIPLY        ;    F6 EA
	defOp    "F7 IMUL",imul,dx,none,none,TYPE_MULTIPLY        ; 66 F7 EA
	defOp    "F7 IMUL",imul,edx,none,none,TYPE_MULTIPLY       ;    F7 EA
	defOp    "0FAF IMUL",imul,ax,dx,none,TYPE_MULTIPLY        ; 66 0FAF C2
	defOp    "0FAF IMUL",imul,eax,edx,none,TYPE_MULTIPLY      ;    0FAF C2
	defOp    "6B IMUL",imul,ax,dx,0x77,TYPE_MULTIPLY          ; 66 6B C2 77
	defOp    "6B IMUL",imul,ax,dx,-0x77,TYPE_MULTIPLY         ; 66 6B C2 89
	defOp    "6B IMUL",imul,eax,edx,0x77,TYPE_MULTIPLY        ;    6B C2 77
	defOp    "6B IMUL",imul,eax,edx,-0x77,TYPE_MULTIPLY       ;    6B C2 89
	defOp    "69 IMUL",imul,ax,0x777,none,TYPE_MULTIPLY       ; 66 69 C0 7707
	defOp    "69 IMUL",imul,eax,0x777777,none,TYPE_MULTIPLY   ;    69 C0 77777700
	defOp    "F6 DIV",div,dl,none,none,TYPE_DIVIDE            ;    F6 F2
	defOp    "F7 DIV",div,dx,none,none,TYPE_DIVIDE            ; 66 F7 F2
	defOp    "F7 DIV",div,edx,none,none,TYPE_DIVIDE           ;    F7 F2
	defOp    "F6 DIV",div,al,none,none,TYPE_DIVIDE            ;    F6 F0
	defOp    "F7 DIV",div,ax,none,none,TYPE_DIVIDE            ; 66 F7 F0
	defOp    "F7 DIV",div,eax,none,none,TYPE_DIVIDE           ;    F7 F0
	defOp    "F6 IDIV",idiv,dl,none,none,TYPE_DIVIDE          ;    F6 FA
	defOp    "F7 IDIV",idiv,dx,none,none,TYPE_DIVIDE          ; 66 F7 FA
	defOp    "F7 IDIV",idiv,edx,none,none,TYPE_DIVIDE         ;    F7 FA
	defOp    "F6 IDIV",idiv,al,none,none,TYPE_DIVIDE          ;    F6 F8
	defOp    "F7 IDIV",idiv,ax,none,none,TYPE_DIVIDE          ; 66 F7 F8
	defOp    "F7 IDIV",idiv,eax,none,none,TYPE_DIVIDE         ;    F7 F8
	defOpSh  "D0 SAL",sal,al,1,TYPE_SHIFTS_1                  ;    D0 E0
	defOpSh  "D1 SAL",sal,ax,1,TYPE_SHIFTS_1                  ; 66 D1 E0
	defOpSh  "D1 SAL",sal,eax,1,TYPE_SHIFTS_1                 ;    D1 E0
	defOpSh  "C0 SAL",sal,al,7,TYPE_SHIFTS_R                  ;    C0 E007
	defOpSh  "C1 SAL",sal,ax,7,TYPE_SHIFTS_R                  ; 66 C1 E007
	defOpSh  "C1 SAL",sal,eax,7,TYPE_SHIFTS_R                 ;    C1 E007
	defOpSh  "D2 SAL",sal,al,cl,TYPE_SHIFTS_R                 ;    D2 E0
	defOpSh  "D3 SAL",sal,ax,cl,TYPE_SHIFTS_R                 ; 66 D3 E0
	defOpSh  "D3 SAL",sal,eax,cl,TYPE_SHIFTS_R                ;    D3 E0
	defOpSh  "D0 SAR",sar,al,1,TYPE_SHIFTS_1                  ;    D0 F8
	defOpSh  "D1 SAR",sar,ax,1,TYPE_SHIFTS_1                  ; 66 D1 F8
	defOpSh  "D1 SAR",sar,eax,1,TYPE_SHIFTS_1                 ;    D1 F8
	defOpSh  "C0 SAR",sar,al,7,TYPE_SHIFTS_R                  ;    C0 F807
	defOpSh  "C1 SAR",sar,ax,7,TYPE_SHIFTS_R                  ; 66 C1 F807
	defOpSh  "C1 SAR",sar,eax,7,TYPE_SHIFTS_R                 ;    C1 F807
	defOpSh  "D2 SAR",sar,al,cl,TYPE_SHIFTS_R                 ;    D2 F8
	defOpSh  "D3 SAR",sar,ax,cl,TYPE_SHIFTS_R                 ; 66 D3 F8
	defOpSh  "D3 SAR",sar,eax,cl,TYPE_SHIFTS_R                ;    D3 F8
	defOpSh  "D0 SHR",shr,al,1,TYPE_SHIFTS_1                  ;    D0 E8
	defOpSh  "D1 SHR",shr,ax,1,TYPE_SHIFTS_1                  ; 66 D1 E8
	defOpSh  "D1 SHR",shr,eax,1,TYPE_SHIFTS_1                 ;    D1 E8
	defOpSh  "C0 SHR",shr,al,7,TYPE_SHIFTS_R                  ;    C0 E807
	defOpSh  "C1 SHR",shr,ax,7,TYPE_SHIFTS_R                  ; 66 C1 E807
	defOpSh  "C1 SHR",shr,eax,7,TYPE_SHIFTS_R                 ;    C1 E807
	defOpSh  "D2 SHR",shr,al,cl,TYPE_SHIFTS_R                 ;    D2 E8
	defOpSh  "D3 SHR",shr,ax,cl,TYPE_SHIFTS_R                 ; 66 D3 E8
	defOpSh  "D3 SHR",shr,eax,cl,TYPE_SHIFTS_R                ;    D3 E8
	defOpSh  "D0 ROL",rol,al,1,TYPE_SHIFTS_1                  ;    D0 C0
	defOpSh  "D1 ROL",rol,ax,1,TYPE_SHIFTS_1                  ; 66 D1 C0
	defOpSh  "D1 ROL",rol,eax,1,TYPE_SHIFTS_1                 ;    D1 C0
	defOpSh  "C0 ROL",rol,al,7,TYPE_SHIFTS_1                  ;    C0 C007
	defOpSh  "C1 ROL",rol,ax,7,TYPE_SHIFTS_1                  ; 66 C1 C007
	defOpSh  "C1 ROL",rol,eax,7,TYPE_SHIFTS_1                 ;    C1 C007
	defOpSh  "D2 ROL",rol,al,cl,TYPE_SHIFTS_R                 ;    D2 C0
	defOpSh  "D3 ROL",rol,ax,cl,TYPE_SHIFTS_R                 ; 66 D3 C0
	defOpSh  "D3 ROL",rol,eax,cl,TYPE_SHIFTS_R                ;    D3 C0
	defOpSh  "D0 ROR",ror,al,1,TYPE_SHIFTS_1                  ;    D0 C8
	defOpSh  "D1 ROR",ror,ax,1,TYPE_SHIFTS_1                  ; 66 D1 C8
	defOpSh  "D1 ROR",ror,eax,1,TYPE_SHIFTS_1                 ;    D1 C8
	defOpSh  "C0 ROR",ror,al,7,TYPE_SHIFTS_1                  ;    C0 C807
	defOpSh  "C1 ROR",ror,ax,7,TYPE_SHIFTS_1                  ; 66 C1 C807
	defOpSh  "C1 ROR",ror,eax,7,TYPE_SHIFTS_1                 ;    C1 C807
	defOpSh  "D2 ROR",ror,al,cl,TYPE_SHIFTS_R                 ;    D2 C8
	defOpSh  "D3 ROR",ror,ax,cl,TYPE_SHIFTS_R                 ; 66 D3 C8
	defOpSh  "D3 ROR",ror,eax,cl,TYPE_SHIFTS_R                ;    D3 C8
	defOpSh  "D0 RCL",rcl,al,1,TYPE_SHIFTS_1                  ;    D0 D0
	defOpSh  "D1 RCL",rcl,ax,1,TYPE_SHIFTS_1                  ; 66 D1 D0
	defOpSh  "D1 RCL",rcl,eax,1,TYPE_SHIFTS_1                 ;    D1 D0
	defOpSh  "C0 RCL",rcl,al,7,TYPE_SHIFTS_1                  ;    C0 D007
	defOpSh  "C1 RCL",rcl,ax,7,TYPE_SHIFTS_1                  ; 66 C1 D007
	defOpSh  "C1 RCL",rcl,eax,7,TYPE_SHIFTS_1                 ;    C1 D007
	defOpSh  "D2 RCL",rcl,al,cl,TYPE_SHIFTS_R                 ;    D2 D0
	defOpSh  "D3 RCL",rcl,ax,cl,TYPE_SHIFTS_R                 ; 66 D3 D0
	defOpSh  "D3 RCL",rcl,eax,cl,TYPE_SHIFTS_R                ;    D3 D0
	defOpSh  "D0 RCR",rcr,al,1,TYPE_SHIFTS_1                  ;    D0 D8
	defOpSh  "D1 RCR",rcr,ax,1,TYPE_SHIFTS_1                  ; 66 D1 D8
	defOpSh  "D1 RCR",rcr,eax,1,TYPE_SHIFTS_1                 ;    D1 D8
	defOpSh  "C0 RCR",rcr,al,7,TYPE_SHIFTS_1                  ;    C0 D807
	defOpSh  "C1 RCR",rcr,ax,7,TYPE_SHIFTS_1                  ; 66 C1 D807
	defOpSh  "C1 RCR",rcr,eax,7,TYPE_SHIFTS_1                 ;    C1 D807
	defOpSh  "D2 RCR",rcr,al,cl,TYPE_SHIFTS_R                 ;    D2 D8
	defOpSh  "D3 RCR",rcr,ax,cl,TYPE_SHIFTS_R                 ; 66 D3 D8
	defOpSh  "D3 RCR",rcr,eax,cl,TYPE_SHIFTS_R                ;    D3 D8
	defOpShD "0FA4 SHLD",shld,ax,dx,8,TYPE_SHIFTS_R           ; 66 0FA4 D0 08
	defOpShD "0FA4 SHLD",shld,eax,edx,16,TYPE_SHIFTS_R        ;    0FA4 D0 10
	defOpShD "0FA5 SHLD",shld,ax,dx,cl,TYPE_SHIFTS_R          ; 66 0FA5 D0
	defOpShD "0FA5 SHLD",shld,eax,edx,cl,TYPE_SHIFTS_R        ;    0FA5 D0
	defOpShD "0FAC SHRD",shrd,ax,dx,8,TYPE_SHIFTS_R           ; 66 0FAC D0 08
	defOpShD "0FAC SHRD",shrd,eax,edx,16,TYPE_SHIFTS_R        ;    0FAC D0 10
	defOpShD "0FAD SHRD",shrd,ax,dx,cl,TYPE_SHIFTS_R          ; 66 0FAD D0
	defOpShD "0FAD SHRD",shrd,eax,edx,cl,TYPE_SHIFTS_R        ;    0FAD D0

	db 0

	align	4

typeMasks:
	dd PS_ARITH
	dd PS_ARITH
	dd PS_ARITH
	dd PS_LOGIC
	dd PS_LOGIC
	dd PS_LOGIC
	dd PS_MULTIPLY
	dd PS_DIVIDE
	dd PS_SHIFTS_1
	dd PS_SHIFTS_R

arithValues:
.bvals:	dd	0x00,0x01,0x02,0x7E,0x7F,0x80,0x81,0xFE,0xFF
	ARITH_BYTES equ ($-.bvals)/4

.wvals:	dd	0x0000,0x0001,0x0002,0x7FFE,0x7FFF,0x8000,0x8001,0xFFFE,0xFFFF
	ARITH_WORDS equ ($-.wvals)/4

.dvals:	dd	0x00000000,0x00000001,0x00000002,0x7FFFFFFE,0x7FFFFFFF,0x80000000,0x80000001,0xFFFFFFFE,0xFFFFFFFF
	ARITH_DWORDS equ ($-.dvals)/4

logicValues:
.bvals:	dd	0x00,0x01,0x55,0xAA,0x5A,0xA5,0xFF
	LOGIC_BYTES equ ($-.bvals)/4

.wvals:	dd	0x0000,0x0001,0x5555,0xAAAA,0x5A5A,0xA5A5,0xFFFF
	LOGIC_WORDS equ ($-.wvals)/4

.dvals:	dd	0x00000000,0x00000001,0x55555555,0xAAAAAAAA,0x5A5A5A5A,0xA5A5A5A5,0xFFFFFFFF
	LOGIC_DWORDS equ ($-.dvals)/4

muldivValues:
.bvals:	dd	0x00,0x01,0x02,0x3F,0x40,0x41,0x7E,0x7F,0x80,0x81,0xFE,0xFF
	MULDIV_BYTES equ ($-.bvals)/4

.wvals:	dd	0x0000,0x0001,0x0002,0x3FFF,0x4000,0x4001,0x7FFE,0x7FFF,0x8000,0x8001,0xFFFE,0xFFFF
	MULDIV_WORDS equ ($-.wvals)/4

.dvals:	dd	0x00000000,0x00000001,0x00000002,0x3FFFFFFF,0x40000000,0x40000001,0x7FFFFFFE,0x7FFFFFFF,0x80000000,0x80000001,0xFFFFFFFE,0xFFFFFFFF
	MULDIV_DWORDS equ ($-.dvals)/4

shiftsValues:
.bvals:	dd	0x00,0x01,0x02,0x7E,0x7F,0x80,0x81,0xFE,0xFF
	SHIFTS_BYTES equ ($-.bvals)/4

.wvals:	dd	0x0000,0x0001,0x0181,0x7FFE,0x7FFF,0x8000,0x8001,0xFFFE,0xFFFF
	SHIFTS_WORDS equ ($-.wvals)/4

.dvals:	dd	0x00000000,0x00000001,0x00018001,0x7FFFFFFE,0x7FFFFFFF,0x80000000,0x80000001,0xFFFFFFFE,0xFFFFFFFF
	SHIFTS_DWORDS equ ($-.dvals)/4

shiftsValuesR:
.bvals:	dd	0x00,0x01,0x02,0x08
	SHIFTS_BYTES_R equ ($-.bvals)/4

.wvals:	dd	0x8000,0x8001,0x8002,0x8010
	SHIFTS_WORDS_R equ ($-.wvals)/4

.dvals:	dd	0x80000000,0x80000001,0x80000002,0x8000001F,0x80000020
	SHIFTS_DWORDS_R equ ($-.dvals)/4


typeValues:
	;
	; Values for TYPE_ARITH
	;
	dd	ARITH_BYTES,arithValues,ARITH_BYTES,arithValues
	dd	ARITH_BYTES+ARITH_WORDS,arithValues,ARITH_BYTES+ARITH_WORDS,arithValues
	dd	ARITH_BYTES+ARITH_WORDS+ARITH_DWORDS,arithValues,ARITH_BYTES+ARITH_WORDS+ARITH_DWORDS,arithValues
	dd	0,0,0,0
	;
	; Values for TYPE_ARITH1
	;
	dd	ARITH_BYTES,arithValues,1,arithValues
	dd	ARITH_BYTES+ARITH_WORDS,arithValues,1,arithValues
	dd	ARITH_BYTES+ARITH_WORDS+ARITH_DWORDS,arithValues,1,arithValues
	dd	0,0,0,0
	;
	; Values for TYPE_ARITH1D
	;
	dd	1,arithValues,ARITH_BYTES,arithValues
	dd	1,arithValues,ARITH_BYTES+ARITH_WORDS,arithValues
	dd	1,arithValues,ARITH_BYTES+ARITH_WORDS+ARITH_DWORDS,arithValues
	dd	0,0,0,0
	;
	; Values for TYPE_LOGIC
	;
	dd	LOGIC_BYTES,logicValues,LOGIC_BYTES,logicValues
	dd	LOGIC_BYTES+LOGIC_WORDS,logicValues,LOGIC_BYTES+LOGIC_WORDS,logicValues
	dd	LOGIC_BYTES+LOGIC_WORDS+LOGIC_DWORDS,logicValues,LOGIC_BYTES+LOGIC_WORDS+LOGIC_DWORDS,logicValues
	dd	0,0,0,0
	;
	; Values for TYPE_LOGIC1
	;
	dd	LOGIC_BYTES,logicValues,1,logicValues
	dd	LOGIC_BYTES+LOGIC_WORDS,logicValues,1,logicValues
	dd	LOGIC_BYTES+LOGIC_WORDS+LOGIC_DWORDS,logicValues,1,logicValues
	dd	0,0,0,0
	;
	; Values for TYPE_LOGIC1D
	;
	dd	1,logicValues,LOGIC_BYTES,logicValues
	dd	1,logicValues,LOGIC_BYTES+LOGIC_WORDS,logicValues
	dd	1,logicValues,LOGIC_BYTES+LOGIC_WORDS+LOGIC_DWORDS,logicValues
	dd	0,0,0,0
	;
	; Values for TYPE_MULTIPLY (a superset of ARITH values)
	;
	dd	MULDIV_BYTES,muldivValues,MULDIV_BYTES,muldivValues
	dd	MULDIV_BYTES+MULDIV_WORDS,muldivValues,MULDIV_BYTES+MULDIV_WORDS,muldivValues
	dd	MULDIV_BYTES+MULDIV_WORDS+MULDIV_DWORDS,muldivValues,MULDIV_BYTES+MULDIV_WORDS+MULDIV_DWORDS,muldivValues
	dd	0,0,0,0
	;
	; Values for TYPE_DIVIDE
	;
	dd	MULDIV_BYTES,muldivValues,MULDIV_BYTES,muldivValues
	dd	MULDIV_BYTES+MULDIV_WORDS,muldivValues,MULDIV_BYTES+MULDIV_WORDS,muldivValues
	dd	MULDIV_BYTES+MULDIV_WORDS+MULDIV_DWORDS,muldivValues,MULDIV_BYTES+MULDIV_WORDS+MULDIV_DWORDS,muldivValues
	dd	0,0,0,0
	;
	; Values for TYPE_SHIFTS_1
	;
	dd	SHIFTS_BYTES,shiftsValues,1,shiftsValues
	dd	SHIFTS_BYTES+SHIFTS_WORDS,shiftsValues,1,shiftsValues
	dd	SHIFTS_BYTES+SHIFTS_WORDS+SHIFTS_DWORDS,shiftsValues,1,shiftsValues
	dd	0,0,0,0
	;
	; Values for TYPE_SHIFTS_R
	;
	dd	SHIFTS_BYTES,shiftsValues,SHIFTS_BYTES_R,shiftsValuesR
	dd	SHIFTS_BYTES+SHIFTS_WORDS,shiftsValues,SHIFTS_BYTES_R+SHIFTS_WORDS_R,shiftsValuesR
	dd	SHIFTS_BYTES+SHIFTS_WORDS+SHIFTS_DWORDS,shiftsValues,SHIFTS_BYTES_R+SHIFTS_WORDS_R+SHIFTS_DWORDS_R,shiftsValuesR
	dd	0,0,0,0
