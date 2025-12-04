; Enhanced Mandelbrot Fractal Benchmark for 8086 (No FPU)
; Uses VGA Mode 13h (320x200 256-color) with color palette
; Progressive refinement with FPS counter
; Pure integer math with fixed-point arithmetic

org 0x100        ; COM file format

section .text
start:
    ; Set VGA Mode 13h (320x200 256 colors)
    mov ax, 0x0013
    int 0x10
    
    ; Setup color palette (256 color gradient)
    call setup_palette
    
    ; Initialize timing
    call init_timer
    
    ; Initialize parameters
    mov byte [max_iterations], 32   ; Start with 32 iterations
    mov word [frame_number], 0
    
main_loop:
    ; Render one frame
    call render_frame
    
    ; Increment frame number
    inc word [frame_number]
    
    ; Update FPS counter
    call update_fps
    
    ; Display stats overlay
    call display_stats
    
    ; Progressive refinement: increase detail every few frames
    mov ax, [frame_number]
    and ax, 0x0007          ; Every 8 frames
    jnz skip_iteration_increase
    
    mov al, [max_iterations]
    cmp al, 128
    jge skip_iteration_increase
    add al, 8
    mov [max_iterations], al
    
skip_iteration_increase:
    ; Check for keypress to exit
    mov ah, 0x01
    int 0x16
    jz main_loop
    
    ; Key pressed, read and exit
    mov ah, 0x00
    int 0x16
    
exit_program:
    ; Restore text mode
    mov ax, 0x0003
    int 0x10
    
    ; Exit to DOS
    mov ax, 0x4C00
    int 0x21

; Setup 256-color palette with smooth gradient
setup_palette:
    push ax
    push bx
    push cx
    push dx
    push di
    push es
    
    ; Build palette in memory first
    push ds
    pop es
    mov di, palette_buffer
    xor cx, cx              ; Color index 0-255
    
palette_loop:
    ; Create smooth color gradient
    mov ax, cx
    
    ; Blue component (0-63 range for VGA DAC)
    mov bx, ax
    and bx, 0x003F
    mov [di+2], bl          ; Blue
    
    ; Green component
    mov bx, ax
    shr bx, 2
    and bx, 0x003F
    mov [di+1], bl          ; Green
    
    ; Red component
    mov bx, ax
    shr bx, 3
    and bx, 0x003F
    mov [di], bl            ; Red
    
    add di, 3
    inc cx
    cmp cx, 256
    jl palette_loop
    
    ; Set palette using INT 10h
    mov ax, 0x1012
    xor bx, bx
    mov cx, 256
    push ds
    pop es
    mov dx, palette_buffer
    int 0x10
    
    pop es
    pop di
    pop dx
    pop cx
    pop bx
    pop ax
    ret

; Initialize timer for FPS calculation
init_timer:
    push ax
    push dx
    
    ; Read BIOS timer tick count
    mov ah, 0x00
    int 0x1A
    mov [last_tick], dx
    mov [frame_count], word 0
    mov [fps], word 0
    
    pop dx
    pop ax
    ret

; Update FPS counter
update_fps:
    push ax
    push cx
    push dx
    
    ; Increment frame counter
    inc word [frame_count]
    
    ; Get current tick
    mov ah, 0x00
    int 0x1A
    
    ; Calculate elapsed ticks
    mov ax, dx
    sub ax, [last_tick]
    
    ; Update FPS every ~18 ticks (1 second)
    cmp ax, 18
    jl fps_done
    
    ; Calculate FPS
    mov ax, [frame_count]
    mov [fps], ax
    
    ; Reset counters
    mov [frame_count], word 0
    mov [last_tick], dx
    
fps_done:
    pop dx
    pop cx
    pop ax
    ret

; Render one frame of Mandelbrot
render_frame:
    push bp
    mov bp, sp
    
    mov word [current_y], 0
    
y_loop:
    mov ax, [current_y]
    cmp ax, 200
    jge render_done
    
    mov word [current_x], 0
    
x_loop:
    mov ax, [current_x]
    cmp ax, 320
    jge next_y
    
    ; Calculate Mandelbrot iteration
    call calc_mandelbrot
    
    ; Plot pixel
    call plot_pixel
    
    inc word [current_x]
    jmp x_loop
    
next_y:
    inc word [current_y]
    jmp y_loop
    
render_done:
    mov sp, bp
    pop bp
    ret

; Calculate Mandelbrot for current pixel
; Uses fixed-point: 16-bit signed, 8.8 format (256 = 1.0)
calc_mandelbrot:
    push bp
    mov bp, sp
    sub sp, 14
    
    ; Map screen coordinates to complex plane
    ; Range: approximately -2.5 to 1.0 (x), -1.25 to 1.25 (y)
    
    ; x0 = (x - 200) * 3 (roughly -2.34 to 1.41 for x=0..320)
    mov ax, [current_x]
    sub ax, 200
    mov bx, ax
    shl ax, 1
    add ax, bx              ; * 3
    mov [bp-2], ax          ; x0 in fixed point
    
    ; y0 = (y - 100) * 2.5 = (y - 100) * 5 / 2
    mov ax, [current_y]
    sub ax, 100
    mov bx, 5
    imul bx
    sar ax, 1               ; Divide by 2
    mov [bp-4], ax          ; y0 in fixed point
    
    ; Initialize iteration
    xor ax, ax
    mov [bp-6], ax          ; x = 0
    mov [bp-8], ax          ; y = 0
    mov byte [bp-10], 0     ; iteration counter
    
iter_loop:
    ; Calculate x*x in fixed point (result in bp-12)
    mov ax, [bp-6]
    imul ax
    ; Result is in DX:AX (16.16), shift right 8 to get 8.8
    mov cl, 8
    call shift_right_dx_ax
    mov [bp-12], ax         ; Store x*x
    
    ; Calculate y*y in fixed point (result in bp-14)
    mov ax, [bp-8]
    imul ax
    ; Result is in DX:AX (16.16), shift right 8 to get 8.8
    mov cl, 8
    call shift_right_dx_ax
    mov [bp-14], ax         ; Store y*y
    
    ; Check if x*x + y*y > 4.0 (1024 in 8.8 format)
    mov ax, [bp-12]
    add ax, [bp-14]
    cmp ax, 1024
    jg iter_escape
    
    ; Check max iterations
    mov al, [bp-10]
    cmp al, [max_iterations]
    jge iter_escape
    
    ; Calculate new x: x*x - y*y + x0
    mov ax, [bp-12]         ; x*x
    sub ax, [bp-14]         ; - y*y
    add ax, [bp-2]          ; + x0
    push ax                 ; Save new_x temporarily
    
    ; Calculate new y: 2*x*y + y0
    mov ax, [bp-6]          ; x
    imul word [bp-8]        ; * y
    ; Result in DX:AX (16.16), shift right 7 (for 8.8 and *2 effect)
    mov cl, 7
    call shift_right_dx_ax
    add ax, [bp-4]          ; + y0
    mov [bp-8], ax          ; y = new_y
    
    pop ax                  ; Retrieve new_x
    mov [bp-6], ax          ; x = new_x
    
    inc byte [bp-10]
    jmp iter_loop
    
iter_escape:
    mov al, [bp-10]
    mov [color], al
    
    mov sp, bp
    pop bp
    ret

; Helper: Shift DX:AX right by CL bits
shift_right_dx_ax:
    push cx
.loop:
    test cl, cl
    jz .done
    shr dx, 1
    rcr ax, 1
    dec cl
    jmp .loop
.done:
    pop cx
    ret

; Plot pixel at current_x, current_y with color
plot_pixel:
    push ax
    push bx
    push dx
    push es
    
    ; Calculate offset: y * 320 + x
    mov ax, [current_y]
    mov dx, 320
    mul dx
    add ax, [current_x]
    mov bx, ax
    
    ; Set ES to VGA memory segment
    mov ax, 0xA000
    mov es, ax
    
    ; Write pixel
    mov al, [color]
    mov es:[bx], al
    
    pop es
    pop dx
    pop bx
    pop ax
    ret

; Display statistics overlay
display_stats:
    push ax
    push bx
    push dx
    
    ; Display FPS at top-left (using text mode INT 10h over graphics)
    mov ah, 0x02
    mov bh, 0
    mov dh, 0
    mov dl, 0
    int 0x10
    
    mov si, fps_msg
    call print_string
    
    mov ax, [fps]
    call print_decimal
    
    ; Display iteration count
    mov ah, 0x02
    mov dh, 1
    mov dl, 0
    int 0x10
    
    mov si, iter_msg
    call print_string
    
    xor ah, ah
    mov al, [max_iterations]
    call print_decimal
    
    pop dx
    pop bx
    pop ax
    ret

; Print null-terminated string at SI
print_string:
    push ax
    push bx
ps_loop:
    lodsb
    test al, al
    jz ps_done
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    jmp ps_loop
ps_done:
    pop bx
    pop ax
    ret

; Print decimal number in AX
print_decimal:
    push ax
    push bx
    push cx
    push dx
    
    mov cx, 0
    mov bx, 10
    
pd_divide:
    xor dx, dx
    div bx
    add dl, '0'
    push dx
    inc cx
    test ax, ax
    jnz pd_divide
    
pd_print:
    pop ax
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    loop pd_print
    
    pop dx
    pop cx
    pop bx
    pop ax
    ret

section .data
fps_msg db 'FPS: ', 0
iter_msg db 'Iter: ', 0

section .bss
palette_buffer resb 768     ; 256 colors * 3 bytes (RGB)
current_x resw 1
current_y resw 1
color resb 1
max_iterations resb 1
frame_count resw 1
frame_number resw 1
last_tick resw 1
fps resw 1
