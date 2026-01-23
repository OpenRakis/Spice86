; Tests that handles allocated after deallocation don't collide with existing handles.
; This tests for the handle ID reuse bug where EmmHandles.Count was used for new IDs.

use16
org 0x100

start:
    ; Allocate handle 1 (4 pages)
    mov bx, 4
    mov ah, 43h             ; Allocate
    int 67h
    cmp ah, 0
    jne failed
    mov di, dx              ; Save handle1 in DI
    
    ; Allocate handle 2 (4 pages)
    mov bx, 4
    mov ah, 43h             ; Allocate
    int 67h
    cmp ah, 0
    jne failed
    mov si, dx              ; Save handle2 in SI
    
    ; Map handle2's page 0 to physical page 0 and write a marker
    mov al, 0               ; Physical page 0
    mov bx, 0               ; Logical page 0
    mov dx, si              ; handle2
    mov ah, 44h
    int 67h
    cmp ah, 0
    jne failed
    
    ; Write 0xAA to handle2's page
    mov ax, 0E000h
    mov es, ax
    mov byte [es:0], 0AAh
    
    ; Deallocate handle1 (the first handle)
    mov dx, di              ; handle1
    mov ah, 45h             ; Deallocate
    int 67h
    cmp ah, 0
    jne failed
    
    ; Verify handle2's data is still intact (0xAA)
    mov al, [es:0]
    cmp al, 0AAh
    je success

failed:
    mov al, 0FFh            ; TestResult.Failure
    jmp writeResult

success:
    mov al, 0               ; TestResult.Success

writeResult:
    mov dx, 0999h           ; ResultPort
    out dx, al
    hlt
