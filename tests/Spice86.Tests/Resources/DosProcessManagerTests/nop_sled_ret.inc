; Returns control to the DOS process manager immediately. The NOP payload in front of
; the RET fills the program to the requested size, so a test can pick the program size
; that the memory, TSR and environment checks see without changing where control
; transfers back.
; Parameter: payload_length is the total program size in bytes, including the final RET.
use16
org 100h

    times payload_length - 1 nop ; pad with NOPs up to one byte short of payload_length
    ; Return to DOS through the INT 20h at the start of the PSP.
    ret
