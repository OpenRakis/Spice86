; Build: fasm ah0c_flush_then_ah06_input_status_to_video.asm ah0c_flush_then_ah06_input_status_to_video.com
; Executes INT 21h AH=0Ch with AL=06h and DL=FFh (flush the keyboard buffer, then run the
; direct console input status subfunction) and writes the AL result byte into a text video
; cell. The test preloads a key and checks that the cell reports no input, because AH=0Ch
; must clear the buffer before invoking the subfunction.
; Parameter: video_offset is the byte offset of the receiving cell in the B800h segment.
use16
org 100h

video_offset equ 0h

    ; Flush the keyboard buffer and then run the AH=06h input status subfunction, whose
    ; result lands in AL.
    mov al, 06h
    mov dl, 0FFh
    mov ah, 0Ch
    int 21h
    ; Store AL with attribute 07h in the receiving cell of the B800h text segment.
    mov ah, 07h
    mov bx, 0B800h
    mov es, bx
    mov di, video_offset
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
