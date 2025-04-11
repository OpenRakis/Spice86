.model small

my_data_seg segment
  hello db "HELLO.EXE", 0

  params label word
    dw 0
    dw OFFSET command_line, SEG command_line
    dw 0ffffh,0ffffh ; fcb1
    dw 0ffffh,0ffffh ; fcb2

  command_line db 0,13
my_data_seg ends

my_code_seg segment

free_memory proc
  mov ax, offset my_stack_end
  shr ax, 1
  shr ax, 1
  shr ax, 1
  shr ax, 1
  mov bx, ss
  add bx, ax
  add bx, 2               ; BX = a paragraph beyond program
  mov ax, es              ; ES -> first paragraph of the program (containing PSP)
  sub bx, ax              ; BX = program size in paragraphs
  mov ah, 4ah             ; Resize memory block - http://www.ctyme.com/intr/rb-2936.htm
  int 21h                 ; Call MS-DOS

  ret
free_memory ENDP

execute_hello PROC
  push ds                 ; Save DS
  push es                 ; Save ES

  mov cs:[stk_seg],ss     ; Save stack pointer
  mov cs:[stk_ptr],sp

  mov ax, 4B00h
  mov dx, OFFSET hello
  mov bx, SEG params
  mov es, bx
  mov bx, OFFSET params

  mov ax,4b00h            ; Exec - load and/or execute program - http://www.ctyme.com/intr/rb-2939.htm
  int 21h                 ; Call MS-DOS

  cli                     ; Let no interrupt disturb
  mov ss,cs:[stk_seg]     ; Restore stack pointer
  mov sp,cs:[stk_ptr]
  sti                     ; Allow interrupts

  pop es                  ; Restore ES and DS
  pop ds
  ret

  ; Data for this function in the code segment
  stk_ptr dw 0
  stk_seg dw 0
execute_hello endp

main proc
  mov ax, my_data_seg           ; Initialize DS
  mov ds, ax

  call free_memory        ; ES should point to PSP (default)
  call execute_hello

  mov ax, 4C00h           ; Terminate with return code - http://www.ctyme.com/intr/rb-2974.htm
  int 21h                 ; Call MS-DOS
main endp

my_code_seg ends

my_stack_seg segment stack
  db 100h dup(?)  
my_stack_end:
my_stack_seg ends

end main
