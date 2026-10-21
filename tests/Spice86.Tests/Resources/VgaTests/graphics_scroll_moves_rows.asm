; graphics_scroll_moves_rows.asm
; Tests that INT 10h AH=06h (scroll up) in CGA graphics mode 4 actually
; copies the scrolled rows.
;
; A geometry mix-up in the graphics move path sets the copy width and the
; copy row count from window coordinates instead of window extents, so at a
; full-width scroll both become zero: the move is a no-op and the rows are
; blanked instead of shifted.
;
; Layout: mode 4 is 80 bytes per scanline. The emulator maps a pixel Y to
;   offset = Y / 2 * LineLength
; so character row r (pixel rows r*8 .. r*8+7) starts at byte offset r * 320.
; Even scanlines live at that offset, odd scanlines at offset + 0x2000.
;
; Setup:  character row 0 filled with colour 3, row 1 left clear,
;         row 2 filled with colour 1.
; Action: scroll rows 0-2 up by one line.
; Expect: row 0 now holds row 1's old content (clear) and row 1 holds
;         row 2's old content (non-zero). A no-op leaves both blank.
;
; Build: nasm -f bin graphics_scroll_moves_rows.asm -o graphics_scroll_moves_rows.com

        BITS 16
        ORG 0x100

        ; Set video mode 4 (320x200, 2bpp CGA colour)
        mov     ax, 0x0004
        int     0x10

        ; --- Row 0: solid colour 3 ---
        mov     ah, 0x02
        mov     bh, 0
        mov     dh, 0
        mov     dl, 0
        int     0x10

        mov     ah, 0x09
        mov     al, 0xDB        ; solid block glyph
        mov     bh, 0
        mov     bl, 0x03        ; colour 3
        mov     cx, 40
        int     0x10

        ; --- Row 1: clear (blank glyph, colour 0) ---
        mov     ah, 0x02
        mov     bh, 0
        mov     dh, 1
        mov     dl, 0
        int     0x10

        mov     ah, 0x09
        mov     al, 0x20        ; space leaves pixels clear
        mov     bh, 0
        mov     bl, 0x00
        mov     cx, 40
        int     0x10

        ; --- Row 2: solid colour 1 ---
        mov     ah, 0x02
        mov     bh, 0
        mov     dh, 2
        mov     dl, 0
        int     0x10

        mov     ah, 0x09
        mov     al, 0xDB
        mov     bh, 0
        mov     bl, 0x01        ; colour 1
        mov     cx, 40
        int     0x10

        ; --- Scroll rows 0-2 up by one line ---
        mov     ah, 0x06
        mov     al, 0x01
        mov     bh, 0x00
        mov     ch, 0
        mov     cl, 0
        mov     dh, 2
        mov     dl, 39
        int     0x10

        ; --- Verify: row 0 (B800:0000) must be clear after the scroll ---
        mov     ax, 0xB800
        mov     es, ax
        xor     bx, bx
        mov     cx, 8
        xor     dh, dh
.check_row0:
        or      dh, [es:bx]
        inc     bx
        loop    .check_row0
        or      dh, dh
        jnz     .fail

        ; --- Verify: row 1 (B800:0140 = 320) must be non-zero ---
        mov     bx, 320
        mov     cx, 8
        xor     dh, dh
.check_row1:
        or      dh, [es:bx]
        inc     bx
        loop    .check_row1
        or      dh, dh
        jz      .fail

        ; --- Pass ---
        mov     al, 0x00
        mov     dx, 0x0999
        out     dx, al
        jmp     .done

.fail:
        mov     al, 0xFF
        mov     dx, 0x0999
        out     dx, al

.done:
        mov     ax, 0x4C00
        int     0x21
