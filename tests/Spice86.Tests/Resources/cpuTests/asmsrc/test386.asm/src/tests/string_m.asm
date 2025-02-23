;
;   Tests store, compare, scan, and move string operands
;   %1 data size b=byte, w=word, d=dword
;   %2 direction 0=increment, 1=decrement
;   %3 addressing a16=16-bit, a32=32-bit
;   DS test segment 1
;   ES test segment 2
;
%macro testStringOps 3

	%assign value 0x12345678
	%ifidni %1,b
		%assign val_size 1
		%define val_mask 0x000000ff
		%define sized_eax al
	%endif
	%ifidni %1,w
		%assign val_size 2
		%define val_mask 0x0000ffff
		%define sized_eax ax
	%endif
	%ifidni %1,d
		%assign val_size 4
		%define val_mask 0xffffffff
		%define sized_eax eax
	%endif

	%if %2 == 0
		cld
		%assign off_value 0x0001ffff-(val_size-1)
		%ifidni %3,a16
			; 16-bit addressing
			%assign off_cmp 0x00010000
		%else
			; 32-bit addressing
			%assign off_cmp 0x00020000
		%endif
	%else
		std
		%assign off_value 0x00010000
		%ifidni %3,a16
			; 16-bit addressing
			%assign off_cmp 0x0001ffff-(val_size-1)
		%else
			; 32-bit addressing
			%assign off_cmp 0x0000ffff-(val_size-1)
		%endif
	%endif

	%ifidni %3,a16
		%assign off_mask 0x0000ffff
	%else
		%assign off_mask 0xffffffff
	%endif

	; VERIFY string operands

	mov    edi, off_value
	mov    ebx, off_value & off_mask
	mov    sized_eax, 0
	mov    [es:ebx], sized_eax
	mov    sized_eax, value
	%3 stos%1         ; STORE EAX in ES:EDI
	cmp    [es:ebx], sized_eax
	jne    error
	cmp    edi, off_cmp
	jne    error

	mov    esi, off_value
	mov    edi, off_value
	mov    ebx, off_value & off_mask
	mov    [ds:ebx], sized_eax
	mov    [es:ebx], sized_eax
	cmp    sized_eax, 0
	je     error
	%3 cmps%1         ; COMPARE ES:EDI with DS:ESI
	jne    error
	cmp    edi, off_cmp
	jne    error
	cmp    esi, off_cmp
	jne    error

	mov    edi, off_value
	mov    sized_eax, value
	mov    [es:ebx], sized_eax
	cmp    sized_eax, 0
	%3 scas%1         ; SCAN/COMPARE ES:EDI with EAX
	jne    error
	cmp    edi, off_cmp
	jne    error

	mov    esi, off_value
	mov    edi, off_value
	mov    sized_eax, value
	mov    [ds:ebx], sized_eax
	mov    sized_eax, 0
	mov    [es:ebx], sized_eax
	%3 movs%1         ; MOVE data from DS:ESI to ES:EDI
	mov    sized_eax, value
	cmp    [es:ebx], sized_eax
	jne    error
	cmp    edi, off_cmp
	jne    error
	cmp    esi, off_cmp
	jne    error

	mov    esi, off_value
	mov    sized_eax, value
	mov    [es:ebx], sized_eax
	xor    eax, eax
	%3 lods%1         ; LOAD data from DS:ESI into EAX
	cmp    sized_eax, value & val_mask
	jne    error
	cmp    esi, off_cmp
	jne    error

%endmacro

;
;   Tests store, compare, scan, and move string operands with repetitions
;   %1 element size b=byte, w=word, d=dword
;   %2 direction 0=increment, 1=decrement
;   %3 addressing a16=16-bit, a32=32-bit
;   DS test segment 1
;   ES test segment 2
;
%macro testStringReps 3

	%assign bytes 0x100

	%ifidni %1,b
		%assign items bytes
	%endif
	%ifidni %1,w
		%assign items bytes/2
	%endif
	%ifidni %1,d
		%assign items bytes/4
	%endif

	%if %2 == 0
		cld
		%assign off_value 0x0001ff00
		%ifidni %3,a16
			; 16-bit addressing
			%assign off_cmp 0x00010000
		%else
			; 32-bit addressing
			%assign off_cmp 0x00020000
		%endif
	%else
		std
		%assign off_value 0x000100ff
		%ifidni %3,a16
			; 16-bit addressing
			%assign off_cmp 0x0001ffff
		%else
			; 32-bit addressing
			%assign off_cmp 0x0000ffff
		%endif
	%endif

	mov    eax, 0x12345678
	mov    esi, off_value
	mov    edi, off_value

	; VERIFY REPs on memory buffers

	; STORE buffers with pattern in EAX
	mov    eax, 0x12345678
	mov    esi, off_value
	mov    edi, off_value
	mov    ecx, items
	%3 rep stos%1          ; store ECX items at ES:EDI with the value in EAX
	cmp    ecx, 0
	jnz    error           ; ECX must be 0
	cmp    edi, off_cmp
	jnz    error
	mov    edi, off_value  ; reset EDI
	; now switch ES:EDI with DS:ESI
	mov    dx, es
	mov    cx, ds
	xchg   dx, cx
	mov    es, dx
	mov    ds, cx
	xchg   edi, esi
	; store again ES:EDI with pattern in EAX
	mov    ecx, items      ; reset ECX
	%3 rep stos%1
	mov    edi, off_value  ; reset EDI

	; COMPARE two buffers
	mov    ecx, items      ; reset ECX
	%3 repe cmps%1         ; find nonmatching items in ES:EDI and DS:ESI
	cmp    ecx, 0
	jnz    error           ; ECX must be 0
	cmp    esi, off_cmp
	jne    error
	cmp    edi, off_cmp
	jne    error
	mov    edi, off_value  ; reset EDI
	mov    esi, off_value  ; reset ESI

	; SCAN buffer for pattern
	mov    ecx, items      ; reset ECX
	%3 repe scas%1         ; SCAN first dword not equal to EAX
	cmp    ecx, 0
	jne    error           ; ECX must be 0
	cmp    edi, off_cmp
	jne    error
	mov    edi, off_value  ; rewind EDI

	; MOVE and COMPARE data between buffers
	; first zero-fill ES:EDI so that we can compare the moved data later
	mov    eax, 0
	mov    ecx, items      ; reset ECX
	%3 rep stos%1          ; zero fill ES:EDI
	mov    edi, off_value  ; reset EDI
	mov    ecx, items      ; reset ECX
	%3 rep movs%1          ; MOVE data from DS:ESI to ES:EDI
	cmp    ecx, 0
	jne    error           ; ECX must be 0
	cmp    esi, off_cmp
	jne    error
	cmp    edi, off_cmp
	jne    error
	mov    ecx, items      ; reset ECX
	mov    edi, off_value  ; reset EDI
	mov    esi, off_value  ; reset ESI
	%3 repe cmps%1         ; COMPARE moved data in ES:EDI with DS:ESI
	cmp    ecx, 0
	jne    error           ; ECX must be 0
	cmp    esi, off_cmp
	jne    error
	cmp    edi, off_cmp
	jne    error
%endmacro
