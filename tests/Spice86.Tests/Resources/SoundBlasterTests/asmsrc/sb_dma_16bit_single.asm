; Sound Blaster 16 16-bit Single-Cycle DMA Transfer Test
; Tests 16-bit PCM DMA transfer using SB16 commands
; Based on DOSBox soundblaster.cpp 16-bit DMA handling

use16
org 0x100

start:
    ; Setup test environment
    mov ax, cs
    mov ds, ax
    mov es, ax
    
    ; Initialize test result
    mov word [test_result], 0x0000
    
    ; Setup DMA channel 5 for Sound Blaster 16-bit
    ; DMA channel 5: address 0xC4-0xC6, count 0xC6-0xC8, page 0x8B
    
    ; Mask channel 5 (16-bit DMA controller)
    mov al, 0x05            ; Mask channel 5 (bit 2 set, bits 1-0 = channel - 4)
    out 0xD4, al            ; 16-bit DMA mask register
    
    ; Set DMA mode for channel 5
    mov al, 0x49            ; Mode: channel 5, read, single transfer
    out 0xD6, al            ; 16-bit DMA mode register
    
    ; Clear flip-flop
    xor al, al
    out 0xD8, al            ; 16-bit DMA flip-flop clear
    
    ; Set DMA address (word address, not byte address)
    mov ax, test_dma_buffer
    shr ax, 1               ; Convert to word address
    out 0xC4, al            ; Low byte
    mov al, ah
    out 0xC4, al            ; High byte
    
    ; Set DMA count (number of words - 1)
    mov ax, 0x001F          ; 32 words - 1 (64 bytes)
    out 0xC6, al            ; Low byte
    mov al, ah
    out 0xC6, al            ; High byte
    
    ; Set DMA page
    xor al, al
    out 0x8B, al
    
    ; Fill DMA buffer with 16-bit test pattern
    mov cx, 32              ; 32 words = 64 bytes
    mov di, test_dma_buffer
    xor ax, ax
.fill_loop:
    mov [di], ax
    add di, 2
    add ax, 0x100
    loop .fill_loop
    
    ; Reset DSP
    mov dx, 0x226
    mov al, 1
    out dx, al
    mov cx, 100
.reset_wait1:
    loop .reset_wait1
    xor al, al
    out dx, al
    
    ; Wait for DSP ready
    mov dx, 0x22A
    mov cx, 1000
.wait_ready:
    in al, dx
    cmp al, 0xAA
    je .dsp_ready
    loop .wait_ready
    jmp test_failed
    
.dsp_ready:
    ; Set sample rate (command 0x41 for output rate)
    mov dx, 0x22C
.wait_write1:
    in al, dx
    test al, 0x80
    jnz .wait_write1
    
    mov al, 0x41            ; Set output sample rate command
    out dx, al
    
.wait_write2:
    in al, dx
    test al, 0x80
    jnz .wait_write2
    
    mov al, 0x56            ; High byte (22050 Hz)
    out dx, al
    
.wait_write3:
    in al, dx
    test al, 0x80
    jnz .wait_write3
    
    mov al, 0x22            ; Low byte
    out dx, al
    
    ; Enable speaker
.wait_write4:
    in al, dx
    test al, 0x80
    jnz .wait_write4
    
    mov al, 0xD1            ; Speaker on
    out dx, al
    
    ; Unmask DMA channel 5
    mov al, 0x01            ; Unmask channel 5
    out 0xD4, al
    
    ; Send 16-bit single-cycle DMA command (0xB0-0xBF)
    ; 0xB0: 16-bit, mono, single-cycle, unsigned
.wait_write5:
    mov dx, 0x22C
    in al, dx
    test al, 0x80
    jnz .wait_write5
    
    mov al, 0xB0            ; 16-bit single-cycle DMA command
    out dx, al
    
.wait_write6:
    in al, dx
    test al, 0x80
    jnz .wait_write6
    
    mov al, 0x00            ; Mode: mono, unsigned
    out dx, al
    
    ; Send DMA length (32 words - 1 = 31, 0x001F)
.wait_write7:
    in al, dx
    test al, 0x80
    jnz .wait_write7
    
    mov al, 0x1F            ; Low byte
    out dx, al
    
.wait_write8:
    in al, dx
    test al, 0x80
    jnz .wait_write8
    
    mov al, 0x00            ; High byte
    out dx, al
    
    ; Wait for IRQ (16-bit IRQ on 0x22F)
    mov cx, 10000
.wait_irq:
    mov dx, 0x22F           ; 16-bit IRQ acknowledge port
    in al, dx
    test al, 0x80
    jnz .irq_received
    loop .wait_irq
    jmp test_failed
    
.irq_received:
    ; Acknowledge 16-bit IRQ
    in al, dx
    
    ; Disable speaker
    mov dx, 0x22C
.wait_write9:
    in al, dx
    test al, 0x80
    jnz .wait_write9
    
    mov al, 0xD3            ; Speaker off
    out dx, al
    
    ; Test passed
    mov word [test_result], 0x0001
    jmp test_end
    
test_failed:
    mov word [test_result], 0xFFFF
    
test_end:
    ; Exit
    mov ax, 0x4C00
    int 0x21

; Data section
test_result:    dw 0x0000
test_dma_buffer: times 64 db 0
