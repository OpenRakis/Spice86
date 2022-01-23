;;; Segment code (0C00:0100)

;; fn0C00_0100: 0C00:0100
fn0C00_0100 proc
	mov	sp,0A0h
	mov	ax,0FFFFh
	mov	bx,1h
	add	bx,ax
	mov	[0000h],ax
	mov	[0002h],bx
	pushf
	mov	dx,0FFFFh
	mov	word ptr [0004h],0FFFFh
	add	[0004h],dx
	mov	[0006h],dx
	pushf
	mov	cx,1h
	mov	word ptr [0008h],2h
	add	cx,[0008h]
	mov	[000Ah],cx
	pushf
	mov	ax,1h
	add	ax,7FFFh
	mov	[000Ch],ax
	pushf
	mov	bp,8000h
	add	bp,0FFh
	mov	[000Eh],bp
	pushf
	mov	si,0C783h
	add	si,0EB2Ah
	mov	[0010h],si
	pushf
	mov	word ptr [0012h],8960h
	add	word ptr [0012h],0A95h
	pushf
	mov	word ptr [0014h],0F1E1h
	add	word ptr [0014h],64h
	pushf
	mov	byte ptr [0016h],1h
	add	byte ptr [0016h],0FFh
	pushf
	mov	dh,0FFh
	add	dh,0FFh
	mov	[0017h],dx
	pushf
	mov	al,1h
	add	al,2h
	mov	[0019h],ax
	pushf
	mov	byte ptr [001Bh],7Fh
	mov	ch,1h
	add	ch,[001Bh]
	mov	[001Ch],cx
	pushf
	mov	bl,80h
	mov	byte ptr [001Eh],0FFh
	add	[001Eh],bl
	mov	[001Fh],bx
	pushf
	mov	al,0A6h
	mov	ah,86h
	add	ah,al
	mov	[0021h],ax
	pushf
	mov	ax,0FFFFh
	mov	bx,1h
	adc	bx,ax
	mov	[0023h],ax
	mov	[0025h],bx
	pushf
	mov	dx,0FFFFh
	mov	word ptr [0027h],0FFFFh
	adc	[0027h],dx
	mov	[0029h],dx
	pushf
	mov	cx,1h
	mov	word ptr [002Bh],2h
	adc	cx,[002Bh]
	mov	[002Dh],cx
	pushf
	mov	ax,1h
	adc	ax,7FFFh
	mov	[002Fh],ax
	pushf
	mov	bp,8000h
	adc	bp,0FFh
	mov	[0031h],bp
	pushf
	mov	si,77D3h
	adc	si,8425h
	mov	[0033h],si
	pushf
	mov	word ptr [0035h],0EBA0h
	adc	word ptr [0035h],0D3C1h
	pushf
	mov	word ptr [0037h],7F50h
	adc	word ptr [0037h],0F5h
	pushf
	mov	byte ptr [0039h],1h
	adc	byte ptr [0039h],0FFh
	pushf
	mov	dh,0FFh
	adc	dh,0FFh
	mov	[003Ah],dx
	pushf
	mov	al,1h
	adc	al,2h
	mov	[003Ch],ax
	pushf
	mov	byte ptr [003Eh],7Fh
	mov	ch,1h
	adc	ch,[003Eh]
	mov	[003Fh],cx
	pushf
	mov	bl,80h
	mov	byte ptr [0041h],0FFh
	adc	[0041h],bl
	mov	[0042h],bx
	pushf
	mov	al,0B9h
	mov	ah,0D3h
	adc	ah,al
	mov	[0044h],ax
	pushf
	mov	di,0FFFFh
	inc	di
	mov	[0046h],di
	pushf
	mov	bp,7FFFh
	inc	bp
	mov	[0048h],bp
	pushf
	mov	word ptr [004Ah],7412h
	inc	word ptr [004Ah]
	pushf
	mov	dl,7Fh
	inc	dl
	mov	[004Ch],dx
	pushf
	mov	byte ptr [004Dh],0FFh
	inc	byte ptr [004Dh]
	pushf
	mov	byte ptr [004Eh],0B5h
	inc	byte ptr [004Eh]
	pushf
	hlt
0C00:02A9                            00 00 00 00 00 00 00          .......
0C00:02B0 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 ................
; ...
1C00:00F0 EB 0E 00 00 00 00 00 00 00 00 00 00 00 00 00 FF ................
