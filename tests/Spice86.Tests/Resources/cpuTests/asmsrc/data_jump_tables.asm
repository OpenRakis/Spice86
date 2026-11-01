; compile it with fasm
; Data-modelling fixture: jump and call tables. DS = SS = 2000h. Results go to offsets 40h-7Ch
; (physical 20040h-2007Ch); offset 100h holds the data-segment table. Every index of every table
; is executed at least once so discovery sees every target.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; A bounded ES-table dispatch. Each index is executed once through the same table.
    mov word [80h], 0
    mov bx, [80h]
    mov ax, 0F000h
    mov es, ax
    mov si, bx
    shl si, 1
    cmp si, 6
    ja j1_1
    jmp word [es:si+j1_table]
j1_1:
    mov word [80h], 1
    mov bx, [80h]
    mov ax, 0F000h
    mov es, ax
    mov si, bx
    shl si, 1
    cmp si, 6
    ja j1_2
    jmp word [es:si+j1_table]
j1_2:
    mov word [80h], 2
    mov bx, [80h]
    mov ax, 0F000h
    mov es, ax
    mov si, bx
    shl si, 1
    cmp si, 6
    ja j1_3
    jmp word [es:si+j1_table]
j1_3:
    mov word [80h], 3
    mov bx, [80h]
    mov ax, 0F000h
    mov es, ax
    mov si, bx
    shl si, 1
    cmp si, 6
    ja j1_out
    jmp word [es:si+j1_table]
j1_out:

; A nonzero lower bound: BX=2..4 gives byte indexes 4..8. Each index is executed once.
    mov word [80h], 2
    mov bx, [80h]
    mov ax, 0F000h
    mov es, ax
    mov si, bx
    shl si, 1
    cmp si, 4
    jb j1b_out
    cmp si, 8
    ja j1b_out
    jmp word [es:si+j1b_table]
j1b_1:
    mov word [80h], 3
    mov bx, [80h]
    mov ax, 0F000h
    mov es, ax
    mov si, bx
    shl si, 1
    cmp si, 4
    jb j1b_out
    cmp si, 8
    ja j1b_out
    jmp word [es:si+j1b_table]
j1b_2:
    mov word [80h], 4
    mov bx, [80h]
    mov ax, 0F000h
    mov es, ax
    mov si, bx
    shl si, 1
    cmp si, 4
    jb j1b_out
    cmp si, 8
    ja j1b_out
    jmp word [es:si+j1b_table]
j1b_out:

; The patched scenario replaces the first table word with j2_h1 before execution.
; Index 0 must then reach t1 instead of t0. Both indexes run through this dispatch.
; Two extra passes enter the target stubs directly, so they remain shared blocks.
    mov cx, 4
j2_pass:
    cmp cx, 4
    je j2_h1
    cmp cx, 3
    je j2_h0
    mov si, cx
    dec si
    shl si, 1
    mov ax, 0F000h
    mov es, ax
    cmp si, 2
    ja j2_out
    jmp word [es:si+j2_table]
j2_back:
    dec cx
    jnz j2_pass
j2_out:

; Two dispatches through one table with different bounds. BX=1 takes the first,
; then BX=5 takes the second.
    mov word [80h], 1
    mov bx, [80h]
    mov ax, 0F000h
    mov es, ax
    mov si, bx
    shl si, 1
    cmp si, 4
    ja j4_past1
    jmp word [es:si+j4_table]
j4_past1:
    mov word [80h], 5
    mov bx, [80h]
    mov ax, 0F000h
    mov es, ax
    mov si, bx
    shl si, 1
    cmp si, 8
    jb j4_done
    cmp si, 12
    ja j4_done
    jmp word [es:si+j4_table]
j4_done:

; a near jump table on an index with a bounds check, table in CS - wrong rewrite: resolving the
; dispatch to a single constant target
    xor bx, bx
    mov cx, 5
t1_loop:
    cmp bx, 3
    ja t1_default
    mov si, bx
    shl si, 1
    jmp word [cs:si+t1_table]
t1_t0:
    mov word [40h], 1111h
    jmp t1_next
t1_t1:
    mov word [42h], 2222h
    jmp t1_next
t1_t2:
    mov word [44h], 3333h
    jmp t1_next
t1_t3:
    mov word [46h], 4444h
    jmp t1_next
t1_default:
    mov word [48h], 9999h
t1_next:
    inc bx
    loop t1_loop

; the same with the table in the data segment - wrong rewrite: reading the table bytes as code
; (cs:) instead of data
    mov word [100h], t2_t0
    mov word [102h], t2_t1
    xor bx, bx
t2_loop:
    jmp word [bx+100h]
t2_t0:
    mov word [4Ah], 0AAAAh
    jmp t2_next
t2_t1:
    mov word [4Ch], 0BBBBh
    jmp t2_next
t2_next:
    add bx, 2
    cmp bx, 4
    jb t2_loop

; a table patched at run time: the second dispatch of index 0 must take the new target - wrong
; rewrite: caching the target of the first dispatch
    mov word [104h], t3_a
    xor bx, bx
    jmp word [bx+104h]
t3_a:
    mov word [4Eh], 1
    mov word [104h], t3_b
    jmp word [bx+104h]
t3_b:
    mov word [50h], 2

; a call table - wrong rewrite: a direct-call model that inlines one entry and loses the other
    mov word [106h], t4_f0
    mov word [108h], t4_f1
    xor bx, bx
    call word [bx+106h]
    mov bx, 2
    call word [bx+106h]

; A signed shift produces a negative byte index: 8000h SAR 9 gives FFC0h,
; which reaches the word at j3_table-40h. A second pass uses 400h SAR 9 = 2.
; The two compare edges keep the target stubs shared.
    mov word [80h], 8000h
    mov cx, 2
j3_pass:
    cmp cx, 3
    je j3_h1
    cmp cx, 4
    je j3_h0
    mov bx, [80h]
    sar bx, 9
    sub dx, dx
    mov ax, 0F000h
    mov es, ax
    jmp word [es:bx+j3_table]
j3_back:
    mov word [80h], 400h
    dec cx
    jnz j3_pass
j3_out:

    hlt

j1_h0: jmp j1_t0
j1_h1: jmp j1_t1
j1_h2: jmp j1_t2
j1_h3: jmp j1_t3

j1_t0:
    inc word [56h]
    jmp j1_1
j1_t1:
    inc word [58h]
    jmp j1_2
j1_t2:
    inc word [5Ah]
    jmp j1_3
j1_t3:
    inc word [5Ch]
    jmp j1_out

j1b_h0: jmp j1b_t0
j1b_h1: jmp j1b_t1
j1b_h2: jmp j1b_t2

j1b_t0:
    mov word [64h], 1
    jmp j1b_1
j1b_t1:
    mov word [66h], 1
    jmp j1b_2
j1b_t2:
    mov word [78h], 1
    jmp j1b_out

j2_h0: jmp j2_t0
j2_h1: jmp j2_t1

j2_t0:
    inc word [60h]
    jmp j2_back
j2_t1:
    mov word [62h], 1
    jmp j2_back

j3_h0: jmp j3_t0
j3_h1: jmp j3_t1

j3_t0:
    mov word [7Ah], 1
    jmp j3_back
j3_t1:
    mov word [7Ch], 1
    jmp j3_back

j4_h0: jmp j4_t0
j4_h1: jmp j4_t1
j4_h2: jmp j4_t2
j4_h3: jmp j4_t3
j4_h4: jmp j4_t4
j4_h5: jmp j4_t5
j4_h6: jmp j4_t6

j4_t0:
    mov word [68h], 1100h
    jmp j4_past1
j4_t1:
    mov word [6Ah], 1111h
    jmp j4_past1
j4_t2:
    mov word [6Ch], 1122h
    jmp j4_past1
j4_t3:
    mov word [70h], 1133h
    jmp j4_past1
j4_t4:
    mov word [72h], 1144h
    jmp j4_done
j4_t5:
    mov word [74h], 1155h
    jmp j4_done
j4_t6:
    mov word [76h], 1166h
    jmp j4_done

t4_f0:
    mov word [52h], 0F0F0h
    ret
t4_f1:
    mov word [54h], 0F1F1h
    ret

t1_table:
    dw t1_t0
    dw t1_t1
    dw t1_t2
    dw t1_t3

j1_table:
    dw j1_h0
    dw j1_h1
    dw j1_h2
    dw j1_h3

j1b_table:
    dw j1b_h0
    dw j1b_h1
    dw j1b_h0
    dw j1b_h1
    dw j1b_h2

j2_table:
    dw j2_h0
    dw j2_h1

; the word the bx = 0xFFC0 dispatch reads: 0xFFC0 + j3_table wraps to j3_table - 40h
j3_wrap:
    dw j3_h0
    rb 0x3E
j3_table:
    dw j3_h0
    dw j3_h1

j4_table:
    dw j4_h0
    dw j4_h1
    dw j4_h2
    dw j4_h3
    dw j4_h4
    dw j4_h5
    dw j4_h6

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
