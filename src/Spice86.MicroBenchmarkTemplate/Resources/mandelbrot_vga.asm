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
    
    ; Initialize zoom/pan
    mov word [zoom_level], 256      ; Start zoom (8.8 fixed point)
    mov word [center_x], -128       ; Center X (-0.5 in 8.8)
    mov word [center_y], 0          ; Center Y (0.0 in 8.8)
    mov byte [max_iterations], 64   ; Start with 64 iterations
    
main_loop:
    ; Render one frame
    call render_frame
    
    ; Update FPS counter
    call update_fps
    
    ; Display stats overlay
    call display_stats
    
    ; Progressive refinement: increase detail each frame
    mov al, [max_iterations]
    cmp al, 255
    jge skip_iteration_increase
    add al, 2
    mov [max_iterations], al
    
skip_iteration_increase:
    ; Slowly zoom in
    mov ax, [zoom_level]
    cmp ax, 32              ; Stop zooming at 0.125 (32/256)
    jle skip_zoom
    sub ax, 1
    mov [zoom_level], ax
    
skip_zoom:
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
    
    mov ax, 0x1012          ; Set block of DAC registers
    xor bx, bx              ; Start at color 0
    mov cx, 256             ; All 256 colors
    push ds
    pop es
    mov dx, palette_data
    int 0x10
    
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
calc_mandelbrot:
    push bp
    mov bp, sp
    sub sp, 10
    
    ; Map screen coordinates to complex plane
    ; x0 = ((x - 160) * scale / zoom) + center_x
    mov ax, [current_x]
    sub ax, 160
    mov bx, [zoom_level]
    cwd
    idiv bx
    add ax, [center_x]
    mov [bp-2], ax          ; x0
    
    ; y0 = ((y - 100) * scale / zoom) + center_y
    mov ax, [current_y]
    sub ax, 100
    mov bx, [zoom_level]
    cwd
    idiv bx
    add ax, [center_y]
    mov [bp-4], ax          ; y0
    
    ; Initialize iteration
    xor ax, ax
    mov [bp-6], ax          ; x = 0
    mov [bp-8], ax          ; y = 0
    mov byte [bp-10], 0     ; iteration counter
    
iter_loop:
    ; Calculate x*x and y*y
    mov ax, [bp-6]
    imul ax                 ; x*x
    sar ax, 8               ; Scale down from 16.16 to 8.8
    mov bx, ax
    
    mov ax, [bp-8]
    imul ax                 ; y*y
    sar ax, 8
    
    ; Check if x*x + y*y > 4 (1024 in 8.8)
    add ax, bx
    cmp ax, 1024
    jg iter_escape
    
    ; Check max iterations
    mov al, [bp-10]
    cmp al, [max_iterations]
    jge iter_escape
    
    ; Calculate new values
    ; temp = x*x - y*y + x0
    mov ax, [bp-6]
    imul ax
    sar ax, 8
    mov bx, ax
    
    mov ax, [bp-8]
    imul ax
    sar ax, 8
    sub bx, ax
    add bx, [bp-2]
    
    ; y = 2*x*y + y0
    mov ax, [bp-6]
    imul word [bp-8]
    sar ax, 7               ; Scale and *2
    add ax, [bp-4]
    mov [bp-8], ax
    
    ; x = temp
    mov [bp-6], bx
    
    inc byte [bp-10]
    jmp iter_loop
    
iter_escape:
    mov al, [bp-10]
    mov [color], al
    
    mov sp, bp
    pop bp
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
    
    ; Display FPS at top-left
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
iter_msg db 'Iterations: ', 0

; Color palette - 256 colors with smooth gradient
palette_data:
    ; Generate RGB values for smooth color gradient
    ; Colors 0-63: Black to Blue
    %assign i 0
    %rep 64
        db (i*0)/63, (i*0)/63, (i*4)/63
        %assign i i+1
    %endrep
    
    ; Colors 64-127: Blue to Cyan
    %assign i 0
    %rep 64
        db (i*0)/63, (i*4)/63, 63
        %assign i i+1
    %endrep
    
    ; Colors 128-191: Cyan to Yellow
    %assign i 0
    %rep 64
        db (i*4)/63, 63, ((63-i)*4)/63
        %assign i i+1
    %endrep
    
    ; Colors 192-255: Yellow to White
    %assign i 0
    %rep 64
        db 63, 63, (i*4)/63
        %assign i i+1
    %endrep

section .bss
current_x resw 1
current_y resw 1
color resb 1
zoom_level resw 1
center_x resw 1
center_y resw 1
max_iterations resb 1
frame_count resw 1
last_tick resw 1
fps resw 1
