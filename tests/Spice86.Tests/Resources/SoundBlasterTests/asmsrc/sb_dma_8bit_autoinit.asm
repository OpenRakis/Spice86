; Sound Blaster 8-bit Auto-Init DMA Transfer Test
; Tests auto-init DMA mode with continuous transfers
; Based on DOSBox soundblaster.cpp auto-init handling

use16
org 0x100

start:
    ; Setup test environment
    mov ax, cs
    mov ds, ax
    mov es, ax
    
    ; Initialize IRQ count
    mov word [irq_count], 0x0000
    
    ; Setup DMA channel 1 for Sound Blaster (auto-init mode)
    ; Mask channel 1
    mov al, 0x05
    out 0x0A, al
    
    ; Set DMA mode for channel 1 (single transfer, auto-init)
    mov al, 0x59            ; Mode: channel 1, read, auto-init
    out 0x0B, al
    
    ; Clear flip-flop
    xor al, al
    out 0x0C, al
    
    ; Set DMA address (physical address = segment * 16 + offset)
    mov ax, ds
    mov cl, 4
    shl ax, cl              ; DS * 16
    mov bx, test_dma_buffer
    add ax, bx              ; Add buffer offset
    out 0x02, al            ; DMA address low byte
    mov al, ah
    out 0x02, al            ; DMA address high byte
    
    ; Set DMA count (32 bytes - 1)
    mov ax, 0x001F
    out 0x04, al
    mov al, ah
    out 0x04, al
    
    ; Set DMA page (bits 16-23)
    mov ax, ds
    mov cl, 4
    shr ax, cl
    shr ax, cl
    shr ax, cl              ; DS >> 12 = page number
    out 0x83, al
    
    ; Fill DMA buffer with test pattern
    mov cx, 32
    mov di, test_dma_buffer
    mov al, 0x80            ; Mid-point value
.fill_loop:
    mov [di], al
    inc di
    add al, 2
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
    ; Set block size for auto-init (command 0x48)
    mov dx, 0x22C
.wait_write1:
    in al, dx
    test al, 0x80
    jnz .wait_write1
    
    mov al, 0x48            ; Set block size command
    out dx, al
    
.wait_write2:
    in al, dx
    test al, 0x80
    jnz .wait_write2
    
    mov al, 0x1F            ; Low byte (32 bytes - 1)
    out dx, al
    
.wait_write3:
    in al, dx
    test al, 0x80
    jnz .wait_write3
    
    mov al, 0x00            ; High byte
    out dx, al
    
    ; Set time constant
.wait_write4:
    in al, dx
    test al, 0x80
    jnz .wait_write4
    
    mov al, 0x40            ; Set time constant command
    out dx, al
    
.wait_write5:
    in al, dx
    test al, 0x80
    jnz .wait_write5
    
    mov al, 0xD3            ; Time constant value
    out dx, al
    
    ; Enable speaker
.wait_write6:
    in al, dx
    test al, 0x80
    jnz .wait_write6
    
    mov al, 0xD1            ; Speaker on
    out dx, al
    
    ; Unmask DMA channel 1
    mov al, 0x01
    out 0x0A, al
    
    ; Send 8-bit auto-init DMA command (0x1C)
.wait_write7:
    mov dx, 0x22C
    in al, dx
    test al, 0x80
    jnz .wait_write7
    
    mov al, 0x1C            ; 8-bit auto-init DMA command
    out dx, al
    
    ; Wait for first IRQ
    mov cx, 10000
.wait_irq1:
    mov dx, 0x22E
    in al, dx
    test al, 0x80
    jnz .irq1_received
    loop .wait_irq1
    jmp test_failed
    
.irq1_received:
    ; Acknowledge first IRQ
    in al, dx
    inc word [irq_count]
    
    ; Wait for second IRQ to confirm auto-init
    mov cx, 10000
.wait_irq2:
    mov dx, 0x22E
    in al, dx
    test al, 0x80
    jnz .irq2_received
    loop .wait_irq2
    jmp test_failed
    
.irq2_received:
    ; Acknowledge second IRQ
    in al, dx
    inc word [irq_count]
    
    ; Exit auto-init mode (command 0xDA)
    mov dx, 0x22C
.wait_write8:
    in al, dx
    test al, 0x80
    jnz .wait_write8
    
    mov al, 0xDA            ; Exit auto-init 8-bit
    out dx, al
    
    ; Disable speaker
.wait_write9:
    in al, dx
    test al, 0x80
    jnz .wait_write9
    
    mov al, 0xD3            ; Speaker off
    out dx, al
    
    ; Check if we got at least 2 IRQs
    cmp word [irq_count], 2
    jl test_failed
    
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
irq_count:      dw 0x0000
test_dma_buffer: times 32 db 0
