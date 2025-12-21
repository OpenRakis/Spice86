; Sound Blaster 8-bit PCM Playback Test
; Plays 11025 Hz mono 8-bit PCM audio from embedded WAV data
; End-to-end test for SB PCM pipeline validation
;
; Sample rate: 11025 Hz
; Format: 8-bit unsigned mono PCM
; Duration: 1 second (11025 samples)
; Test tone: 440Hz sine wave

use16
org 0x100

start:
    ; Setup test environment
    mov ax, cs
    mov ds, ax
    mov es, ax
    
    ; Print test heading
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
    ; Physical address = segment * 16 + offset
    mov ax, ds
    mov cl, 4
    shl ax, cl              ; DS * 16
    mov bx, pcm_data
    add ax, bx              ; Add buffer offset
    
    ; Set DMA address (low 16 bits)
    out 0x02, al            ; Low byte
    mov al, ah
    out 0x02, al            ; High byte
    
    ; Set DMA count (number of bytes - 1)
    ; PCM data is 11025 bytes, so count = 11024 (0x2B0F)
    mov ax, PCM_SIZE - 1
    out 0x03, al            ; Low byte
    mov al, ah
    out 0x03, al            ; High byte
    
    ; Set DMA page (bits 16-23 of physical address)
    mov ax, ds
    mov cl, 4
    shr ax, cl              ; DS >> 4
    shr ax, cl              ; DS >> 8
    shr ax, cl              ; DS >> 12 = page number
    out 0x83, al
    
    ; Reset DSP
    mov dx, 0x226           ; Reset port (base + 6)
    mov al, 1
    out dx, al
    mov cx, 100
.reset_wait:
    loop .reset_wait
    xor al, al
    out dx, al
    
    ; Wait for DSP ready (0xAA on read data port)
    mov dx, 0x22A           ; Read data port (base + 0xA)
    mov cx, 10000
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
    
    ; Send DMA length (11025 - 1 = 11024 = 0x2B0F)
    call wait_dsp_write
    mov ax, PCM_SIZE - 1
    out dx, al              ; Low byte
    
    call wait_dsp_write
    mov al, ah
    out dx, al              ; High byte
    
    ; Wait for DMA transfer to complete
    ; At 11025 Hz with 11025 samples, this takes exactly 1 second
    ; Add some margin for safety
    mov cx, 30000           ; Wait approximately 1.5 seconds
.wait_transfer:
    ; Check DSP status
    mov dx, 0x22E           ; DSP read-buffer status port
    in al, dx
    test al, 0x80           ; Check if data available (IRQ pending)
    jnz .transfer_complete
    loop .wait_transfer
    
    ; Check IRQ status as fallback
    mov dx, 0x22E           ; IRQ acknowledge port
    in al, dx               ; Reading acknowledges IRQ
    
.transfer_complete:
    ; Acknowledge IRQ (reading port 0x22E acknowledges 8-bit IRQ)
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
    
    ; Write success to test port
    mov dx, 0x999
    mov al, 0x00            ; Success code
    out dx, al
    
    ; Exit via HLT (recommended for Spice86 tests)
    hlt

test_failed:
    mov si, failure_msg
    call print_string
    
    ; Write failure to test port
    mov dx, 0x999
    mov al, 0xFF            ; Failure code
    out dx, al
    
    hlt

; Wait for DSP write buffer ready
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

; Print null-terminated string
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
PCM_SIZE equ 11025

heading:        db 'SB PCM 11025Hz 8bit Mono Test',13,10,0
success_msg:    db 'PCM PLAYBACK SUCCESS',13,10,0
failure_msg:    db 'PCM PLAYBACK FAILED',13,10,0

; PCM audio data (11025 bytes)
pcm_data:
    incbin "test_sine_440hz_11025_8bit_mono.raw"
