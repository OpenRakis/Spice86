far_ptr_t struc
  offs dw 0
  segm dw 0
far_ptr_t ends

tsr_info_t struc
  signature dw 0CAFEh
  old_interrupt_21h far_ptr_t<0,0>
  load_segment dw 0
tsr_info_t ends

.model tiny

.code

org 100h ; this is a COM program

start:
  jmp   setup         ; jump to TSR installation

int21h_handler proc far
  cmp ah,9 ; is it print
  jne run_original

  ; ---------------------------------
  ; copy string with $
  ; search for 'l' and replace with 'X'

  push ds
  push es
  push di
  push si
  push ax
  push cx
  push dx

  ; print parameter = ds:dx = string+$
  mov si,dx ; source is ds:si

  ; es:di is dest
  mov ax,cs
  mov es,ax
  mov di,offset buffer

  cld
  mov cx, 16-1 ; max 15
copy_loop:
  lodsb
  cmp al, '$'
  je done
  cmp al, 'l'
  jne not_l
  mov al, 'X'
not_l:
  stosb
  loop copy_loop
done:
  mov al,'$'
  stosb

  mov ax,cs
  mov ds,ax
  mov dx,offset buffer
  mov ah,9

  pushf
  call far ptr cs:[tsr_info.old_interrupt_21h]

  pop dx
  pop cx
  pop ax
  pop si
  pop di
  pop es
  pop ds

  iret

  ; ----------------------------------

run_original:
  jmp far ptr cs:[tsr_info.old_interrupt_21h]   ; jump to original int 21h

  ; local buffer
  buffer db 16 dup('#')
int21h_handler endp

; tsr data
tsr_info TSR_info_t<>

resident_end: ; also contains PSP at start due to "org 100h"

TSR_INFO_OFFSET = offset tsr_info - offset int21h_handler;

current_load_seg dw 0

setup:
  mov bx,ds
  mov current_load_seg,bx

  ; ds == cs

  mov ax,3521h              ; get int 21h
  int 21h

  mov di,bx
  add di,TSR_INFO_OFFSET

  mov ax,es:[di+TSR_info_t.signature]
  cmp ax,0CAFEh
  je uinstall

  ; install

  ; save current int 21h handler
  mov [tsr_info.old_interrupt_21h.offs],bx
  mov [tsr_info.old_interrupt_21h.segm],es

  ; save load-segment for uninstall
  mov ax,current_load_seg
  mov [tsr_info.load_segment],ax

  mov ax,2521h              ; set interrupt vector
  mov dx,offset int21h_handler
  int 21h

  mov ax,3100h
  mov dx,offset resident_end
  mov cl,4
  shr dx,cl
  inc dx
  int 21h

uinstall:
  push ds
  mov ax,es:[di+TSR_info_t.old_interrupt_21h.segm]
  mov ds,ax
  mov dx,es:[di+TSR_info_t.old_interrupt_21h.offs]
  mov ax,2521h
  int 21h
  pop ds

  mov ax,es:[di+TSR_info_t.load_segment]
  mov es,ax
  mov ah,49h
  int 21h

  mov ax,4c00h
  int 21h

END start
