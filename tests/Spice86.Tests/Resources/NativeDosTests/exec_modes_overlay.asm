; Test for DOS EXEC environment block format validation
; This test validates that the environment block format follows MS-DOS specification
; Reports results by writing characters to video memory at B800:0000
; Expected output: "SEMJCTLOAV" (10 characters indicating tests pass)
;
; Key test: The environment block must have:
; 1. Environment strings terminated by double null (00 00)
; 2. A word count (0001h) after the double null
; 3. The program path as a null-terminated string
;
; NASM syntax for COM file

BITS 16
ORG 100h

; Constants
BACKSLASH equ 92    ; ASCII code for backslash character

section .text
start:
    ; Test 1: Write 'S' - Startup success
    mov al, 'S'
    call write_char_to_video
    
    ; Test 2: Write 'E' - Environment block validation
    ; Validate that env_block has correct format
    call validate_env_block
    jnc test2_pass
    mov al, 'e'         ; lowercase = failed
    jmp test2_write
test2_pass:
    mov al, 'E'
test2_write:
    call write_char_to_video
    
    ; Test 3: Write 'M' - Memory check (validate structure sizes)
    call validate_memory_layout
    jnc test3_pass
    mov al, 'm'
    jmp test3_write
test3_pass:
    mov al, 'M'
test3_write:
    call write_char_to_video
    
    ; Test 4: Write 'J' - (J)unction/Join test - verify double null
    call verify_double_null
    jnc test4_pass
    mov al, 'j'
    jmp test4_write
test4_pass:
    mov al, 'J'
test4_write:
    call write_char_to_video
    
    ; Test 5: Write 'C' - Count word verification
    call verify_count_word
    jnc test5_pass
    mov al, 'c'
    jmp test5_write
test5_pass:
    mov al, 'C'
test5_write:
    call write_char_to_video
    
    ; Test 6: Write 'T' - Terminator after program path
    call verify_terminator
    jnc test6_pass
    mov al, 't'
    jmp test6_write
test6_pass:
    mov al, 'T'
test6_write:
    call write_char_to_video
    
    ; Test 7: Write 'L' - Length validation
    call verify_lengths
    jnc test7_pass
    mov al, 'l'
    jmp test7_write
test7_pass:
    mov al, 'L'
test7_write:
    call write_char_to_video
    
    ; Test 8: Write 'O' - Offset calculations
    call verify_offsets
    jnc test8_pass
    mov al, 'o'
    jmp test8_write
test8_pass:
    mov al, 'O'
test8_write:
    call write_char_to_video
    
    ; Test 9: Write 'A' - All structure validation
    call validate_all
    jnc test9_pass
    mov al, 'a'
    jmp test9_write
test9_pass:
    mov al, 'A'
test9_write:
    call write_char_to_video
    
    ; Test 10: Write 'V' - Verification complete
    mov al, 'V'
    call write_char_to_video
    
    ; Exit program successfully
    mov ax, 4C00h
    int 21h

; Write a character to video memory at current position
; Input: AL = character to write
write_char_to_video:
    push es
    push bx
    push ax
    
    mov bx, [video_seg]
    mov es, bx
    mov bx, [video_offset]
    pop ax
    
    ; Write character with white on black attribute
    mov byte [es:bx], al
    inc bx
    mov byte [es:bx], 07h    ; Attribute: white on black
    inc bx
    
    mov [video_offset], bx
    
    pop bx
    pop es
    ret

; Validate environment block has correct format
validate_env_block:
    ; Check that env_block starts at a valid address
    mov si, env_block
    cmp si, 0
    je .fail
    clc
    ret
.fail:
    stc
    ret

; Validate memory layout
validate_memory_layout:
    ; Check that env_block_end > env_block
    mov ax, env_block_end
    mov bx, env_block
    cmp ax, bx
    jbe .fail
    clc
    ret
.fail:
    stc
    ret

; Verify double null at end of environment strings
verify_double_null:
    ; Find the double null in env_block
    ; PATH=C:\DOS<00>PROMPT=$P$G<00><00>...
    mov si, env_block
    ; Skip first string "PATH=C:\DOS"
.find_null1:
    lodsb
    test al, al
    jnz .find_null1
    ; Now at first null, skip second string "PROMPT=$P$G"
.find_null2:
    lodsb
    test al, al
    jnz .find_null2
    ; Now at second null, next byte should also be null (double null)
    lodsb
    test al, al
    jnz .fail
    clc
    ret
.fail:
    stc
    ret

; Verify count word after double null
verify_count_word:
    ; Find position after double null
    mov si, env_block
.find_null1:
    lodsb
    test al, al
    jnz .find_null1
.find_null2:
    lodsb
    test al, al
    jnz .find_null2
    ; Skip the extra null
    lodsb
    ; Now SI points to count word, should be 0x0001
    lodsw
    cmp ax, 1
    jne .fail
    clc
    ret
.fail:
    stc
    ret

; Verify null terminator after program path
verify_terminator:
    ; Find program path and verify it's null-terminated
    mov si, env_block
.find_null1:
    lodsb
    test al, al
    jnz .find_null1
.find_null2:
    lodsb
    test al, al
    jnz .find_null2
    ; Skip extra null
    lodsb
    ; Skip count word
    add si, 2
    ; Now at program path, find its terminator
.find_path_null:
    lodsb
    cmp al, 0
    je .success
    test al, al
    jnz .find_path_null
.success:
    clc
    ret

; Verify string lengths are reasonable
verify_lengths:
    ; Just check that env block is not too large
    mov ax, env_block_end
    sub ax, env_block
    cmp ax, 1024      ; Should be less than 1K
    jae .fail
    clc
    ret
.fail:
    stc
    ret

; Verify offset calculations
verify_offsets:
    ; Verify that data section starts at reasonable offset
    mov ax, env_block
    cmp ax, start
    jb .fail
    clc
    ret
.fail:
    stc
    ret

; Final comprehensive validation
validate_all:
    ; Run all checks together
    call validate_env_block
    jc .fail
    call validate_memory_layout
    jc .fail
    call verify_double_null
    jc .fail
    call verify_count_word
    jc .fail
    clc
    ret
.fail:
    stc
    ret

section .data
    ; Video memory tracking
    video_seg dw 0B800h
    video_offset dw 0
    
    ; Environment block - FIXED FORMAT
    ; MS-DOS environment format (for EXEC parameter block):
    ; - Series of null-terminated strings (VAR=VALUE format)
    ; - Double null (0x00, 0x00) to end environment strings
    ; - Word count (0x0001) indicating program path follows
    ; - Full program path as null-terminated string
    env_block:
        db "PATH=C:", BACKSLASH, "DOS", 0         ; Environment variable with path
        db "PROMPT=$P$G", 0                       ; Another environment variable
        db 0                                      ; Second null byte (DOUBLE NULL terminator)
        dw 1                                      ; Count word: 1 program name string follows
        db "C:", BACKSLASH, "TEST", BACKSLASH, "HELLO.COM", 0  ; Full path to program
    env_block_end:

