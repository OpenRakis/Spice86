; Sound Blaster 8-bit PCM Playback Test
; Plays 11025 Hz mono 8-bit PCM audio (440Hz sine wave, 1 second = 11025 samples)
; Based on DOSBox soundblaster.cpp DMA command handling

use16
org 0x100

PCM_SIZE equ 11025

start:
    ; Setup test environment
    mov ax, cs
    mov ds, ax
    mov es, ax
    mov ax, 0x0003
    int 0x10
    mov si, heading
    call print_string
    
    ; Setup DMA channel 1 for Sound Blaster
    ; DMA channel 1: address 0x0002-0x0003, count 0x0004-0x0005, page 0x83
    
    ; Disable DMA channel 1 by masking it
    mov al, 0x05            ; Mask channel 1 (bit 2 set, bits 1-0 = channel)
    out 0x0A, al
    
    ; Set DMA mode for channel 1 (single transfer, read from memory)
    mov al, 0x49            ; Mode: channel 1, read, single transfer
    out 0x0B, al
    
    ; Clear flip-flop
    xor al, al
    out 0x0C, al
    
    ; Calculate physical address for DMA buffer
    mov ax, ds
    mov cl, 4
    shl ax, cl              ; DS * 16
    mov bx, pcm_data
    add ax, bx              ; Add buffer offset
    out 0x02, al            ; DMA address low byte
    mov al, ah
    out 0x02, al            ; DMA address high byte
    
    ; Set DMA count (number of bytes - 1)
    mov ax, PCM_SIZE - 1    ; 11024 (0x2B0F)
    out 0x03, al            ; Low byte
    mov al, ah
    out 0x03, al            ; High byte
    
    ; Set DMA page (bits 16-23 of physical address)
    mov ax, ds
    mov cl, 4
    shr ax, cl
    shr ax, cl
    shr ax, cl              ; DS >> 12 = page number
    out 0x83, al
    
    ; Reset DSP
    mov dx, 0x226           ; Reset port (base + 6)
    mov al, 1
    out dx, al
    mov cx, 100
.reset_wait1:
    loop .reset_wait1
    xor al, al
    out dx, al
    
    ; Wait for DSP ready (0xAA on read data port)
    mov dx, 0x22A           ; Read data port (base + 0xA)
    mov cx, 1000
.wait_ready:
    in al, dx
    cmp al, 0xAA
    je .dsp_ready
    loop .wait_ready
    jmp test_failed
    
.dsp_ready:
    ; Set time constant for 11025 Hz
    ; TC = 256 - (1000000 / sample_rate)
    ; For 11025 Hz: TC = 256 - 90.7 ≈ 165 (0xA5)
    mov dx, 0x22C           ; Write command port (base + 0xC)
    call wait_dsp_write
    
    mov al, 0x40            ; Set time constant command
    out dx, al
    call wait_dsp_write
    
    mov al, 0xA5            ; Time constant value for 11025 Hz
    out dx, al
    
    ; Enable speaker
    call wait_dsp_write
    mov al, 0xD1            ; Speaker on command
    out dx, al
    
    ; Unmask DMA channel 1
    mov al, 0x01            ; Unmask channel 1
    out 0x0A, al
    
    ; Send 8-bit single-cycle DMA command (0x14)
    call wait_dsp_write
    mov al, 0x14            ; 8-bit single-cycle DMA command
    out dx, al
    
    ; Send DMA length (11024 = 0x2B0F)
    call wait_dsp_write
    mov ax, PCM_SIZE - 1
    out dx, al              ; Low byte
    
    call wait_dsp_write
    mov al, ah
    out dx, al              ; High byte
    
    ; Wait for DMA transfer to complete (1 second at 11025 Hz = 11025 samples)
    mov cx, 30000
.wait_transfer:
    mov dx, 0x22E           ; DSP read-buffer status
    in al, dx
    test al, 0x80           ; Check if IRQ pending
    jnz .transfer_complete
    loop .wait_transfer
    
    ; Timeout - DMA transfer did not complete
    jmp test_failed
    
.transfer_complete:
    ; Acknowledge IRQ
    mov dx, 0x22E
    in al, dx
    
    ; Disable speaker
    mov dx, 0x22C
    call wait_dsp_write
    mov al, 0xD3            ; Speaker off command
    out dx, al
    
    ; Test passed
    mov si, success_msg
    call print_string
    mov dx, 0x999
    mov al, 0x00            ; Success
    out dx, al
    hlt
    
test_failed:
    ; Test failed
    mov si, failure_msg
    call print_string
    mov dx, 0x999
    mov al, 0xFF            ; Failure
    out dx, al
    hlt

wait_dsp_write:
    push cx
    push ax
    mov cx, 0xFFFF
    mov dx, 0x22C
.wait:
    in al, dx
    test al, 0x80
    jz .ready
    loop .wait
.ready:
    pop ax
    pop cx
    ret

print_string:
    push ax
    push si
.loop:
    lodsb
    or al, al
    jz .done
    mov ah, 0x0E
    mov bh, 0x00
    mov bl, 0x07
    int 0x10
    jmp .loop
.done:
    pop si
    pop ax
    ret

; Data section
heading:        db 'SB PCM 11025Hz 8bit',13,10,0
success_msg:    db 'PCM PLAYBACK PASS',13,10,0
failure_msg:    db 'PCM PLAYBACK FAIL',13,10,0

; PCM audio data (11025 bytes)
pcm_data:
    incbin "test_sine_440hz_11025_8bit_mono.raw"
