.model small

my_data_seg segment
  msg db 'Hello',0dh,0ah,'$'
my_data_seg ends

my_code_seg segment
start:
  mov ax, my_data_seg
  mov ds, ax      

  mov ah, 09h
  mov dx, offset msg     
  int 21h     

  mov ah, 4Ch     
  int 21h         
my_code_seg ends

my_stack_seg segment stack
  db 100h dup(?)  
my_stack_seg ends

end start
