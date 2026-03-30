; scroll_up_clears_full_row.asm
; Tests that INT 10h AH=06h (scroll up) clears the full width of the
; vacated row, not just half of it.
;
; Setup:
;   Row 0: 80 x 'A' (attribute 0x1F)
;   Row 1: 80 x 'B' (attribute 0x1F)
;   Row 2: 80 x 'C' (attribute 0x1F)
;
; Action:
;   Scroll window (rows 0-2, cols 0-79) up by 1 line
;
; Expected result in video memory:
;   Row 0: 80 x 'B' (from old row 1)
;   Row 1: 80 x 'C' (from old row 2)
;   Row 2: 80 x ' ' with attribute 0x07 (cleared row)

        BITS 16
        ORG 0x100

        ; Set video mode 3 (80x25 colour text)
        mov     ax, 0x0003
        int     0x10

        ; --- Fill row 0 with 'A' ---
        mov     ah, 0x02        ; set cursor position
        mov     bh, 0           ; page 0
        mov     dh, 0           ; row 0
        mov     dl, 0           ; col 0
        int     0x10

        mov     ah, 0x09        ; write char + attribute
        mov     al, 'A'
        mov     bh, 0           ; page 0
        mov     bl, 0x1F        ; attribute
        mov     cx, 80          ; repeat count
        int     0x10

        ; --- Fill row 1 with 'B' ---
        mov     ah, 0x02
        mov     bh, 0
        mov     dh, 1
        mov     dl, 0
        int     0x10

        mov     ah, 0x09
        mov     al, 'B'
        mov     bh, 0
        mov     bl, 0x1F
        mov     cx, 80
        int     0x10

        ; --- Fill row 2 with 'C' ---
        mov     ah, 0x02
        mov     bh, 0
        mov     dh, 2
        mov     dl, 0
        int     0x10

        mov     ah, 0x09
        mov     al, 'C'
        mov     bh, 0
        mov     bl, 0x1F
        mov     cx, 80
        int     0x10

        ; --- Scroll up 1 line, rows 0-2, cols 0-79 ---
        mov     ah, 0x06        ; scroll up
        mov     al, 0x01        ; 1 line
        mov     bh, 0x07        ; attribute for blank line
        mov     ch, 0           ; upper-left row
        mov     cl, 0           ; upper-left col
        mov     dh, 2           ; lower-right row
        mov     dl, 79          ; lower-right col
        int     0x10

        ; --- Exit ---
        mov     ax, 0x4C00
        int     0x21
