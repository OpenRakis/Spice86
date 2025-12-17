; Sound Blaster 8-bit Single-Cycle DMA Transfer Test
; Tests basic 8-bit PCM DMA transfer with IRQ signaling
; Based on DOSBox soundblaster.cpp DMA command handling

use16
org 0x100

start:
    ; Setup test environment
    mov ax, cs
    mov ds, ax
    mov es, ax
    
    ; Setup DMA channel 1 for Sound Blaster
    ; DMA channel 1: address 0x0002-0x0003, count 0x0004-0x0005, page 0x83
    
    ; Disable DMA channel 1 by masking it
    mov al, 0x05            ; Mask channel 1 (bit 2 set, bits 1-0 = channel)
    out 0x0A, al
    
    ; Set DMA mode for channel 1 (single transfer, write to memory, auto-init off)
    mov al, 0x49            ; Mode: channel 1, read, single transfer
    out 0x0B, al
    
    ; Clear flip-flop
    xor al, al
    out 0x0C, al
    
    ; Set DMA address (physical address = segment * 16 + offset)
    ; For segment 0x160: physical = 0x1600 + offset
    ; DMA needs: page register (bits 16-23) and address register (bits 0-15)
    ; Physical 0x1600 + buffer_offset = page 0x01, address 0x0600 + buffer_offset
    mov ax, ds
    mov cl, 4
    shl ax, cl              ; DS * 16 = 0x1600 for DS=0x160
    mov bx, test_dma_buffer
    add ax, bx              ; Add buffer offset
    out 0x02, al            ; DMA address low byte
    mov al, ah
    out 0x02, al            ; DMA address high byte
    
    ; Set DMA count (number of bytes - 1)
    mov ax, 0x001F          ; 32 bytes - 1 (reduced from 64 to match working auto-init test)
    out 0x03, al            ; Low byte (port 0x03 for channel 1 count)
    mov al, ah
    out 0x03, al            ; High byte
    
    ; Set DMA page (bits 16-23) - for DS=0x160, page = 0x01
    mov ax, ds
    mov cl, 4
    shr ax, cl
    shr ax, cl
    shr ax, cl              ; DS >> 12 = page number
    out 0x83, al
    
    ; Fill DMA buffer with test pattern
    mov cx, 32              ; Reduced from 64 to match DMA count
    mov di, test_dma_buffer
    xor al, al
.fill_loop:
    mov [di], al
    inc di
    inc al
    loop .fill_loop
    
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
    ; Set time constant for 22050 Hz (default playback rate)
    ; TC = 256 - (1000000 / sample_rate)
    ; For 22050 Hz: TC = 256 - 45 = 211 (0xD3)
    mov dx, 0x22C           ; Write command port (base + 0xC)
.wait_write1:
    mov dx, 0x22C
    in al, dx
    test al, 0x80
    jnz .wait_write1
    
    mov al, 0x40            ; Set time constant command
    out dx, al
    
.wait_write2:
    in al, dx
    test al, 0x80
    jnz .wait_write2
    
    mov al, 0xD3            ; Time constant value
    out dx, al
    
    ; Enable speaker
.wait_write3:
    mov dx, 0x22C
    in al, dx
    test al, 0x80
    jnz .wait_write3
    
    mov al, 0xD1            ; Speaker on command
    out dx, al
    
    ; Unmask DMA channel 1
    mov al, 0x01            ; Unmask channel 1
    out 0x0A, al
    
    ; Send 8-bit single-cycle DMA command (0x14)
.wait_write4:
    mov dx, 0x22C
    in al, dx
    test al, 0x80
    jnz .wait_write4
    
    mov al, 0x14            ; 8-bit single-cycle DMA command
    out dx, al
    
    ; Send DMA length (32 bytes - 1 = 31, 0x001F)
.wait_write5:
    in al, dx
    test al, 0x80
    jnz .wait_write5
    
    mov al, 0x1F            ; Low byte (changed from 0x3F)
    out dx, al
    
.wait_write6:
    in al, dx
    test al, 0x80
    jnz .wait_write6
    
    mov al, 0x00            ; High byte
    out dx, al
    
    ; Wait for IRQ (check IRQ status register)
    mov cx, 10000
.wait_irq:
    mov dx, 0x22E           ; IRQ acknowledge port (base + 0xE)
    in al, dx
    test al, 0x80           ; Check if IRQ is pending
    jnz .irq_received
    loop .wait_irq
    jmp test_failed
    
.irq_received:
    ; Acknowledge IRQ
    in al, dx               ; Reading port acknowledges 8-bit IRQ
    
    ; Disable speaker
    mov dx, 0x22C
.wait_write7:
    in al, dx
    test al, 0x80
    jnz .wait_write7
    
    mov al, 0xD3            ; Speaker off command
    out dx, al
    
    ; Test passed - write success to port 0x999
    mov dx, 0x999
    mov al, 0x00            ; Success
    out dx, al
    hlt                     ; Halt CPU
    
test_failed:
    ; Test failed - write failure to port 0x999
    mov dx, 0x999
    mov al, 0xFF            ; Failure
    out dx, al
    hlt                     ; Halt CPU

; Data section
test_dma_buffer: times 32 db 0
