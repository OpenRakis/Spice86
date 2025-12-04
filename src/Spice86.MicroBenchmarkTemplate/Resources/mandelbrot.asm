; Mandelbrot Fractal Benchmark for 8086 (No FPU)
; Uses integer math only with fixed-point arithmetic
; Displays in text mode with ASCII characters for depth levels
; Shows performance metrics: frames rendered and approximate cycles

org 0x100        ; COM file format

section .text
start:
    ; Set video mode to 80x25 text mode
    mov ax, 0x0003
    int 0x10
    
    ; Hide cursor
    mov ah, 0x01
    mov cx, 0x2000
    int 0x10
    
    xor word [frame_count], word 0
    xor word [frame_count+2], word 0
    
main_loop:
    ; Render one frame of Mandelbrot
    call render_mandelbrot
    
    ; Increment frame counter
    add word [frame_count], 1
    adc word [frame_count+2], 0
    
    ; Display performance stats every frame
    call display_stats
    
    ; Check for keypress to exit
    mov ah, 0x01
    int 0x16
    jz main_loop     ; No key pressed, continue
    
    ; Key pressed, read and exit
    mov ah, 0x00
    int 0x16
    
exit_program:
    ; Restore text mode
    mov ax, 0x0003
    int 0x10
    
    ; Show cursor
    mov ah, 0x01
    mov cx, 0x0607
    int 0x10
    
    ; Exit to DOS
    mov ax, 0x4C00
    int 0x21

; Render Mandelbrot fractal
render_mandelbrot:
    push bp
    mov bp, sp
    
    mov byte [current_row], 2    ; Start at row 2 (leave room for stats)
    
row_loop:
    mov al, [current_row]
    cmp al, 24                    ; 24 rows (reserve bottom for stats)
    jge render_done
    
    mov byte [current_col], 0
    
col_loop:
    mov al, [current_col]
    cmp al, 80
    jge next_row
    
    ; Calculate Mandelbrot iteration for this position
    call calc_mandelbrot_point
    
    ; Convert iteration count to ASCII character
    call iter_to_char
    
    ; Display character at current position
    call display_char
    
    inc byte [current_col]
    jmp col_loop
    
next_row:
    inc byte [current_row]
    jmp row_loop
    
render_done:
    mov sp, bp
    pop bp
    ret

; Calculate Mandelbrot iteration for current position
; Uses fixed-point arithmetic (8.8 format)
calc_mandelbrot_point:
    push bp
    mov bp, sp
    sub sp, 8        ; Local variables
    
    ; Map screen coordinates to complex plane
    ; x0 = (col - 40) * scale - 0.5
    ; y0 = (row - 12) * scale
    
    mov al, [current_col]
    sub al, 40
    cbw
    mov cx, 3        ; Scale factor
    imul cx
    sub ax, 128      ; Offset
    mov [bp-2], ax   ; x0
    
    mov al, [current_row]
    sub al, 14
    cbw
    mov cx, 4
    imul cx
    mov [bp-4], ax   ; y0
    
    ; Initialize iteration
    xor ax, ax
    mov [bp-6], ax   ; x = 0
    mov [bp-8], ax   ; y = 0
    
    mov byte [iteration], 0
    
iter_loop:
    ; Check if x*x + y*y > 4 (in fixed point: > 1024)
    mov ax, [bp-6]
    imul ax
    mov bx, ax       ; x*x
    
    mov ax, [bp-8]
    imul ax
    add ax, bx       ; x*x + y*y
    
    cmp ax, 1024
    jg iter_done
    
    ; Check max iterations
    mov al, [iteration]
    cmp al, 16
    jge iter_done
    
    ; Calculate new x and y
    ; temp_x = x*x - y*y + x0
    mov ax, [bp-6]
    imul ax
    mov bx, ax
    
    mov ax, [bp-8]
    imul ax
    sub bx, ax
    
    sar bx, 6        ; Scale down
    add bx, [bp-2]
    
    ; new_y = 2*x*y + y0
    mov ax, [bp-6]
    imul word [bp-8]
    sar ax, 5        ; Scale down and *2
    add ax, [bp-4]
    
    mov [bp-8], ax   ; y = new_y
    mov [bp-6], bx   ; x = temp_x
    
    inc byte [iteration]
    jmp iter_loop
    
iter_done:
    mov sp, bp
    pop bp
    ret

; Convert iteration count to display character
iter_to_char:
    mov al, [iteration]
    cmp al, 16
    jge use_space
    
    ; Map iterations to ASCII gradient
    cmp al, 0
    je use_at
    cmp al, 1
    je use_hash
    cmp al, 2
    je use_percent
    cmp al, 3
    je use_plus
    cmp al, 4
    je use_equals
    cmp al, 5
    je use_minus
    cmp al, 6
    je use_colon
    cmp al, 7
    je use_period
    jmp use_space
    
use_at:
    mov al, '@'
    jmp char_done
use_hash:
    mov al, '#'
    jmp char_done
use_percent:
    mov al, '%'
    jmp char_done
use_plus:
    mov al, '+'
    jmp char_done
use_equals:
    mov al, '='
    jmp char_done
use_minus:
    mov al, '-'
    jmp char_done
use_colon:
    mov al, ':'
    jmp char_done
use_period:
    mov al, '.'
    jmp char_done
use_space:
    mov al, ' '
    
char_done:
    mov [display_char_val], al
    ret

; Display character at current position
display_char:
    mov ah, 0x02     ; Set cursor position
    mov bh, 0        ; Page 0
    mov dh, [current_row]
    mov dl, [current_col]
    int 0x10
    
    mov ah, 0x09     ; Write character
    mov al, [display_char_val]
    mov bh, 0
    mov cx, 1
    mov bl, 0x0F     ; White on black
    int 0x10
    
    ret

; Display performance statistics
display_stats:
    ; Display at top of screen
    mov ah, 0x02
    mov bh, 0
    mov dh, 0
    mov dl, 0
    int 0x10
    
    ; Display title
    mov si, stats_msg
    call print_string
    
    ; Display frame count
    mov ax, [frame_count]
    call print_hex_word
    mov ax, [frame_count+2]
    call print_hex_word
    
    ; Display second line
    mov ah, 0x02
    mov bh, 0
    mov dh, 1
    mov dl, 0
    int 0x10
    
    mov si, press_key_msg
    call print_string
    
    ret

; Print null-terminated string at SI
print_string:
    push ax
    push bx
print_loop:
    lodsb
    test al, al
    jz print_done
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    jmp print_loop
print_done:
    pop bx
    pop ax
    ret

; Print 16-bit hex value in AX
print_hex_word:
    push ax
    push bx
    push cx
    mov cx, 4
hex_loop:
    rol ax, 4
    push ax
    and al, 0x0F
    add al, '0'
    cmp al, '9'
    jle hex_digit
    add al, 7
hex_digit:
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    pop ax
    loop hex_loop
    pop cx
    pop bx
    pop ax
    ret

section .data
stats_msg db 'Mandelbrot Benchmark - Frames: 0x', 0
press_key_msg db 'Press any key to exit', 0

section .bss
frame_count resd 1
current_row resb 1
current_col resb 1
iteration resb 1
display_char_val resb 1
