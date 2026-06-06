org 0x100
bits 16

start:
    push cs
    pop ds
    push cs
    pop es

    mov dx, ansi_clear_home
    call print_dos
    mov dx, banner_top
    call print_dos

retry_init:
    call init_mscdex
    jnc playback_loop

    mov dx, ansi_home
    call print_dos
    mov dx, line_state_wait
    call print_dos
    call delay_short
    jmp retry_init

playback_loop:
    call find_next_audio_track
    jnc play_selected

    mov dx, ansi_home
    call print_dos
    mov dx, line_state_no_audio
    call print_dos
    call delay_short
    jmp retry_init

play_selected:
    call play_current_track
    jc retry_init

wait_track_done:
    call delay_poll
    call render_dashboard

    call is_audio_playing
    cmp al, 0
    je advance_track

    call get_current_lba
    mov ebx, [end_lba]
    cmp eax, ebx
    jb wait_track_done

advance_track:
    mov al, [selected_track]
    inc al
    mov bl, [last_track]
    cmp al, bl
    jbe set_search_track
    mov al, [first_track]
set_search_track:
    mov [search_track], al
    jmp playback_loop

init_mscdex:
    mov ax, 0x1500
    int 0x2f
    cmp bx, 0
    jne have_drive
    stc
    ret

have_drive:
    mov [first_drive_index], cx

    mov ax, 0x1501
    mov bx, device_list
    push ds
    pop es
    int 0x2f
    mov al, [device_list]
    mov [subunit], al

    call ioctl_audio_disk_info
    jc init_fail

    mov al, [ioctl_buffer + 1]
    mov [first_track], al
    mov al, [ioctl_buffer + 2]
    mov [last_track], al

    lea si, [ioctl_buffer + 3]
    call msf_to_lba32
    mov [leadout_lba], eax

    mov al, [first_track]
    mov [search_track], al
    clc
    ret

init_fail:
    stc
    ret

find_next_audio_track:
    mov al, [search_track]
    mov [scan_track], al

    mov al, [last_track]
    sub al, [first_track]
    inc al
    xor ah, ah
    mov cx, ax

scan_loop:
    mov al, [scan_track]
    call ioctl_audio_track_info
    jc next_scan

    mov al, [ioctl_buffer + 6]
    test al, 0x40
    jnz next_scan

    mov al, [scan_track]
    mov [selected_track], al

    lea si, [ioctl_buffer + 2]
    call msf_to_lba32
    mov [start_lba], eax

    mov al, [selected_track]
    mov bl, [last_track]
    cmp al, bl
    je use_leadout_end

    inc al
    call ioctl_audio_track_info
    jc use_leadout_end
    lea si, [ioctl_buffer + 2]
    call msf_to_lba32
    mov [end_lba], eax
    jmp have_end

use_leadout_end:
    mov eax, [leadout_lba]
    mov [end_lba], eax

have_end:
    mov eax, [end_lba]
    sub eax, [start_lba]
    jbe next_scan
    mov [length_lba], eax
    clc
    ret

next_scan:
    mov al, [scan_track]
    inc al
    mov bl, [last_track]
    cmp al, bl
    jbe store_scan
    mov al, [first_track]
store_scan:
    mov [scan_track], al
    loop scan_loop

    stc
    ret

play_current_track:
    mov al, [subunit]
    mov [request_packet + 1], al
    mov byte [request_packet + 2], 0x84
    mov word [request_packet + 3], 0
    mov byte [request_packet + 13], 0

    mov eax, [start_lba]
    mov [request_packet + 14], eax
    mov eax, [length_lba]
    mov [request_packet + 18], eax

    mov ax, 0x1510
    mov bx, request_packet
    push ds
    pop es
    int 0x2f

    test word [request_packet + 3], 0x8000
    jz play_ok
    stc
    ret

play_ok:
    clc
    ret

is_audio_playing:
    call ioctl_device_status
    jc not_playing

    mov eax, [ioctl_buffer + 1]
    test eax, 0x00000400
    jz not_playing
    mov al, 1
    ret

not_playing:
    xor al, al
    ret

get_current_lba:
    mov byte [ioctl_buffer + 0], 0x01
    mov byte [ioctl_buffer + 1], 0x00
    call send_ioctl_input
    jc current_lba_failed
    mov eax, [ioctl_buffer + 2]
    ret

current_lba_failed:
    xor eax, eax
    ret

ioctl_audio_disk_info:
    mov byte [ioctl_buffer + 0], 0x0a
    call send_ioctl_input
    ret

ioctl_audio_track_info:
    mov byte [ioctl_buffer + 0], 0x0b
    mov [ioctl_buffer + 1], al
    call send_ioctl_input
    ret

ioctl_device_status:
    mov byte [ioctl_buffer + 0], 0x06
    call send_ioctl_input
    ret

ioctl_channel_control:
    mov byte [ioctl_buffer + 0], 0x04
    call send_ioctl_input
    ret

send_ioctl_input:
    mov al, [subunit]
    mov [request_packet + 1], al
    mov byte [request_packet + 2], 0x03
    mov word [request_packet + 3], 0
    mov byte [request_packet + 13], 0

    mov word [request_packet + 14], ioctl_buffer
    mov word [request_packet + 16], ds

    mov ax, 0x1510
    mov bx, request_packet
    push ds
    pop es
    int 0x2f

    test word [request_packet + 3], 0x8000
    jz ioctl_ok
    stc
    ret

ioctl_ok:
    clc
    ret

render_dashboard:
    call get_current_lba
    mov [current_lba], eax

    mov eax, [current_lba]
    sub eax, [start_lba]
    jns elapsed_ok
    xor eax, eax
elapsed_ok:
    mov [elapsed_lba], eax

    call ioctl_channel_control
    jc default_volume
    mov al, [ioctl_buffer + 2]
    mov [volume_left], al
    mov al, [ioctl_buffer + 4]
    mov [volume_right], al
    jmp have_volume

default_volume:
    mov byte [volume_left], 255
    mov byte [volume_right], 255

have_volume:
    mov dx, ansi_home
    call print_dos
    mov dx, banner_top
    call print_dos

    mov dx, line_state_play
    call print_dos

    mov dx, label_track
    call print_dos
    mov al, [selected_track]
    call print_u8_dec

    mov dx, label_title
    call print_dos
    mov dx, value_title_unknown
    call print_dos

    mov dx, label_elapsed
    call print_dos
    mov eax, [elapsed_lba]
    call print_mm_ss_from_lba

    mov dx, label_length
    call print_dos
    mov eax, [length_lba]
    call print_mm_ss_from_lba

    mov dx, label_volume
    call print_dos
    movzx eax, byte [volume_left]
    call print_u32_dec
    mov dx, slash_space
    call print_dos
    movzx eax, byte [volume_right]
    call print_u32_dec

    mov dx, label_progress
    call print_dos
    call print_progress_bar

    mov dx, label_meter_l
    call print_dos
    mov al, [volume_left]
    call print_live_meter

    mov dx, label_meter_r
    call print_dos
    mov al, [volume_right]
    call print_live_meter

    mov dx, ansi_reset
    call print_dos
    ret

print_progress_bar:
    mov eax, [elapsed_lba]
    mov ebx, 30
    mul ebx

    mov ebx, [length_lba]
    cmp ebx, 0
    jne have_len_for_bar
    xor eax, eax
    jmp bar_value_done

have_len_for_bar:
    div ebx

bar_value_done:
    cmp eax, 30
    jbe bar_clamped
    mov eax, 30
bar_clamped:
    mov [bar_fill], eax

    mov dl, '['
    call print_char

    xor cx, cx
bar_loop:
    cmp cx, 30
    jae bar_done
    mov eax, [bar_fill]
    cmp cx, ax
    jb bar_full
    mov dl, '-'
    call print_char
    inc cx
    jmp bar_loop

bar_full:
    mov dl, '#'
    call print_char
    inc cx
    jmp bar_loop

bar_done:
    mov dl, ']'
    call print_char
    mov dl, 13
    call print_char
    mov dl, 10
    call print_char
    ret

print_live_meter:
    push ax
    mov ah, 0x00
    int 0x1a
    mov al, dl
    and al, 0x0f
    inc al
    mov bl, al

    pop ax
    xor ah, ah
    mul bl
    mov bl, 16
    div bl

    xor ah, ah
    mov bl, 16
    div bl

    mov [meter_fill], al

    mov dl, '['
    call print_char

    xor cx, cx
meter_loop:
    cmp cx, 16
    jae meter_done
    mov al, [meter_fill]
    cmp cl, al
    jb meter_full
    mov dl, '.'
    call print_char
    inc cx
    jmp meter_loop

meter_full:
    mov dl, '='
    call print_char
    inc cx
    jmp meter_loop

meter_done:
    mov dl, ']'
    call print_char
    mov dl, 13
    call print_char
    mov dl, 10
    call print_char
    ret

print_mm_ss_from_lba:
    xor edx, edx
    mov ebx, 75
    div ebx

    xor edx, edx
    mov ebx, 60
    div ebx

    push dx
    call print_u32_dec
    mov dl, ':'
    call print_char
    pop dx
    movzx eax, dl
    cmp eax, 10
    jae print_seconds
    mov dl, '0'
    call print_char
print_seconds:
    movzx eax, dl
    call print_u32_dec
    mov dl, 13
    call print_char
    mov dl, 10
    call print_char
    ret

print_u8_dec:
    xor ah, ah
    movzx eax, al

print_u32_dec:
    cmp eax, 0
    jne convert_loop
    mov dl, '0'
    call print_char
    ret

convert_loop:
    xor cx, cx
conv_step:
    xor edx, edx
    mov ebx, 10
    div ebx
    push dx
    inc cx
    test eax, eax
    jnz conv_step

print_digits:
    pop dx
    add dl, '0'
    call print_char
    loop print_digits
    ret

print_char:
    mov ah, 0x02
    int 0x21
    ret

print_dos:
    mov ah, 0x09
    int 0x21
    ret

msf_to_lba32:
    movzx eax, byte [si + 2]
    imul eax, eax, 4500

    movzx edx, byte [si + 1]
    imul edx, edx, 75
    add eax, edx

    movzx edx, byte [si]
    add eax, edx

    sub eax, 150
    jns lba_done
    xor eax, eax
lba_done:
    ret

delay_poll:
    mov cx, 0x0100
poll_outer:
    mov dx, 0xffff
poll_inner:
    dec dx
    jnz poll_inner
    loop poll_outer
    ret

delay_short:
    mov cx, 0x0600
short_outer:
    mov dx, 0xffff
short_inner:
    dec dx
    jnz short_inner
    loop short_outer
    ret

ansi_clear_home db 27, '[2J', 27, '[H', '$'
ansi_home db 27, '[H', '$'
ansi_reset db 27, '[0m', '$'

banner_top db 27, '[1;36m', 'CDDA MSCDEX LOOP PLAYER', 13, 10,
           db 27, '[0;37m', '----------------------------------------------', 13, 10, '$'
line_state_wait db 27, '[1;33m', 'State: waiting for MSCDEX CD-ROM drive...', 13, 10, '$'
line_state_no_audio db 27, '[1;31m', 'State: no audio tracks found, retrying...', 13, 10, '$'
line_state_play db 27, '[1;32m', 'State: playing', 13, 10, '$'

label_track db 27, '[1;35m', 'Track: ', '$'
label_title db 27, '[1;35m', 'Title: ', '$'
value_title_unknown db 27, '[0;37m', 'Track metadata unavailable on Red Book audio', 13, 10, '$'
label_elapsed db 27, '[1;35m', 'Elapsed: ', '$'
label_length db 27, '[1;35m', 'Length : ', '$'
label_volume db 27, '[1;35m', 'Volume L/R: ', '$'
slash_space db ' / ', 13, 10, '$'
label_progress db 27, '[1;34m', 'Progress: ', '$'
label_meter_l db 27, '[1;33m', 'Live Stereo L: ', '$'
label_meter_r db 27, '[1;33m', 'Live Stereo R: ', '$'

first_drive_index dw 0
subunit db 0
first_track db 0
last_track db 0
search_track db 0
scan_track db 0
selected_track db 0
volume_left db 0
volume_right db 0
meter_fill db 0

bar_fill dd 0
start_lba dd 0
end_lba dd 0
length_lba dd 0
leadout_lba dd 0
current_lba dd 0
elapsed_lba dd 0

device_list times 32 db 0
request_packet times 32 db 0
ioctl_buffer times 32 db 0
