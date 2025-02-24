%macro COM_OUT 2
mov al, %2
mov dx, [cs:COMTHRports+(COM_PORT-1)*2]
add dx, %1
out dx, al
%endmacro

	%if COM_PORT
	; Initialize the serial port
	COM_OUT 1, 0x00     ; Disable interrupts
	COM_OUT 3, 0x80     ; Enable DLAB (set baud rate divisor)
	COM_OUT 0, COM_PORT_DIV & 0xff ; Set divisor low byte
	COM_OUT 1, COM_PORT_DIV >> 8   ; Set divisor high byte
	COM_OUT 3, 0x03     ; Disable DLAB, 8 bits, no parity, one stop bit
	COM_OUT 2, 0x00     ; Disable FIFO
	COM_OUT 4, 0x00     ; IRQs disabled, RTS/DTR disabled
	%endif

	%if LPT_PORT && IBM_PS1
	; Enable output to LPT port for PS/1 computers
	mov    ax, 0xff7f  ; bit 7 = 0  setup functions
	out    94h, al     ; system board enable/setup register
	mov    dx, 102h
	in     al, dx      ; al = p[102h] POS register 2
	or     al, 0x91    ; enable LPT1 on port 3BCh, normal mode
	out    dx, al
	mov    al, ah
	out    94h, al     ; bit 7 = 1  enable functions
	%endif

	%if LPT_PORT
	; Initialize the printer
	mov    dx, [cs:LPTports+(LPT_PORT-1)*2]
	add    dx, 2       ; CONTROL register
	mov    al, 0x08    ; SELECT, -INIT, IRQ disabled
	out    dx, al
	jmp    $+2
	mov    al, 0x0C    ; SELECT, IRQ disabled
	out    dx, al
	%endif
