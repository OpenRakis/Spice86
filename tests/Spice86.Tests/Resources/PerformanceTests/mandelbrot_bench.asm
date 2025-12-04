; Mandelbrot Benchmark for Automated Performance Testing
; Outputs FPS to I/O port 0x99 for headless testing
; Runs for fixed duration then exits with performance data
; Pure 8086 assembly, no FPU

org 0x100        ; COM file format

; I/O Port for performance data output
PERF_PORT equ 0x99

; Test duration in seconds
TEST_DURATION equ 30

section .text
start:
    ; Set VGA Mode 13h (320x200 256 colors)
    mov ax, 0x0013
    int 0x10
    
    ; Setup color palette
    call setup_palette
    
    ; Initialize timing
    call init_timer
    
    ; Initialize test
    mov byte [max_iterations], 32
    mov word [frame_number], 0
    mov word [total_frames], 0
    mov byte [test_running], 1
    
    ; Output test start marker
    mov al, 0xFF
    mov dx, PERF_PORT
    out dx, al
    
main_loop:
    ; Check if test duration elapsed
    call check_test_duration
    cmp byte [test_running], 0
    je test_complete
    
    ; Render one frame
    call render_frame
    
    ; Increment counters
    inc word [frame_number]
    inc word [total_frames]
    
    ; Update and output FPS
    call update_fps
    call output_fps
    
    ; Progressive refinement
    mov ax, [frame_number]
    and ax, 0x0007
    jnz skip_iteration_increase
    
    mov al, [max_iterations]
    cmp al, 128
    jge skip_iteration_increase
    add al, 8
    mov [max_iterations], al
    
skip_iteration_increase:
    ; Check for early exit keypress
    mov ah, 0x01
    int 0x16
    jz main_loop
    
    ; Key pressed - abort test
    mov ah, 0x00
    int 0x16
    jmp exit_program
    
test_complete:
    ; Output final stats
    call output_final_stats
    
exit_program:
    ; Restore text mode
    mov ax, 0x0003
    int 0x10
    
    ; Exit to DOS
    mov ax, 0x4C00
    int 0x21

; Check if test duration has elapsed
check_test_duration:
    push ax
    push dx
    
    ; Get current tick
    mov ah, 0x00
    int 0x1A
    
    ; Calculate elapsed ticks
    mov ax, dx
    sub ax, [start_tick]
    
    ; Check if TEST_DURATION seconds elapsed (18.2 ticks per second)
    mov dx, TEST_DURATION
    mov cx, 18
    push ax
    mov ax, dx
    mul cx
    mov cx, ax
    pop ax
    
    cmp ax, cx
    jl test_still_running
    
    ; Test duration reached
    mov byte [test_running], 0
    
test_still_running:
    pop dx
    pop ax
    ret

; Output current FPS to I/O port
output_fps:
    push ax
    push dx
    
    ; Output FPS value (low byte)
    mov ax, [fps]
    mov dx, PERF_PORT
    out dx, al
    
    ; Output FPS value (high byte)
    mov al, ah
    out dx, al
    
    pop dx
    pop ax
    ret

; Output final statistics
output_final_stats:
    push ax
    push dx
    
    ; Output end marker
    mov al, 0xFE
    mov dx, PERF_PORT
    out dx, al
    
    ; Output total frames (low byte)
    mov ax, [total_frames]
    out dx, al
    
    ; Output total frames (high byte)
    mov al, ah
    out dx, al
    
    ; Output average FPS (calculated from total)
    ; Average FPS = total_frames / TEST_DURATION
    mov ax, [total_frames]
    mov cx, TEST_DURATION
    xor dx, dx
    div cx
    mov dx, PERF_PORT
    out dx, al
    
    pop dx
    pop ax
    ret

; Setup palette
setup_palette:
    push ax
    push bx
    push cx
    push dx
    push di
    push es
    
    push ds
    pop es
    mov di, palette_buffer
    xor cx, cx
    
palette_loop:
    mov ax, cx
    
    ; Blue component
    mov bx, ax
    and bx, 0x003F
    mov [di+2], bl
    
    ; Green component
    mov bx, ax
    shr bx, 2
    and bx, 0x003F
    mov [di+1], bl
    
    ; Red component
    mov bx, ax
    shr bx, 3
    and bx, 0x003F
    mov [di], bl
    
    add di, 3
    inc cx
    cmp cx, 256
    jl palette_loop
    
    ; Set palette
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

; Initialize timer
init_timer:
    push ax
    push dx
    
    mov ah, 0x00
    int 0x1A
    mov [start_tick], dx
    mov [last_tick], dx
    mov [frame_count], word 0
    mov [fps], word 0
    
    pop dx
    pop ax
    ret

; Update FPS
update_fps:
    push ax
    push cx
    push dx
    
    inc word [frame_count]
    
    mov ah, 0x00
    int 0x1A
    
    mov ax, dx
    sub ax, [last_tick]
    
    cmp ax, 18
    jl fps_done
    
    mov ax, [frame_count]
    mov [fps], ax
    
    mov [frame_count], word 0
    mov [last_tick], dx
    
fps_done:
    pop dx
    pop cx
    pop ax
    ret

; Render frame
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
    
    call calc_mandelbrot
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

; Calculate Mandelbrot
calc_mandelbrot:
    push bp
    mov bp, sp
    sub sp, 10
    
    ; Map coordinates
    mov ax, [current_x]
    sub ax, 160
    shl ax, 2
    sub ax, 128
    mov [bp-2], ax
    
    mov ax, [current_y]
    sub ax, 100
    mov bx, ax
    shl ax, 1
    add ax, bx
    mov [bp-4], ax
    
    ; Initialize
    xor ax, ax
    mov [bp-6], ax
    mov [bp-8], ax
    mov byte [bp-10], 0
    
iter_loop:
    ; x*x
    mov ax, [bp-6]
    imul ax
    mov bx, dx
    mov cx, ax
    mov ax, cx
    mov al, ah
    mov ah, bl
    mov [bp-6+2], ax
    
    ; y*y
    mov ax, [bp-8]
    imul ax
    mov bx, dx
    mov cx, ax
    mov ax, cx
    mov al, ah
    mov ah, bl
    mov [bp-8+2], ax
    
    ; Check escape
    mov ax, [bp-6+2]
    add ax, [bp-8+2]
    cmp ax, 1024
    jg iter_escape
    
    ; Check iterations
    mov al, [bp-10]
    cmp al, [max_iterations]
    jge iter_escape
    
    ; New x and y
    mov ax, [bp-6+2]
    sub ax, [bp-8+2]
    add ax, [bp-2]
    mov bx, ax
    
    mov ax, [bp-6]
    imul word [bp-8]
    shl ax, 1
    rcl dx, 1
    mov ax, dx
    add ax, [bp-4]
    mov [bp-8], ax
    
    mov [bp-6], bx
    
    inc byte [bp-10]
    jmp iter_loop
    
iter_escape:
    mov al, [bp-10]
    mov [color], al
    
    mov sp, bp
    pop bp
    ret

; Plot pixel
plot_pixel:
    push ax
    push bx
    push dx
    push es
    
    mov ax, [current_y]
    mov dx, 320
    mul dx
    add ax, [current_x]
    mov bx, ax
    
    mov ax, 0xA000
    mov es, ax
    
    mov al, [color]
    mov es:[bx], al
    
    pop es
    pop dx
    pop bx
    pop ax
    ret

section .bss
palette_buffer resb 768
current_x resw 1
current_y resw 1
color resb 1
max_iterations resb 1
frame_count resw 1
frame_number resw 1
total_frames resw 1
start_tick resw 1
last_tick resw 1
fps resw 1
test_running resb 1
