017D:0000 mov AX,0xDD1D
017D:0003 call near 0xE4AD
017D:0006 call near 0xE594
017D:0009 call near 0x00B0
017D:000C sti
017D:000D call near 0x0580
017D:0098 mov CX,word ptr ES:[DI]
017D:009B shr CX,1
017D:009D mov BX,DI
017D:009F mov AX,word ptr ES:[DI]
017D:00A2 add AX,BX
017D:00A4 stos word ptr ES:[DI],AX
017D:00A5 loop 0x009F
017D:00A7 ret near
017D:00B0 call near 0x00D1
017D:00B3 call near 0x0169
017D:00B6 call near 0xDA53
017D:00B9 call near 0xB17A
017D:00BC call near 0xB17A
017D:00BF xor AX,AX
017D:00C1 mov ES,AX
017D:00C3 mov AX,word ptr ES:[0x046C]
017D:00C7 mov word ptr DS:[0xD824],AX
017D:00CA mov word ptr DS:[0xD826],AX
017D:00CD mov word ptr DS:[0xD828],AX
017D:00D0 ret near
017D:00D1 push DS
017D:00D2 pop ES
017D:00D3 mov DI,0x4948
017D:00D6 mov SI,0x00BA
017D:00D9 call near 0xF0B9
017D:00DC mov CX,0x018C
017D:00DF mov SI,DI
017D:00E1 lods AX,word ptr DS:[SI]
017D:00E2 xchg AH,AL
017D:00E4 stos word ptr ES:[DI],AX
017D:00E5 loop 0x00E1
017D:00E7 mov DI,0x4880
017D:00EA mov CX,0x0063
017D:00ED mov SI,0x494A
017D:00F0 xor AX,AX
017D:00F2 mov DX,1
017D:00F5 mov BX,word ptr DS:[SI]
017D:00F7 shl BX,1
017D:00F9 div BX
017D:00FB cmp word ptr DS:[SI],DX
017D:00FD adc AX,0
017D:0100 stos word ptr ES:[DI],AX
017D:0101 add SI,8
017D:0104 loop 0x00F0
017D:0106 mov SI,0x00BF
017D:0109 call near 0xF0B9
017D:010C mov AX,DI
017D:010E add AX,0x62FC
017D:0111 mov word ptr DS:[0xDCFE],AX
017D:0114 mov word ptr DS:[0xDD00],ES
017D:0118 push DS
017D:0119 pop ES
017D:011A mov DI,0xAA76
017D:011D mov SI,0x00BD
017D:0120 call near 0xF0A0
017D:0123 call near 0x0098
017D:0126 mov SI,0x00BC
017D:0129 call near 0xF0B9
017D:012C mov word ptr DS:[0xAA72],DI
017D:0130 mov word ptr DS:[0xAA74],ES
017D:0134 call near 0x0098
017D:0137 les AX,word ptr DS:[0x39B7]
017D:013B mov word ptr DS:[0x47AC],AX
017D:013E mov word ptr DS:[0x47AE],ES
017D:0142 mov CX,0x1D4C
017D:0145 call near 0xF0FF
017D:0148 les AX,word ptr DS:[0x39B7]
017D:014C mov word ptr DS:[0x47B0],AX
017D:014F mov word ptr DS:[0x47B2],ES
017D:0153 mov CX,0xADD4
017D:0156 call near 0xF0FF
017D:0159 call near 0xCFB9
017D:015C jmp near 0xC137
017D:0169 mov AX,0x003A
017D:016C call near 0xC13E
017D:016F push DS
017D:0170 pop ES
017D:0171 mov DI,0x4C60
017D:0174 push DI
017D:0175 mov AX,7
017D:0178 mov CX,0x0100
017D:017B rep stos word ptr ES:[DI],AX
017D:017D pop DI
017D:017E les SI,word ptr DS:[0xDBB0]
017D:0182 mov CX,0xC5F9
017D:0185 lods AL,byte ptr ES:[SI]
017D:0187 mov BX,AX
017D:0189 shl BX,1
017D:018B inc word ptr DS:[BX+DI]
017D:018D loop 0x0185
017D:018F mov SI,0x0100
017D:0192 mov DX,word ptr DS:[SI+2]
017D:0195 mov BX,word ptr DS:[SI+4]
017D:0198 call near 0xB5C5
017D:019B mov word ptr DS:[SI+2],DX
017D:019E mov word ptr DS:[SI+6],DI
017D:01A1 or byte ptr ES:[DI],0x40
017D:01A5 mov ES,word ptr DS:[0xDBB2]
017D:01A9 mov AL,byte ptr ES:[DI]
017D:01AC mov byte ptr DS:[SI+0x10],AL
017D:01AF xor BX,BX
017D:01B1 mov BL,AL
017D:01B3 shl BX,1
017D:01B5 mov AX,word ptr DS:[BX+0x4C60]
017D:01B9 mov CL,4
017D:01BB shr AX,CL
017D:01BD mov byte ptr DS:[SI+0x11],AL
017D:01C0 add SI,0x001C
017D:01C3 cmp byte ptr DS:[SI],0xFF
017D:01C6 jne short 0x0192
017D:01C8 mov DI,0x0100
017D:01CB mov BP,0x01E0
017D:01CE mov DX,word ptr DS:[DI+2]
017D:01D1 mov BX,word ptr DS:[DI+4]
017D:01D4 call near 0x6603
017D:01D7 add DI,0x001C
017D:01DA cmp byte ptr DS:[DI],0xFF
017D:01DD jne short 0x01CB
017D:01DF ret near
017D:01E0 mov word ptr DS:[SI+4],DI
017D:01E3 mov word ptr DS:[SI+6],DX
017D:01E6 mov word ptr DS:[SI+8],BX
017D:01E9 mov AL,byte ptr DS:[DI]
017D:01EB mov AH,byte ptr DS:[SI+0x12]
017D:01EE and AX,0x700F
017D:01F1 cmp AL,3
017D:01F3 jbe short 0x0206
017D:01F5 xor AH,0x80
017D:01F8 cmp AL,5
017D:01FA jbe short 0x0206
017D:01FC xor AH,0x80
017D:01FF cmp AL,9
017D:0201 jbe short 0x0206
017D:0203 xor AH,0x80
017D:0206 or AL,AH
017D:0208 mov byte ptr DS:[SI+0x12],AL
017D:020B ret near
017D:0580 call near 0xDE54
017D:0583 je short 0x05FD
017D:0585 call far dword ptr DS:[0x3959]
017D:0589 call near 0xAEB7
017D:058C mov SI,0x0337
017D:058F call near 0x0945
017D:0592 mov AX,0x0018
017D:0595 call far dword ptr DS:[0x3939]
017D:0599 call near 0x093F
017D:059C mov BX,AX
017D:059E inc AX
017D:059F jne short 0x05A3
017D:05A3 call near 0xDE0C
017D:05A6 jb short 0x05FD
017D:05A8 call near 0x0911
017D:05AB call far dword ptr DS:[0x3959]
017D:05AF call near 0x093F
017D:05B2 mov BP,AX
017D:05B4 call near 0xC097
017D:05B7 and byte ptr DS:[0x47D1],0x7F
017D:05BC call near 0x39E6
017D:05BF call near 0x093F
017D:05C2 mov BX,AX
017D:05C4 call near 0xDE0C
017D:05C7 jb short 0x05FD
017D:05C9 call near 0x093F
017D:05CC or AX,AX
017D:05CE js short 0x05DC
017D:05D0 mov BP,0x0F66
017D:05D3 call near 0xC108
017D:05D6 call near 0xC0F4
017D:05D9 call near 0x3A7C
017D:05DC call near 0xC07C
017D:05DF or byte ptr DS:[0x47D1],0x80
017D:05E4 call near 0xDD63
017D:05E7 jb short 0x05FD
017D:05E9 call near 0x093F
017D:05EC clc
017D:05ED call near AX
017D:061C call near 0xAD57
017D:061F mov AX,0x0015
017D:0622 jmp near 0xCA1B
017D:0625 call near 0xC07C
017D:0628 call near 0xDD63
017D:062B jb short 0x064C
017D:062D call near 0xC9F4
017D:0630 je short 0x0628
017D:0632 call near 0xC4CD
017D:0635 cmp word ptr DS:[0xDBCE],8
017D:063A jb short 0x0646
017D:0646 call near 0xCC85
017D:0649 je short 0x0628
017D:0911 call near 0x39E6
017D:0914 call near 0xB930
017D:0917 call near 0x0B21
017D:091A call near 0x9985
017D:091D call near 0x98E6
017D:0920 mov byte ptr DS:[0x22E3],1
017D:0925 mov byte ptr DS:[0x46D7],0
017D:092A mov SI,0x070C
017D:092D call near 0xDA5F
017D:0930 mov SI,0x3916
017D:0933 call near 0xDA5F
017D:0936 call near 0x0A3E
017D:0939 mov SI,0x0826
017D:093C jmp near 0xDA5F
017D:093F mov SI,word ptr DS:[0x4854]
017D:0943 lods AX,word ptr CS:[SI]
017D:0945 mov word ptr DS:[0x4854],SI
017D:0949 ret near
017D:0A3E mov SI,0x0A16
017D:0A41 jmp near 0xDA5F
017D:0B21 call near 0xAC30
017D:0B24 mov byte ptr CS:[0xC13C],0x25
017D:0B2A mov SI,0x0B45
017D:0B2D call near 0xDA5F
017D:0B30 cmp byte ptr DS:[0x227D],0
017D:0B35 jne short 0x0B3E
017D:0B3E mov word ptr DS:[0x3CBE],0
017D:0B44 ret near
017D:0F66 ret near
017D:39E6 mov SI,0xC0B6
017D:39E9 jmp near 0xDA5F
017D:3A7C call near 0x39E6
017D:3A7F mov AX,word ptr DS:[4]
017D:3A82 cmp AL,4
017D:3A84 jne short 0x3A94
017D:3A94 ret near
017D:5B99 push DS
017D:5B9A pop ES
017D:5B9B movs word ptr ES:[DI],word ptr DS:[SI]
017D:5B9C movs word ptr ES:[DI],word ptr DS:[SI]
017D:5B9D movs word ptr ES:[DI],word ptr DS:[SI]
017D:5B9E movs word ptr ES:[DI],word ptr DS:[SI]
017D:5B9F ret near
017D:6603 push SI
017D:6604 mov AL,byte ptr DS:[DI+9]
017D:6607 or AL,AL
017D:6609 je short 0x661B
017D:660B call near 0x6906
017D:660E push SI
017D:660F push DI
017D:6610 push BP
017D:6611 call near BP
017D:6613 pop BP
017D:6614 pop DI
017D:6615 pop SI
017D:6616 mov AL,byte ptr DS:[SI+1]
017D:6619 jmp short 0x6607
017D:661B pop SI
017D:661C ret near
017D:6906 mov SI,AX
017D:6908 dec AL
017D:690A mov AH,0x1B
017D:690C mul AH
017D:690E add AX,0x08AA
017D:6911 xchg SI,AX
017D:6912 cmp byte ptr DS:[SI+3],0x80
017D:6916 ret near
017D:94F3 cmp word ptr DS:[0x47C4],0x0010
017D:94F8 jae short 0x9532
017D:9532 ret near
017D:96B5 push word ptr DS:[0x47C4]
017D:96B9 push word ptr DS:[0x47C2]
017D:96BD mov word ptr DS:[0x47C4],0x0010
017D:96C3 mov byte ptr DS:[0x47C2],0x80
017D:96C8 mov SI,word ptr DS:[0xAB84]
017D:96CC call near 0x9F9E
017D:96CF pop word ptr DS:[0x47C2]
017D:96D3 pop word ptr DS:[0x47C4]
017D:96D7 ret near
017D:98E6 call near 0x98F5
017D:98E9 mov word ptr DS:[0x47C8],AX
017D:98EC mov word ptr DS:[0x47AA],AX
017D:98EF mov word ptr DS:[0x479E],AX
017D:98F2 jmp near 0x9B8B
017D:98F5 xor AX,AX
017D:98F7 mov word ptr DS:[0x1C06],AX
017D:98FA mov word ptr DS:[0x1BF8],AX
017D:98FD mov word ptr DS:[0x1BEA],AX
017D:9900 ret near
017D:9985 test word ptr DS:[0x47CE],7
017D:998B jne short 0x9982
017D:998D ret near
017D:9B8B call near 0xA7A5
017D:9B8E xor AX,AX
017D:9B90 mov byte ptr DS:[0x47C3],0
017D:9B95 mov word ptr DS:[0x47CE],AX
017D:9B98 and byte ptr DS:[0x47D1],0x7F
017D:9B9D xchg AX,word ptr DS:[0x47C6]
017D:9BA1 or AX,AX
017D:9BA3 je short 0x9BAB
017D:9BAB ret near
017D:9F9E mov word ptr DS:[0x477C],SI
017D:9FA2 call near 0x94F3
017D:9FA5 mov word ptr DS:[0x47BC],0xA6B0
017D:9FAB mov AX,word ptr DS:[SI]
017D:9FAD cmp AX,0xFFFF
017D:9FB0 je short 0x9F9C
017D:9FB2 test AL,0x80
017D:9FB4 je short 0x9FC0
017D:9FB6 test AL,0x40
017D:9FB8 jne short 0x9FC0
017D:9FBA and AL,byte ptr DS:[0x47C2]
017D:9FBE jne short 0x9FD3
017D:9FC0 push SI
017D:9FC1 mov AL,AH
017D:9FC3 mov AH,byte ptr DS:[SI+2]
017D:9FC6 rol AH,1
017D:9FC8 rol AH,1
017D:9FCA and AH,3
017D:9FCD call near 0xA396
017D:9FD0 pop SI
017D:9FD1 jne short 0x9FD8
017D:9FD3 add SI,4
017D:9FD6 jmp short 0x9FAB
017D:9FD8 cmp byte ptr DS:[0x46EB],0
017D:9FDD jne short 0x9FF7
017D:9FDF mov AX,word ptr DS:[0x47C4]
017D:9FE2 cmp AX,0x0010
017D:9FE5 jae short 0x9FF7
017D:9FF7 push SI
017D:9FF8 lods AX,word ptr DS:[SI]
017D:9FF9 mov word ptr DS:[0x47DE],AX
017D:9FFC lods AX,word ptr DS:[SI]
017D:9FFD xchg AH,AL
017D:9FFF and AX,0x03FF
017D:A002 or AX,0x0800
017D:A005 mov DI,word ptr DS:[0x47BC]
017D:A009 cmp DI,0xA6B0
017D:A00D je short 0xA034
017D:A034 cmp byte ptr DS:[0x00C6],0
017D:A039 jne short 0xA03E
017D:A03E pop SI
017D:A03F call near 0xC85B
017D:A042 cmp word ptr DS:[0x47B6],0
017D:A047 jne short 0xA0AA
017D:A049 mov AL,byte ptr DS:[SI]
017D:A04B and AL,0x0F
017D:A04D je short 0xA05E
017D:A05E mov AL,byte ptr DS:[SI+2]
017D:A061 and AL,0x0C
017D:A063 je short 0xA092
017D:A065 test byte ptr DS:[SI],0x80
017D:A068 jne short 0xA092
017D:A06A mov AX,SI
017D:A06C sub AX,0xAA78
017D:A06F shr AX,1
017D:A071 shr AX,1
017D:A073 mov BL,byte ptr DS:[0x47C4]
017D:A077 shl BL,1
017D:A079 shl BL,1
017D:A07B shl BL,1
017D:A07D or AH,BL
017D:A07F mov BP,word ptr DS:[0x11BD]
017D:A083 mov word ptr CS:[BP],AX
017D:A087 mov word ptr CS:[BP+2],0
017D:A08D add word ptr DS:[0x11BD],2
017D:A092 mov byte ptr DS:[0x0019],0xFF
017D:A097 or byte ptr DS:[SI],0x80
017D:A09A add SI,4
017D:A09D xor AL,AL
017D:A09F xchg AL,byte ptr DS:[0x47A8]
017D:A0A3 or AL,AL
017D:A0A5 je short 0xA0AA
017D:A0AA cmp byte ptr DS:[0x46EB],0
017D:A0AF jne short 0xA0E2
017D:A0B1 cmp word ptr DS:[0x47C4],0x0010
017D:A0B6 jae short 0xA0E2
017D:A0E2 cmp byte ptr DS:[0x00FB],0
017D:A0E7 js short 0xA0EF
017D:A0EF clc
017D:A0F0 ret near
017D:A30B lods AL,byte ptr ES:[SI]
017D:A30D cmp AL,0x80
017D:A30F jae short 0xA32A
017D:A311 push BX
017D:A312 mov BL,byte ptr ES:[SI]
017D:A315 inc SI
017D:A316 xor BH,BH
017D:A318 cmp AL,1
017D:A31A je short 0xA322
017D:A322 mov AL,byte ptr DS:[BX]
017D:A326 xor AH,AH
017D:A328 pop BX
017D:A329 ret near
017D:A396 sub SP,0x0032
017D:A399 mov BP,SP
017D:A39B shl AX,1
017D:A39D les SI,word ptr DS:[0xAA72]
017D:A3A1 add SI,AX
017D:A3A3 mov SI,word ptr ES:[SI-2]
017D:A3A7 call near 0xA30B
017D:A3AA mov DX,AX
017D:A3AC lods AL,byte ptr ES:[SI]
017D:A3AE cmp AL,0xFF
017D:A3B0 je short 0xA3CB
017D:A3CB mov SI,SP
017D:A3CD cmp SI,BP
017D:A3CF je short 0xA3E2
017D:A3E2 add SP,0x0032
017D:A3E5 or DX,DX
017D:A3E7 ret near
017D:A637 test word ptr DS:[0xDBC8],4
017D:A63D jne short 0xA644
017D:A644 mov AL,byte ptr DS:[0x288E]
017D:A647 mov AH,byte ptr DS:[0x28A6]
017D:A64B call far dword ptr DS:[0x39A5]
017D:A64F ret near
017D:A650 test word ptr DS:[0xDBC8],0x0400
017D:A656 jne short 0xA660
017D:A660 mov AH,byte ptr DS:[0x28AE]
017D:A664 mov AL,byte ptr DS:[0x2896]
017D:A667 cmp AL,4
017D:A669 jae short 0xA66D
017D:A66D call far dword ptr DS:[0x3985]
017D:A671 ret near
017D:A788 ret near
017D:A7A5 mov SI,0xA7C2
017D:A7A8 call near 0xDA5F
017D:A7AB mov word ptr DS:[0xDC26],0
017D:A7B1 call near 0xD61D
017D:A7B4 call near 0xABCC
017D:A7B7 je short 0xA788
017D:A87E pushf
017D:A87F sti
017D:A880 call near 0xAE2F
017D:A883 je short 0xA8AF
017D:A885 call near 0xAC14
017D:A888 mov AL,0x0B
017D:A88A call near 0xABE9
017D:A88D mov SI,0x3811
017D:A890 call far dword ptr DS:[0x3991]
017D:A894 push word ptr DS:[0xCE7A]
017D:A898 call near 0xA9E7
017D:A89B je short 0xA898
017D:A89D mov AX,word ptr DS:[0xCE7A]
017D:A8A0 pop BX
017D:A8A1 sub AX,BX
017D:A8A3 mov CX,0x0800
017D:A8A6 mul CX
017D:A8A8 mov word ptr DS:[0x2882],DX
017D:A8AC mov word ptr DS:[0x2884],AX
017D:A8AF popf
017D:A8B0 ret near
017D:A9A1 xor BX,BX
017D:A9A3 xchg BX,word ptr DS:[0x3821]
017D:A9A7 or BX,BX
017D:A9A9 je short 0xA9B7
017D:A9B7 clc
017D:A9B8 ret near
017D:A9E7 cmp byte ptr DS:[0x3817],3
017D:A9EC je short 0xA9F3
017D:A9EE cmp byte ptr DS:[0x381F],3
017D:A9F3 ret near
017D:AA0E ret near
017D:AA0F mov AX,word ptr DS:[0xDC1C]
017D:AA12 inc AX
017D:AA13 je short 0xAA0E
017D:ABCC cmp byte ptr DS:[0xDC2B],0
017D:ABD1 ret near
017D:ABE9 mov word ptr DS:[0x3811],0
017D:ABEF les DI,word ptr DS:[0x3811]
017D:ABF3 add word ptr DS:[0x3811],0x001A
017D:ABF8 xor AH,AH
017D:ABFA mov SI,AX
017D:ABFC add SI,0x00AE
017D:AC00 mov byte ptr DS:[0x376A],AL
017D:AC03 call near 0xF0B9
017D:AC06 sub CX,0x001A
017D:AC09 mov word ptr DS:[0x3815],CX
017D:AC0D mov word ptr DS:[0x3817],0x8101
017D:AC13 ret near
017D:AC14 push AX
017D:AC15 push BX
017D:AC16 push CX
017D:AC17 push SI
017D:AC18 push DI
017D:AC19 push BP
017D:AC1A push ES
017D:AC1B mov SI,0xAB92
017D:AC1E call near 0xDA5F
017D:AC21 call near 0xA9A1
017D:AC24 call far dword ptr DS:[0x3995]
017D:AC28 pop ES
017D:AC29 pop BP
017D:AC2A pop DI
017D:AC2B pop SI
017D:AC2C pop CX
017D:AC2D pop BX
017D:AC2E pop AX
017D:AC2F ret near
017D:AC30 call far dword ptr DS:[0x3999]
017D:AC34 ret near
017D:ACE6 call near 0xABCC
017D:ACE9 jne short 0xAD36
017D:ACEB test byte ptr DS:[0x3810],1
017D:ACF0 je short 0xAD37
017D:ACF2 cmp byte ptr DS:[0x227D],0
017D:ACF7 jne short 0xAD36
017D:AD36 ret near
017D:AD57 call near 0xAEB7
017D:AD5A mov AL,6
017D:AD5C jmp short 0xAD95
017D:AD95 xor AH,AH
017D:AD97 call near 0xAEC6
017D:AD9A jb short 0xADBD
017D:AD9C cmp AL,byte ptr DS:[0xDBCB]
017D:ADA0 je short 0xADBD
017D:ADA2 call near 0xAE62
017D:ADA5 mov byte ptr DS:[0xDBCB],AL
017D:ADA8 les SI,word ptr DS:[0xDBB6]
017D:ADAC mov AL,byte ptr DS:[0x3810]
017D:ADAF and AL,1
017D:ADB1 call far dword ptr DS:[0x3971]
017D:ADB5 mov byte ptr DS:[0xDBCD],AL
017D:ADB8 xor AX,AX
017D:ADBA mov word ptr DS:[0xDBD2],AX
017D:ADBD ret near
017D:AE28 test word ptr DS:[0xDBC8],0x0100
017D:AE2E ret near
017D:AE2F push AX
017D:AE30 push DS
017D:AE31 mov AX,0x10C8
017D:AE34 mov DS,AX
017D:AE36 test word ptr DS:[0xDBC8],1
017D:AE3C pop DS
017D:AE3D pop AX
017D:AE3E ret near
017D:AE3F call near 0xAE28
017D:AE42 je short 0xAE3E
017D:AE44 mov DI,0xDBB6
017D:AE47 mov AX,word ptr DS:[DI]
017D:AE49 or AX,word ptr DS:[DI+2]
017D:AE4C jne short 0xAE3E
017D:AE4E mov CX,0x9C40
017D:AE51 jmp near 0xF0F6
017D:AE54 call near 0xAE2F
017D:AE57 je short 0xAE3E
017D:AE59 mov DI,0x3811
017D:AE5C mov CX,0x4E20
017D:AE5F jmp near 0xF0F6
017D:AE62 cmp AL,byte ptr DS:[0xDBCA]
017D:AE66 je short 0xAE84
017D:AE68 call near 0xAEB7
017D:AE6B mov byte ptr DS:[0xDBCA],AL
017D:AE6E push AX
017D:AE6F add AX,0x00A4
017D:AE72 mov SI,AX
017D:AE74 les DI,word ptr DS:[0xDBB6]
017D:AE78 mov AX,ES
017D:AE7A cmp AX,word ptr DS:[0xCE68]
017D:AE7E jae short 0xAE85
017D:AE80 call near 0xF0B9
017D:AE83 pop AX
017D:AE84 ret near
017D:AEB7 push AX
017D:AEB8 mov byte ptr DS:[0xDBCB],0
017D:AEBD call far dword ptr DS:[0x3975]
017D:AEC1 mov byte ptr DS:[0xDBCD],AL
017D:AEC4 pop AX
017D:AEC5 ret near
017D:AEC6 test byte ptr DS:[0x2943],0x10
017D:AECB jne short 0xAED4
017D:AECD call near 0xAE28
017D:AED0 je short 0xAED4
017D:AED2 clc
017D:AED3 ret near
017D:B17A mov AL,byte ptr DS:[0x00C6]
017D:B17D push AX
017D:B17E or AL,0x80
017D:B180 mov byte ptr DS:[0x00C6],AL
017D:B183 call near 0x96B5
017D:B186 pop AX
017D:B187 mov byte ptr DS:[0x00C6],AL
017D:B18A ret near
017D:B58B call near 0xB5A0
017D:B58E les DI,word ptr DS:[0xDCFE]
017D:B592 add DI,AX
017D:B594 mov AX,BP
017D:B596 mul DX
017D:B598 shl AX,1
017D:B59A adc DX,0
017D:B59D add DI,DX
017D:B59F ret near
017D:B5A0 push BX
017D:B5A1 shl BX,1
017D:B5A3 shl BX,1
017D:B5A5 shl BX,1
017D:B5A7 jns short 0xB5B9
017D:B5A9 neg BX
017D:B5AB mov AX,word ptr DS:[BX+0x4948]
017D:B5AF neg AX
017D:B5B1 mov BP,word ptr DS:[BX+0x494A]
017D:B5B5 shl BP,1
017D:B5B7 pop BX
017D:B5B8 ret near
017D:B5B9 mov AX,word ptr DS:[BX+0x4948]
017D:B5BD mov BP,word ptr DS:[BX+0x494A]
017D:B5C1 shl BP,1
017D:B5C3 pop BX
017D:B5C4 ret near
017D:B5C5 call near 0xB58B
017D:B5C8 xor AX,AX
017D:B5CA div BP
017D:B5CC mov DX,AX
017D:B5CE ret near
017D:B930 mov byte ptr DS:[0xDD03],0
017D:B935 mov SI,0xB9AE
017D:B938 call near 0xDA5F
017D:B93B mov SI,0xBE57
017D:B93E jmp near 0xDA5F
017D:C07C push word ptr DS:[0xDBD6]
017D:C080 pop word ptr DS:[0xDBDA]
017D:C084 ret near
017D:C08E push word ptr DS:[0xDBD8]
017D:C092 pop word ptr DS:[0xDBDA]
017D:C096 ret near
017D:C097 call near 0xC07C
017D:C09A push word ptr DS:[0xDBD8]
017D:C09E push word ptr DS:[0xDBD6]
017D:C0A2 pop word ptr DS:[0xDBD8]
017D:C0A6 call near BP
017D:C0A8 pop word ptr DS:[0xDBD8]
017D:C0AC ret near
017D:C0AD mov ES,word ptr DS:[0xDBDA]
017D:C0B1 call far dword ptr DS:[0x38D5]
017D:C0B5 ret near
017D:C0F4 mov AX,word ptr DS:[0xDBD6]
017D:C0F7 cmp AX,word ptr DS:[0xDBD8]
017D:C0FB je short 0xC101
017D:C0FD call far dword ptr DS:[0x3935]
017D:C101 ret near
017D:C108 mov byte ptr DS:[0xDCE6],0x80
017D:C10D push AX
017D:C10E push DX
017D:C10F call near 0xC097
017D:C112 pop DX
017D:C113 pop AX
017D:C114 push DS
017D:C115 mov SI,word ptr DS:[0xDBDE]
017D:C119 mov ES,word ptr DS:[0xDBD8]
017D:C11D mov DS,word ptr DS:[0xDBD6]
017D:C121 mov BP,0xCE7A
017D:C124 call far dword ptr SS:[0x3921]
017D:C129 pop DS
017D:C12A call near 0xC4CD
017D:C12D call far dword ptr DS:[0x3935]
017D:C131 mov byte ptr DS:[0xDCE6],0
017D:C136 ret near
017D:C137 xor AX,AX
017D:C139 jmp short 0xC13E
017D:C13E or AX,AX
017D:C140 js short 0xC1A9
017D:C142 push BX
017D:C143 mov BX,AX
017D:C145 xchg BX,word ptr DS:[0x2784]
017D:C149 cmp AX,BX
017D:C14B je short 0xC1A8
017D:C14D push SI
017D:C14E push DI
017D:C14F shl BX,1
017D:C151 js short 0xC15B
017D:C153 mov SI,word ptr DS:[0xCE7B]
017D:C157 mov word ptr DS:[BX-9588],SI
017D:C15B mov SI,AX
017D:C15D shl SI,1
017D:C15F shl SI,1
017D:C161 add SI,0xD844
017D:C165 les DI,word ptr DS:[SI]
017D:C167 mov BX,ES
017D:C169 or BX,BX
017D:C16B je short 0xC177
017D:C177 push CX
017D:C178 push DX
017D:C179 push BP
017D:C17A push SI
017D:C17B mov SI,AX
017D:C17D call near 0xF0B9
017D:C180 cmp word ptr ES:[DI],2
017D:C184 jbe short 0xC189
017D:C189 pop SI
017D:C18A mov DI,word ptr ES:[DI]
017D:C18D sub CX,DI
017D:C18F mov word ptr DS:[SI],DI
017D:C191 mov word ptr DS:[SI+2],ES
017D:C194 mov AX,word ptr DS:[0x2784]
017D:C197 call far dword ptr DS:[0x3905]
017D:C19B pop BP
017D:C19C pop DX
017D:C19D pop CX
017D:C19E mov word ptr DS:[0xDBB0],DI
017D:C1A2 mov word ptr DS:[0xDBB2],ES
017D:C1A6 pop DI
017D:C1A7 pop SI
017D:C1A8 pop BX
017D:C1A9 ret near
017D:C1BA push CX
017D:C1BB push DX
017D:C1BC push DI
017D:C1BD lods AX,word ptr ES:[SI]
017D:C1BF cmp AX,0x0100
017D:C1C2 jne short 0xC1C9
017D:C1C9 mov BX,AX
017D:C1CB inc AX
017D:C1CC je short 0xC1F0
017D:C1CE mov CL,BH
017D:C1D0 xor BH,BH
017D:C1D2 and CX,0x00FF
017D:C1D6 jne short 0xC1DA
017D:C1DA mov AX,BX
017D:C1DC add BX,BX
017D:C1DE add BX,AX
017D:C1E0 mov AX,CX
017D:C1E2 add CX,CX
017D:C1E4 add CX,AX
017D:C1E6 mov DX,SI
017D:C1E8 add SI,CX
017D:C1EA call far dword ptr DS:[0x38BD]
017D:C1EE jmp short 0xC1BD
017D:C1F0 pop DI
017D:C1F1 pop DX
017D:C1F2 pop CX
017D:C1F3 ret near
017D:C412 push DS
017D:C413 mov ES,word ptr DS:[0xDBDE]
017D:C417 mov DS,word ptr DS:[0xDBDA]
017D:C41B call far dword ptr SS:[0x38E1]
017D:C420 pop DS
017D:C421 ret near
017D:C4CD push DS
017D:C4CE mov ES,word ptr DS:[0xDBD8]
017D:C4D2 mov DS,word ptr DS:[0xDBD6]
017D:C4D6 call far dword ptr SS:[0x38F1]
017D:C4DB pop DS
017D:C4DC ret near
017D:C85B mov AX,word ptr DS:[0xCE7A]
017D:C85E mov word ptr DS:[0x476E],AX
017D:C861 mov word ptr DS:[0x4772],0x1770
017D:C867 ret near
017D:C921 mov BX,0x33A3
017D:C924 add BX,AX
017D:C926 add BX,AX
017D:C928 mov BX,word ptr DS:[BX]
017D:C92A ret near
017D:C92B mov word ptr DS:[0xDC00],AX
017D:C92E call near 0xCA01
017D:C931 call near 0xCE1A
017D:C934 mov byte ptr DS:[0xDBE7],0
017D:C939 call near 0xCE01
017D:C93C mov AX,word ptr DS:[0xDC00]
017D:C93F mov word ptr DS:[0xDC02],AX
017D:C942 call near 0xC921
017D:C945 mov AX,word ptr DS:[BX]
017D:C947 mov word ptr DS:[0xDBFE],AX
017D:C94A lea DX,BX+2
017D:C94D call near 0xF229
017D:C950 mov word ptr DS:[0x35A6],BX
017D:C954 mov word ptr DS:[0xDC04],AX
017D:C957 mov word ptr DS:[0xDC06],DX
017D:C95B mov word ptr DS:[0xDC08],CX
017D:C95F mov word ptr DS:[0xDC0A],BP
017D:C963 push word ptr DS:[0xDC1A]
017D:C967 push word ptr DS:[0xDC0C]
017D:C96B call near 0xCD8F
017D:C96E jb short 0xC988
017D:C970 add SI,AX
017D:C972 jb short 0xC97A
017D:C974 cmp SI,word ptr DS:[0xCE74]
017D:C978 jbe short 0xC980
017D:C980 sub AX,2
017D:C983 mov CX,AX
017D:C985 call near 0xCDBF
017D:C988 pop word ptr DS:[0xDC0C]
017D:C98C pop word ptr DS:[0xDC1A]
017D:C990 jb short 0xC9E7
017D:C992 les SI,word ptr DS:[0xDC0C]
017D:C996 lods AX,word ptr ES:[SI]
017D:C998 add AX,SI
017D:C99A jb short 0xC9A2
017D:C99C cmp AX,word ptr DS:[0xCE74]
017D:C9A0 jbe short 0xC9A4
017D:C9A4 mov byte ptr DS:[0xDBB4],0xFF
017D:C9A9 call near 0xC1BA
017D:C9AC dec SI
017D:C9AD inc SI
017D:C9AE cmp byte ptr ES:[SI],0xFF
017D:C9B2 je short 0xC9AD
017D:C9B4 xor BX,BX
017D:C9B6 test byte ptr DS:[0xDBFE],4
017D:C9BB je short 0xC9BF
017D:C9BF mov CX,word ptr ES:[BX+SI]
017D:C9C2 mov BX,word ptr ES:[BX+SI+2]
017D:C9C6 mov AX,word ptr DS:[0xDC04]
017D:C9C9 add AX,CX
017D:C9CB mov word ptr DS:[0xDBF6],AX
017D:C9CE mov AX,word ptr DS:[0xDC06]
017D:C9D1 adc AX,BX
017D:C9D3 mov word ptr DS:[0xDBF8],AX
017D:C9D6 mov AX,word ptr DS:[0xDC08]
017D:C9D9 sub AX,CX
017D:C9DB mov word ptr DS:[0xDBFA],AX
017D:C9DE mov AX,word ptr DS:[0xDC0A]
017D:C9E1 sbb AX,BX
017D:C9E3 mov word ptr DS:[0xDBFC],AX
017D:C9E6 clc
017D:C9E7 ret near
017D:C9F4 push word ptr DS:[0xDBE8]
017D:C9F8 call near 0xCA60
017D:C9FB pop AX
017D:C9FC cmp AX,word ptr DS:[0xDBE8]
017D:CA00 ret near
017D:CA01 xor BX,BX
017D:CA03 xchg BX,word ptr DS:[0x35A6]
017D:CA07 or BX,BX
017D:CA09 je short 0xCA18
017D:CA0B call near 0xCE01
017D:CA0E cmp BX,word ptr DS:[0xDBBA]
017D:CA12 je short 0xCA18
017D:CA18 xor CX,CX
017D:CA1A ret near
017D:CA1B call near 0xC92B
017D:CA1E jb short 0xCA01
017D:CA20 call near 0xCDA0
017D:CA23 jb short 0xCA01
017D:CA25 mov byte ptr DS:[0xDCE6],0
017D:CA2A les SI,word ptr DS:[0xDC10]
017D:CA2E lods AX,word ptr ES:[SI]
017D:CA30 mov BP,word ptr DS:[0xDBDE]
017D:CA34 call near 0xCCF4
017D:CA37 call near 0xAA0F
017D:CA3A call near 0xCC96
017D:CA3D call near 0xCE1A
017D:CA40 inc word ptr DS:[0xDBE8]
017D:CA44 inc word ptr DS:[0xDBEA]
017D:CA48 test byte ptr DS:[0xDBFE],0x40
017D:CA4D jne short 0xCA59
017D:CA4F mov CX,0x0032
017D:CA52 push CX
017D:CA53 call near 0xCB1A
017D:CA56 pop CX
017D:CA57 loop 0xCA52
017D:CA59 mov AX,word ptr DS:[0xCE7A]
017D:CA5C mov word ptr DS:[0xDC22],AX
017D:CA5F ret near
017D:CA60 cmp word ptr DS:[0x35A6],0
017D:CA65 je short 0xCA9A
017D:CA67 cmp byte ptr DS:[0xDBFE],0
017D:CA6C jns short 0xCA71
017D:CA71 call near 0xCAA0
017D:CA74 jae short 0xCA7B
017D:CA7B call near 0xCAD4
017D:CA7E jb short 0xCA8F
017D:CA80 mov AX,word ptr DS:[0xDC1E]
017D:CA83 inc AX
017D:CA84 je short 0xCA89
017D:CA89 call near 0xCC96
017D:CA8C call near 0xCC4E
017D:CA8F mov AL,byte ptr DS:[0xDBFE]
017D:CA92 and AL,0x80
017D:CA94 mov byte ptr DS:[0xDBB5],AL
017D:CA97 call near 0xCB1A
017D:CA9A mov byte ptr DS:[0xDBB5],0
017D:CA9F ret near
017D:CAA0 cmp word ptr DS:[0xDC16],0
017D:CAA5 ja short 0xCAD3
017D:CAA7 mov CX,word ptr DS:[0xDC1A]
017D:CAAB stc
017D:CAAC jcxz short 0xCAD3
017D:CAAE les SI,word ptr DS:[0xDC10]
017D:CAB2 lods AX,word ptr ES:[SI]
017D:CAB4 cmp word ptr ES:[SI],0x6D6D
017D:CAB9 je short 0xCABF
017D:CABB cmp CX,AX
017D:CABD jb short 0xCAD3
017D:CABF mov BP,word ptr DS:[0xDBD6]
017D:CAC3 test byte ptr DS:[0xDBFE],0x40
017D:CAC8 je short 0xCACE
017D:CACE call near 0xCCF4
017D:CAD1 xor AX,AX
017D:CAD3 ret near
017D:CAD4 mov AX,word ptr DS:[0xDC1C]
017D:CAD7 inc AX
017D:CAD8 jne short 0xCAF0
017D:CADA mov AX,word ptr DS:[0xCE7A]
017D:CADD sub AX,word ptr DS:[0xDC22]
017D:CAE1 or AH,AH
017D:CAE3 jne short 0xCAEB
017D:CAE5 cmp AL,byte ptr DS:[0xDBFF]
017D:CAE9 jb short 0xCAEF
017D:CAEB call near 0xCA59
017D:CAEE clc
017D:CAEF ret near
017D:CB00 mov AX,word ptr DS:[0xDBEA]
017D:CB03 cmp AX,word ptr DS:[0xDBEE]
017D:CB07 je short 0xCB61
017D:CB09 mov AX,word ptr DS:[0xDC08]
017D:CB0C or AX,word ptr DS:[0xDC0A]
017D:CB10 je short 0xCB61
017D:CB12 call near 0xCD8F
017D:CB15 jb short 0xCB44
017D:CB17 call near 0xCC0C
017D:CB1A mov CX,word ptr DS:[0xDC20]
017D:CB1E jcxz short 0xCB00
017D:CB20 cmp byte ptr DS:[0xDBFE],0
017D:CB25 js short 0xCB38
017D:CB27 mov AX,word ptr DS:[0xDC04]
017D:CB2A neg AX
017D:CB2C and AX,0x07FF
017D:CB2F add AH,8
017D:CB32 cmp AX,CX
017D:CB34 jae short 0xCB38
017D:CB36 mov CX,AX
017D:CB38 call near 0xCC2B
017D:CB3B jb short 0xCB44
017D:CB3D sub word ptr DS:[0xDC20],CX
017D:CB41 jmp near 0xCDBF
017D:CB44 ret near
017D:CC0C add SI,AX
017D:CC0E jb short 0xCC16
017D:CC10 cmp SI,word ptr DS:[0xCE74]
017D:CC14 jbe short 0xCC20
017D:CC16 xor CX,CX
017D:CC18 xchg CX,word ptr DS:[0xDC0C]
017D:CC1C mov word ptr DS:[0xDC18],CX
017D:CC20 sub AX,2
017D:CC23 mov word ptr DS:[0xDC20],AX
017D:CC26 inc word ptr DS:[0xDBEA]
017D:CC2A ret near
017D:CC2B mov AX,word ptr DS:[0xDC0C]
017D:CC2E mov BX,word ptr DS:[0xDC10]
017D:CC32 cmp AX,BX
017D:CC34 jae short 0xCC3F
017D:CC36 add AX,CX
017D:CC38 add AX,0x0012
017D:CC3B cmp BX,AX
017D:CC3D jb short 0xCC4D
017D:CC3F mov AX,word ptr DS:[0xDC1A]
017D:CC42 add AX,0x000A
017D:CC45 add AX,CX
017D:CC47 jb short 0xCC4D
017D:CC49 cmp word ptr DS:[0xDC18],AX
017D:CC4D ret near
017D:CC4E les SI,word ptr DS:[0xDC10]
017D:CC52 lods AX,word ptr ES:[SI]
017D:CC54 sub word ptr DS:[0xDC1A],AX
017D:CC58 add SI,AX
017D:CC5A jb short 0xCC62
017D:CC5C cmp SI,word ptr DS:[0xCE74]
017D:CC60 jbe short 0xCC6A
017D:CC6A add word ptr DS:[0xDC10],AX
017D:CC6E mov AX,word ptr DS:[0xDBE8]
017D:CC71 inc AX
017D:CC72 cmp AX,word ptr DS:[0xDBEC]
017D:CC76 jbe short 0xCC81
017D:CC81 mov word ptr DS:[0xDBE8],AX
017D:CC84 ret near
017D:CC85 cmp byte ptr DS:[0xDBE7],0
017D:CC8A je short 0xCC91
017D:CC91 ret near
017D:CC96 mov AX,word ptr DS:[0x38FB]
017D:CC99 mov word ptr CS:[0xCC94],AX
017D:CC9D xor BP,BP
017D:CC9F xchg BP,word ptr DS:[0xDC16]
017D:CCA3 or BP,BP
017D:CCA5 je short 0xCC4D
017D:CCA7 mov SI,word ptr DS:[0xDC14]
017D:CCAB mov AL,byte ptr DS:[0xDBFE]
017D:CCAE test AL,0x30
017D:CCB0 jne short 0xCCEA
017D:CCB2 push DS
017D:CCB3 test word ptr DS:[0xDC24],0x0400
017D:CCB9 jne short 0xCCE1
017D:CCE1 pop DS
017D:CCE2 ret near
017D:CCF4 mov word ptr DS:[0xDC1C],0xFFFF
017D:CCFA mov word ptr DS:[0xDC1E],0xFFFF
017D:CD00 add AX,SI
017D:CD02 jb short 0xCD0A
017D:CD04 cmp AX,word ptr DS:[0xCE74]
017D:CD08 jbe short 0xCD0C
017D:CD0C lods AX,word ptr ES:[SI]
017D:CD0E cmp AX,0x6473
017D:CD11 jne short 0xCD25
017D:CD25 cmp AX,0x6C70
017D:CD28 jne short 0xCD37
017D:CD37 cmp AX,0x6D6D
017D:CD3A jne short 0xCD4E
017D:CD4E push DS
017D:CD4F push ES
017D:CD50 mov ES,BP
017D:CD52 xor DI,DI
017D:CD54 test AH,4
017D:CD57 je short 0xCD5D
017D:CD59 mov ES,word ptr DS:[0xDBDA]
017D:CD5D mov word ptr DS:[0xDC16],ES
017D:CD61 mov word ptr DS:[0xDC14],DI
017D:CD65 mov word ptr DS:[0xDC24],AX
017D:CD68 pop DS
017D:CD69 mov CX,AX
017D:CD6B lods AX,word ptr DS:[SI]
017D:CD6C xchg CX,AX
017D:CD6D test AH,4
017D:CD70 jne short 0xCD7C
017D:CD7C call near 0xF403
017D:CD7F pop DS
017D:CD80 ret near
017D:CD8F mov CX,2
017D:CD92 call near 0xCDBF
017D:CD95 jb short 0xCD9F
017D:CD97 les SI,word ptr DS:[0xDC0C]
017D:CD9B mov AX,word ptr ES:[SI-2]
017D:CD9F ret near
017D:CDA0 call near 0xCE1A
017D:CDA3 call near 0xCD8F
017D:CDA6 jb short 0xCE00
017D:CDA8 mov DI,word ptr DS:[0xCE74]
017D:CDAC sub DI,AX
017D:CDAE sub DI,2
017D:CDB1 mov word ptr DS:[0xDC10],DI
017D:CDB5 stos word ptr ES:[DI],AX
017D:CDB6 mov word ptr DS:[0xDC0C],DI
017D:CDBA mov CX,AX
017D:CDBC sub CX,2
017D:CDBF mov BX,word ptr DS:[0x35A6]
017D:CDC3 cmp BX,1
017D:CDC6 jb short 0xCE00
017D:CDC8 push CX
017D:CDC9 mov CX,word ptr DS:[0xDC06]
017D:CDCD mov DX,word ptr DS:[0xDC04]
017D:CDD1 mov AX,0x4200
017D:CDD4 int 0x21
017D:CDD6 pop CX
017D:CDD7 push DS
017D:CDD8 lds DX,word ptr DS:[0xDC0C]
017D:CDDC mov AH,0x3F
017D:CDDE int 0x21
017D:CDE0 pop DS
017D:CDE1 cmp AX,CX
017D:CDE3 jb short 0xCDC8
017D:CDE5 sub word ptr DS:[0xDC08],AX
017D:CDE9 sbb word ptr DS:[0xDC0A],0
017D:CDEE add word ptr DS:[0xDC04],AX
017D:CDF2 adc word ptr DS:[0xDC06],0
017D:CDF7 add word ptr DS:[0xDC0C],AX
017D:CDFB add word ptr DS:[0xDC1A],AX
017D:CDFF clc
017D:CE00 ret near
017D:CE01 mov word ptr DS:[0xDBE8],0
017D:CE07 mov word ptr DS:[0xDBEA],0
017D:CE0D mov word ptr DS:[0xDBEC],0xFFFF
017D:CE13 mov word ptr DS:[0xDBEE],0xFFFF
017D:CE19 ret near
017D:CE1A mov AX,word ptr DS:[0xDBDE]
017D:CE1D mov word ptr DS:[0xDC0E],AX
017D:CE20 mov word ptr DS:[0xDC12],AX
017D:CE23 xor AX,AX
017D:CE25 mov word ptr DS:[0xDC0C],AX
017D:CE28 mov word ptr DS:[0xDC10],AX
017D:CE2B mov word ptr DS:[0xDC1A],AX
017D:CE2E mov word ptr DS:[0xDC20],AX
017D:CE31 mov word ptr DS:[0xDC16],AX
017D:CE34 mov AX,word ptr DS:[0xCE74]
017D:CE37 mov word ptr DS:[0xDC18],AX
017D:CE3A ret near
017D:CE6C test byte ptr DS:[0x2943],2
017D:CE71 jne short 0xCE7B
017D:CE73 cmp word ptr DS:[0x39A9],0x015E
017D:CE79 jae short 0xCE8A
017D:CE7B mov AX,2
017D:CE7E call near 0xC921
017D:CE81 and byte ptr DS:[BX],0xFB
017D:CE84 inc AX
017D:CE85 cmp AX,9
017D:CE88 jb short 0xCE7E
017D:CE8A test byte ptr DS:[0x2943],3
017D:CE8F je short 0xCE9F
017D:CE9F mov AX,2
017D:CEA2 push AX
017D:CEA3 call near 0xCEB0
017D:CEA6 pop AX
017D:CEA7 inc AX
017D:CEA8 cmp AX,8
017D:CEAB jb short 0xCEA2
017D:CEAD jmp near 0xCA01
017D:CEB0 call near 0xC921
017D:CEB3 push BX
017D:CEB4 call near 0xC92B
017D:CEB7 pop DI
017D:CEB8 jb short 0xCEC8
017D:CEBA test byte ptr DS:[DI],8
017D:CEBD je short 0xCEC8
017D:CEBF sub DI,8
017D:CEC2 mov SI,0xDBF6
017D:CEC5 call near 0x5B99
017D:CEC8 ret near
017D:CFB9 xor BX,BX
017D:CFBB mov DI,0xD7F4
017D:CFBE push DS
017D:CFBF pop ES
017D:CFC0 mov SI,word ptr DS:[BX-21898]
017D:CFC4 cmp word ptr DS:[SI],-1
017D:CFC7 jne short 0xCFCE
017D:CFC9 add BX,2
017D:CFCC jmp short 0xCFC0
017D:CFCE mov AX,word ptr DS:[SI+2]
017D:CFD1 xchg AL,AH
017D:CFD3 and AX,0x03FF
017D:CFD6 dec AX
017D:CFD7 stos word ptr ES:[DI],AX
017D:CFD8 and BX,-16
017D:CFDB add BX,0x0010
017D:CFDE cmp BX,0x0110
017D:CFE2 jb short 0xCFC0
017D:CFE4 mov AL,byte ptr DS:[0xCEEB]
017D:CFE7 mov SI,0x00BB
017D:CFEA cmp AL,6
017D:CFEC jne short 0xCFF1
017D:CFF1 mov DI,0xCEEC
017D:CFF4 push DS
017D:CFF5 pop ES
017D:CFF6 call near 0xF0B9
017D:CFF9 mov AL,0xC0
017D:CFFB add AL,byte ptr DS:[0xCEEB]
017D:CFFF xor AH,AH
017D:D001 mov SI,AX
017D:D003 les DI,word ptr DS:[0x47AC]
017D:D007 call near 0xF0B9
017D:D00A call near 0x0098
017D:D00D jmp short 0xD01A
017D:D01A mov AL,0x9A
017D:D01C add AL,byte ptr DS:[0xCEEB]
017D:D020 cmp AL,byte ptr DS:[0x477E]
017D:D024 je short 0xD03B
017D:D026 push SI
017D:D027 mov byte ptr DS:[0x477E],AL
017D:D02A xor AH,AH
017D:D02C mov SI,AX
017D:D02E les DI,word ptr DS:[0x47B0]
017D:D032 call near 0xF0B9
017D:D035 push CX
017D:D036 call near 0x0098
017D:D039 pop CX
017D:D03A pop SI
017D:D03B ret near
017D:D41B mov BP,word ptr DS:[0x21DA]
017D:D41F mov BP,word ptr SS:[BP]
017D:D422 ret near
017D:D61D push AX
017D:D61E mov AX,0x009F
017D:D621 call near 0xE270
017D:D624 call near 0xD41B
017D:D627 mov SI,0x1F7E
017D:D62A cmp word ptr DS:[SI+2],AX
017D:D62D mov word ptr DS:[SI+2],AX
017D:D630 je short 0xD649
017D:D632 cmp BP,SI
017D:D634 jne short 0xD649
017D:D649 call near 0xE283
017D:D64C pop AX
017D:D64D ret near
017D:D9D2 call near 0xACE6
017D:D9D5 mov AX,word ptr DS:[0xCE7A]
017D:D9D8 mov CX,AX
017D:D9DA mov BX,AX
017D:D9DC mov SI,0xDC68
017D:D9DF xchg CX,word ptr DS:[SI]
017D:D9E1 sub BX,CX
017D:D9E3 mov CX,word ptr DS:[SI+2]
017D:D9E6 jcxz short 0xDA03
017D:DA03 ret near
017D:DA53 mov word ptr DS:[0xDC6A],0
017D:DA59 mov byte ptr DS:[0x46D7],0
017D:DA5E ret near
017D:DA5F mov DI,0xDC6A
017D:DA62 mov CX,word ptr DS:[DI]
017D:DA64 jcxz short 0xDA72
017D:DA72 ret near
017D:DAE3 test byte ptr DS:[0x2942],0x40
017D:DAE8 jne short 0xDB02
017D:DAEA mov AX,word ptr DS:[0xDC36]
017D:DAED mov DX,word ptr DS:[0xDC38]
017D:DAF1 mov CX,word ptr DS:[0x2580]
017D:DAF5 shl AX,CL
017D:DAF7 mov CL,CH
017D:DAF9 shl DX,CL
017D:DAFB mov CX,AX
017D:DAFD mov AX,4
017D:DB00 int 0x33
017D:DB02 ret near
017D:DB03 call near 0xDBB2
017D:DB06 mov word ptr DS:[0xDC36],DX
017D:DB0A mov word ptr DS:[0xDC38],BX
017D:DB0E call near 0xDAE3
017D:DB11 jmp near 0xDBEC
017D:DB14 mov DI,0xDC3A
017D:DB17 mov word ptr DS:[DI],CX
017D:DB19 mov word ptr DS:[DI+2],DX
017D:DB1C mov word ptr DS:[DI+4],AX
017D:DB1F mov word ptr DS:[DI+6],BX
017D:DB22 test byte ptr DS:[0x2942],0x40
017D:DB27 jne short 0xDB43
017D:DB29 push AX
017D:DB2A push BX
017D:DB2B mov AL,byte ptr DS:[0x2580]
017D:DB2E call near 0xDB44
017D:DB31 mov AX,7
017D:DB34 int 0x33
017D:DB36 pop DX
017D:DB37 pop CX
017D:DB38 mov AL,byte ptr DS:[0x2581]
017D:DB3B call near 0xDB44
017D:DB3E mov AX,8
017D:DB41 int 0x33
017D:DB43 ret near
017D:DB44 xchg CX,AX
017D:DB45 shl AX,CL
017D:DB47 shl DX,CL
017D:DB49 mov CX,AX
017D:DB4B ret near
017D:DBB2 push AX
017D:DBB3 mov AL,byte ptr DS:[0xDC46]
017D:DBB6 dec byte ptr DS:[0xDC46]
017D:DBBA js short 0xDBC0
017D:DBC0 or AL,AL
017D:DBC2 js short 0xDBC8
017D:DBC8 pop AX
017D:DBC9 ret near
017D:DBEC inc byte ptr DS:[0xDC46]
017D:DBF0 js short 0xDC1A
017D:DC1A ret near
017D:DD63 call near 0xDE7B
017D:DD66 call near 0xDE54
017D:DD69 je short 0xDDAE
017D:DD6B cmp byte ptr DS:[0xCEE8],0
017D:DD70 jne short 0xDDAE
017D:DD72 test byte ptr DS:[0x2942],0x40
017D:DD77 jne short 0xDD89
017D:DD79 mov AX,3
017D:DD7C int 0x33
017D:DD7E xchg BX,SI
017D:DD80 xor BX,SI
017D:DD82 and BX,SI
017D:DD84 and BL,7
017D:DD87 jne short 0xDDAE
017D:DD89 test byte ptr DS:[0x2942],0x80
017D:DD8E je short 0xDD9E
017D:DD9E push SI
017D:DD9F push DI
017D:DDA0 call near 0xE3CC
017D:DDA3 mov word ptr DS:[0],AX
017D:DDA6 call near 0xD9D2
017D:DDA9 pop DI
017D:DDAA pop SI
017D:DDAB or AL,1
017D:DDAD ret near
017D:DDE7 pushf
017D:DDE8 call near 0xDE4E
017D:DDEB popf
017D:DDEC call near 0xE283
017D:DDEF ret near
017D:DE07 push AX
017D:DE08 or AL,1
017D:DE0A pop AX
017D:DE0B ret near
017D:DE0C cmp byte ptr DS:[0xDBCD],0
017D:DE11 jns short 0xDE07
017D:DE13 call near 0xE270
017D:DE16 mov byte ptr DS:[0xCEE8],0
017D:DE1B mov SI,0xFFFF
017D:DE1E mov DI,SI
017D:DE20 mov AX,0x0060
017D:DE23 sub AX,word ptr DS:[0xDBD0]
017D:DE27 xor AH,AH
017D:DE29 mov DL,6
017D:DE2B div DL
017D:DE2D and AL,0x0F
017D:DE2F mov DX,word ptr DS:[0xDBCE]
017D:DE33 shl DX,1
017D:DE35 shl DX,1
017D:DE37 shl DX,1
017D:DE39 shl DX,1
017D:DE3B or DL,AL
017D:DE3D cmp BX,DX
017D:DE3F jbe short 0xDE4A
017D:DE4A or AL,1
017D:DE4C jmp short 0xDDE7
017D:DE4E mov byte ptr DS:[0xCEE8],0
017D:DE53 ret near
017D:DE54 mov byte ptr DS:[0xCEE9],0
017D:DE59 cmp byte ptr DS:[0xCEE8],1
017D:DE5E jne short 0xDE67
017D:DE67 ret near
017D:DE7A ret near
017D:DE7B cmp byte ptr DS:[0xCE9A],0
017D:DE80 je short 0xDE7A
017D:E270 push BX
017D:E271 push CX
017D:E272 push DX
017D:E273 push SI
017D:E274 push DI
017D:E275 push BP
017D:E276 mov BP,SP
017D:E278 xchg AX,word ptr SS:[BP+0x0C]
017D:E27B push AX
017D:E27C mov AX,word ptr SS:[BP+0x0C]
017D:E27F mov BP,word ptr SS:[BP]
017D:E282 ret near
017D:E283 pop AX
017D:E284 mov BP,SP
017D:E286 xchg AX,word ptr SS:[BP+0x0C]
017D:E289 pop BP
017D:E28A pop DI
017D:E28B pop SI
017D:E28C pop DX
017D:E28D pop CX
017D:E28E pop BX
017D:E28F ret near
017D:E3CC push DX
017D:E3CD mov AX,word ptr DS:[0xD826]
017D:E3D0 mov DX,0xCBD1
017D:E3D3 mul DX
017D:E3D5 inc AX
017D:E3D6 mov word ptr DS:[0xD826],AX
017D:E3D9 mov AL,AH
017D:E3DB mov AH,DL
017D:E3DD pop DX
017D:E3DE ret near
017D:E4AD mov SI,0x0080
017D:E4B0 lods AL,byte ptr DS:[SI]
017D:E4B1 xor AH,AH
017D:E4B3 mov BP,AX
017D:E4B5 add BP,SI
017D:E4B7 push CS
017D:E4B8 pop ES
017D:E4B9 call near 0xE56B
017D:E4BC jb short 0xE4E5
017D:E4BE je short 0xE4B9
017D:E4C0 mov DL,AL
017D:E4C2 call near 0xE56B
017D:E4C5 jbe short 0xE542
017D:E4C7 mov AH,AL
017D:E4C9 call near 0xE56B
017D:E4CC jbe short 0xE542
017D:E4CE xchg AL,DL
017D:E4D0 mov DI,0xE40C
017D:E4D3 mov CX,0x0017
017D:E4D6 scas AX,word ptr ES:[DI]
017D:E4D7 jne short 0xE4DE
017D:E4D9 cmp DL,byte ptr ES:[DI]
017D:E4DC je short 0xE4E6
017D:E4DE add DI,5
017D:E4E1 loop 0xE4D6
017D:E4E5 ret near
017D:E4E6 mov AX,0x10C8
017D:E4E9 mov ES,AX
017D:E4EB mov BL,byte ptr CS:[DI+1]
017D:E4EF xor BH,BH
017D:E4F1 add BX,0x2942
017D:E4F5 mov AL,byte ptr CS:[DI+2]
017D:E4F9 or byte ptr ES:[BX],AL
017D:E4FC mov BX,word ptr CS:[DI+3]
017D:E500 or BX,BX
017D:E502 je short 0xE542
017D:E504 call near 0xE56B
017D:E507 jb short 0xE4E5
017D:E509 je short 0xE542
017D:E50B dec SI
017D:E50C cmp BX,0x3826
017D:E510 je short 0xE54D
017D:E512 xor DX,DX
017D:E514 call near 0xE56B
017D:E517 mov AH,AL
017D:E519 jbe short 0xE537
017D:E51B sub AL,0x30
017D:E51D jb short 0xE537
017D:E51F cmp AL,9
017D:E521 jbe short 0xE52B
017D:E52B shl DX,1
017D:E52D shl DX,1
017D:E52F shl DX,1
017D:E531 shl DX,1
017D:E533 or DL,AL
017D:E535 jmp short 0xE514
017D:E537 mov word ptr ES:[BX],DX
017D:E53A add BX,2
017D:E53D cmp AH,0x20
017D:E540 ja short 0xE512
017D:E542 dec SI
017D:E543 call near 0xE56B
017D:E546 jb short 0xE4E5
017D:E548 jne short 0xE543
017D:E54A jmp near 0xE4B7
017D:E56B mov AL,0x0D
017D:E56D cmp SI,BP
017D:E56F jae short 0xE578
017D:E571 lods AL,byte ptr DS:[SI]
017D:E572 cmp AL,0x61
017D:E574 jb short 0xE578
017D:E578 cmp AL,0x20
017D:E57A ret near
017D:E57B push CX
017D:E57C push SI
017D:E57D add AX,0x00C8
017D:E580 mov SI,AX
017D:E582 call near 0xF0B9
017D:E585 pop SI
017D:E586 pop CX
017D:E587 mov AX,ES
017D:E589 sub AX,0x0010
017D:E58C mov word ptr DS:[SI],AX
017D:E58E add SI,4
017D:E591 loop 0xE58C
017D:E593 ret near
017D:E594 mov AX,0x10C8
017D:E597 mov ES,AX
017D:E599 mov CX,0xDD1D
017D:E59C mov DI,0x3CBC
017D:E59F sub CX,DI
017D:E5A1 cld
017D:E5A2 xor AX,AX
017D:E5A4 rep stos byte ptr ES:[DI],AL
017D:E5A6 mov AX,word ptr DS:[2]
017D:E5A9 push ES
017D:E5AA pop DS
017D:E5AB mov word ptr DS:[0xCE68],AX
017D:E5AE mov CX,0xDD1D
017D:E5B1 call near 0xF0FF
017D:E5B4 mov AX,0x4C6F
017D:E5B7 mov CL,4
017D:E5B9 shr AX,CL
017D:E5BB mov CX,DS
017D:E5BD add AX,CX
017D:E5BF mov word ptr DS:[0xDC32],AX
017D:E5C2 mov AH,0x19
017D:E5C4 int 0x21
017D:E5C6 mov byte ptr DS:[0xCE76],AL
017D:E5C9 mov byte ptr DS:[0xCE77],AL
017D:E5CC mov AX,0x3301
017D:E5CF int 0x21
017D:E5D1 mov byte ptr DS:[0x2941],DL
017D:E5D5 mov AX,0x3301
017D:E5D8 xor DX,DX
017D:E5DA int 0x21
017D:E5DC call near 0xE675
017D:E5DF mov AL,byte ptr DS:[0x2942]
017D:E5E2 and AX,1
017D:E5E5 mov SI,0x38B7
017D:E5E8 mov CX,0x002E
017D:E5EB call near 0xE57B
017D:E5EE call far dword ptr DS:[0x38B9]
017D:E5F2 mov word ptr DS:[0xDBD8],AX
017D:E5F5 call near 0xC08E
017D:E5F8 mov word ptr DS:[0xCE74],CX
017D:E5FC mov DI,0xDBDC
017D:E5FF call near 0xF0F6
017D:E602 mov word ptr DS:[0xDBD6],BP
017D:E606 or BP,BP
017D:E608 jne short 0xE610
017D:E60A mov DI,0xDBD4
017D:E60D call near 0xF0F6
017D:E610 call far dword ptr DS:[0x38B5]
017D:E614 mov AL,byte ptr DS:[0x2942]
017D:E617 push AX
017D:E618 shr AL,1
017D:E61A shr AL,1
017D:E61C and AL,7
017D:E61E mov byte ptr DS:[0xCEEB],AL
017D:E621 pop AX
017D:E622 or AL,AL
017D:E624 jns short 0xE62B
017D:E62B test AL,0x40
017D:E62D jne short 0xE632
017D:E62F call near 0xE97A
017D:E632 call near 0xE85C
017D:E635 call near 0xEA7B
017D:E638 mov AL,byte ptr DS:[0x2942]
017D:E63B and AL,2
017D:E63D mov BP,0xCE7A
017D:E640 call far dword ptr DS:[0x3925]
017D:E644 mov word ptr DS:[0xDC48],0x271C
017D:E64A mov byte ptr DS:[0xDC46],0xFF
017D:E64F xor AX,AX
017D:E651 mov BX,0x00C7
017D:E654 xor CX,CX
017D:E656 mov DX,0x013F
017D:E659 call near 0xDB14
017D:E65C mov BX,0x00AB
017D:E65F mov DX,0x00ED
017D:E662 call near 0xDB03
017D:E665 call near 0xE76A
017D:E668 call near 0xCE6C
017D:E66B call near 0xC07C
017D:E66E call near 0xC0AD
017D:E671 jmp near 0xC412
017D:E675 mov DX,0x37F2
017D:E678 call near 0xF1FB
017D:E67B jb short 0xE692
017D:E692 mov SI,0x37F7
017D:E695 inc byte ptr DS:[SI]
017D:E697 cmp byte ptr DS:[SI],0x39
017D:E69A jbe short 0xE675
017D:E69C mov DX,0x37E9
017D:E69F mov AX,0x3D00
017D:E6A2 int 0x21
017D:E6A4 jb short 0xE674
017D:E6A6 mov word ptr DS:[0xDBBA],AX
017D:E6A9 call near 0xE741
017D:E6AC mov SI,DI
017D:E6AE mov BP,ES
017D:E6B0 les DI,word ptr DS:[0x39B7]
017D:E6B4 mov word ptr DS:[0xDBBC],DI
017D:E6B8 mov word ptr DS:[0xDBBE],ES
017D:E6BC mov AX,0x0145
017D:E6BF stos word ptr ES:[DI],AX
017D:E6C0 mov CX,0x014D
017D:E6C3 mov AL,0xFF
017D:E6C5 rep stos byte ptr ES:[DI],AL
017D:E6C7 mov word ptr DS:[0xD820],DI
017D:E6CB push DS
017D:E6CC mov DS,BP
017D:E6CE lods AX,word ptr DS:[SI]
017D:E6CF push SI
017D:E6D0 call near 0xF314
017D:E6D3 pop SI
017D:E6D4 jb short 0xE702
017D:E6D6 call near 0xF3A7
017D:E6D9 je short 0xE6F9
017D:E6DB push AX
017D:E6DC push DX
017D:E6DD push SI
017D:E6DE push DI
017D:E6DF mov CX,word ptr SS:[0xD820]
017D:E6E4 mov SI,CX
017D:E6E6 sub CX,DI
017D:E6E8 sub SI,2
017D:E6EB lea DI,SI+0x0A
017D:E6EE shr CX,1
017D:E6F0 std
017D:E6F1 rep movs word ptr ES:[DI],word ptr ES:[SI]
017D:E6F4 cld
017D:E6F5 pop DI
017D:E6F6 pop SI
017D:E6F7 pop DX
017D:E6F8 pop AX
017D:E6F9 call near 0xE75B
017D:E6FC add word ptr SS:[0xD820],0x000A
017D:E702 add SI,0x0019
017D:E705 cmp byte ptr DS:[SI],0
017D:E708 jne short 0xE6CF
017D:E70A pop DS
017D:E70B mov SI,0x0145
017D:E70E mov AX,word ptr DS:[0xD820]
017D:E711 sub AX,SI
017D:E713 xor DX,DX
017D:E715 mov CX,0x0280
017D:E718 div CX
017D:E71A mov DX,0x000A
017D:E71D mul DX
017D:E71F mov DX,AX
017D:E721 les DI,word ptr SS:[0xDBBC]
017D:E726 add DI,2
017D:E729 add SI,DX
017D:E72B push SI
017D:E72C movs word ptr ES:[DI],word ptr ES:[SI]
017D:E72E movs byte ptr ES:[DI],byte ptr ES:[SI]
017D:E730 pop SI
017D:E731 mov AX,SI
017D:E733 stos word ptr ES:[DI],AX
017D:E734 cmp DI,0x0140
017D:E738 jb short 0xE729
017D:E73A mov CX,word ptr DS:[0xD820]
017D:E73E jmp near 0xF0FF
017D:E741 xor AX,AX
017D:E743 xor DX,DX
017D:E745 call near 0xF2D6
017D:E748 mov AX,word ptr DS:[0x39B9]
017D:E74B add AX,0x0800
017D:E74E mov ES,AX
017D:E750 xor DI,DI
017D:E752 mov CX,0xFFFF
017D:E755 call near 0xF2EA
017D:E758 jb short 0xE741
017D:E75A ret near
017D:E75B push SI
017D:E75C stos word ptr ES:[DI],AX
017D:E75D mov AL,DL
017D:E75F stos byte ptr ES:[DI],AL
017D:E760 add SI,0x0010
017D:E763 movs word ptr ES:[DI],word ptr DS:[SI]
017D:E764 movs byte ptr ES:[DI],byte ptr DS:[SI]
017D:E765 inc SI
017D:E766 movs word ptr ES:[DI],word ptr DS:[SI]
017D:E767 movs word ptr ES:[DI],word ptr DS:[SI]
017D:E768 pop SI
017D:E769 ret near
017D:E76A mov AL,byte ptr DS:[0x2944]
017D:E76D mov CL,4
017D:E76F shr AL,CL
017D:E771 add AL,7
017D:E773 xor AH,AH
017D:E775 mov SI,0x398B
017D:E778 mov CX,8
017D:E77B call near 0xE57B
017D:E77E mov AX,word ptr DS:[0x39B5]
017D:E781 call far dword ptr DS:[0x3989]
017D:E785 mov word ptr DS:[0xDBC8],BX
017D:E789 call near 0xA637
017D:E78C call near 0xAE54
017D:E78F call near 0xE851
017D:E792 jb short 0xE7BC
017D:E7BC mov AX,word ptr DS:[0x3813]
017D:E7BF mov word ptr DS:[0x381B],AX
017D:E7C2 call near 0xA87E
017D:E7C5 mov AL,byte ptr DS:[0x2944]
017D:E7C8 and AX,0x000F
017D:E7CB add AX,2
017D:E7CE mov SI,0x396F
017D:E7D1 mov CX,7
017D:E7D4 call near 0xE57B
017D:E7D7 mov BP,0x3349
017D:E7DA mov CX,0x000A
017D:E7DD mov AX,word ptr DS:[0x39B3]
017D:E7E0 call far dword ptr DS:[0x396D]
017D:E7E4 or word ptr DS:[0xDBC8],BX
017D:E7E8 call near 0xA650
017D:E7EB call near 0xAE3F
017D:E7EE call near 0xE851
017D:E7F1 jb short 0xE818
017D:E818 call near 0xAE28
017D:E81B je short 0xE825
017D:E81D call near 0xE826
017D:E820 and byte ptr DS:[0x2943],0xEF
017D:E825 ret near
017D:E826 cmp word ptr DS:[0xDBBA],0
017D:E82B je short 0xE850
017D:E82D call near 0xE741
017D:E830 push DS
017D:E831 mov SI,DI
017D:E833 push ES
017D:E834 pop DS
017D:E835 lods AX,word ptr DS:[SI]
017D:E836 mov CX,0x00FA
017D:E839 push CX
017D:E83A push SI
017D:E83B call near 0xF314
017D:E83E pop SI
017D:E83F jb short 0xE849
017D:E841 call near 0xF3A7
017D:E844 jne short 0xE849
017D:E846 call near 0xE75B
017D:E849 add SI,0x0019
017D:E84C pop CX
017D:E84D loop 0xE839
017D:E84F pop DS
017D:E850 ret near
017D:E851 mov AX,word ptr DS:[0x39B9]
017D:E854 add AX,0x2F13
017D:E857 cmp AX,word ptr DS:[0xCE68]
017D:E85B ret near
017D:E85C cli
017D:E85D call near 0xE913
017D:E860 xor AX,AX
017D:E862 mov ES,AX
017D:E864 mov DI,0x0020
017D:E867 mov word ptr ES:[DI],0xE8B8
017D:E86C pushf
017D:E86D sti
017D:E86E cmp byte ptr CS:[0xE8D4],0
017D:E874 je short 0xE86E
017D:E876 popf
017D:E877 mov word ptr ES:[DI],0xEF6A
017D:E87C mov AX,word ptr CS:[0xE8D2]
017D:E880 or AH,AH
017D:E882 je short 0xE8A5
017D:E884 or AL,AL
017D:E886 je short 0xE8A5
017D:E888 xor DX,DX
017D:E88A mov CX,0x1745
017D:E88D div CX
017D:E88F shl DX,1
017D:E891 cmp DX,CX
017D:E893 jb short 0xE896
017D:E896 dec AX
017D:E897 jns short 0xE89A
017D:E89A cmp AX,0x000A
017D:E89D jb short 0xE8A1
017D:E8A1 mov byte ptr CS:[0xEFD9],AL
017D:E8A5 mov AX,0x1745
017D:E8A8 pushf
017D:E8A9 push AX
017D:E8AA cli
017D:E8AB mov AL,0x36
017D:E8AD out 0x43,AL
017D:E8AF pop AX
017D:E8B0 out 0x40,AL
017D:E8B2 mov AL,AH
017D:E8B4 out 0x40,AL
017D:E8B6 popf
017D:E8B7 ret near
017D:E8B8 push AX
017D:E8B9 mov AL,0x36
017D:E8BB out 0x43,AL
017D:E8BD in AL,0x40
017D:E8BF mov AH,AL
017D:E8C1 in AL,0x40
017D:E8C3 xchg AH,AL
017D:E8C5 mov word ptr CS:[0xE8D2],AX
017D:E8C9 inc byte ptr CS:[0xE8D4]
017D:E8CE pop AX
017D:E8CF jmp near 0xEF6A
017D:E913 xor byte ptr DS:[0xCE73],0xFF
017D:E918 mov SI,0x2913
017D:E91B pushf
017D:E91C cli
017D:E91D lods AX,word ptr DS:[SI]
017D:E91E mov DI,AX
017D:E920 lods AX,word ptr DS:[SI]
017D:E921 xchg DI,AX
017D:E922 push SI
017D:E923 mov SI,AX
017D:E925 shl SI,1
017D:E927 shl SI,1
017D:E929 xor AX,AX
017D:E92B mov ES,AX
017D:E92D mov AX,word ptr CS:[DI]
017D:E930 xchg AX,word ptr ES:[SI]
017D:E933 mov word ptr CS:[DI],AX
017D:E936 mov AX,word ptr CS:[DI+2]
017D:E93A xchg AX,word ptr ES:[SI+2]
017D:E93E mov word ptr CS:[DI+2],AX
017D:E942 pop SI
017D:E943 lods AX,word ptr DS:[SI]
017D:E944 or AX,AX
017D:E946 jns short 0xE91E
017D:E948 popf
017D:E949 ret near
017D:E97A mov AX,0x3533
017D:E97D int 0x21
017D:E97F mov AX,ES
017D:E981 or AX,BX
017D:E983 je short 0xE9F3
017D:E985 mov AX,0
017D:E988 int 0x33
017D:E98A inc AX
017D:E98B jne short 0xE9F3
017D:E98D xor CX,CX
017D:E98F xor DX,DX
017D:E991 mov AX,4
017D:E994 int 0x33
017D:E996 inc byte ptr DS:[0x2580]
017D:E99A js short 0xE9B3
017D:E99C mov CL,byte ptr DS:[0x2580]
017D:E9A0 mov AX,1
017D:E9A3 shl AX,CL
017D:E9A5 mov CX,AX
017D:E9A7 mov AX,4
017D:E9AA int 0x33
017D:E9AC mov AX,3
017D:E9AF int 0x33
017D:E9B1 jcxz short 0xE996
017D:E9B3 inc byte ptr DS:[0x2581]
017D:E9B7 js short 0xE9D0
017D:E9B9 mov CL,byte ptr DS:[0x2581]
017D:E9BD mov DX,1
017D:E9C0 shl DX,CL
017D:E9C2 mov AX,4
017D:E9C5 int 0x33
017D:E9C7 mov AX,3
017D:E9CA int 0x33
017D:E9CC or DX,DX
017D:E9CE je short 0xE9B3
017D:E9D0 mov AX,0x0010
017D:E9D3 mov DX,AX
017D:E9D5 and word ptr DS:[0x2580],0x7F7F
017D:E9DB mov CX,word ptr DS:[0x2580]
017D:E9DF shr AX,CL
017D:E9E1 mov CL,CH
017D:E9E3 shr DX,CL
017D:E9E5 mov CX,AX
017D:E9E7 mov AX,0x000F
017D:E9EA push DX
017D:E9EB int 0x33
017D:E9ED pop DX
017D:E9EE mov AX,0x0013
017D:E9F1 int 0x33
017D:E9F3 ret near
017D:EA7B test byte ptr DS:[0x2943],0x80
017D:EA80 je short 0xEA85
017D:EA85 test byte ptr DS:[0x2943],0x48
017D:EA8A je short 0xEA8F
017D:EA8F test byte ptr DS:[0x2943],0xE8
017D:EA94 je short 0xEAB6
017D:EAB6 ret near
017D:EF6A push AX
017D:EF6B push DS
017D:EF6C push ES
017D:EF6D mov AX,0x10C8
017D:EF70 mov DS,AX
017D:EF72 cld
017D:EF73 cmp byte ptr DS:[0xCEEA],0
017D:EF78 jg short 0xEFA2
017D:EF7A inc word ptr DS:[0xCE7A]
017D:EF7E jne short 0xEF84
017D:EF84 cmp byte ptr DS:[0x2788],0
017D:EF89 jne short 0xEF9F
017D:EF9F call near 0xEFBA
017D:EFA2 pop ES
017D:EFA3 dec byte ptr DS:[0xCE72]
017D:EFA7 js short 0xEFD5
017D:EFA9 mov AL,0x20
017D:EFAB out 0x20,AL
017D:EFAD cmp byte ptr DS:[0xDBB5],0
017D:EFB2 je short 0xEFB7
017D:EFB7 pop DS
017D:EFB8 pop AX
017D:EFB9 iret
017D:EFBA push BX
017D:EFBB test byte ptr DS:[0x2943],0x10
017D:EFC0 jne short 0xEFD3
017D:EFC2 push CX
017D:EFC3 call far dword ptr DS:[0x3981]
017D:EFC7 mov byte ptr DS:[0xDBCD],AL
017D:EFCA mov word ptr DS:[0xDBCE],BX
017D:EFCE mov word ptr DS:[0xDBD0],CX
017D:EFD2 pop CX
017D:EFD3 pop BX
017D:EFD4 ret near
017D:EFD5 mov byte ptr DS:[0xCE72],byte ptr [0x000107A9]
017D:EFDA pop DS
017D:EFDB pop AX
017D:EFDC jmp far F000:0006
017D:F0A0 push DI
017D:F0A1 push ES
017D:F0A2 inc byte ptr DS:[0xCE71]
017D:F0A6 push DS
017D:F0A7 pop ES
017D:F0A8 mov DI,0x4C60
017D:F0AB call near 0xF0B9
017D:F0AE dec byte ptr DS:[0xCE71]
017D:F0B2 mov SI,DI
017D:F0B4 pop ES
017D:F0B5 pop DI
017D:F0B6 jmp near 0xF403
017D:F0B9 mov word ptr DS:[0xCE78],SI
017D:F0BD shl SI,1
017D:F0BF mov SI,word ptr DS:[SI+0x31FF]
017D:F0C3 lods AX,word ptr DS:[SI]
017D:F0C4 mov DX,SI
017D:F0C6 or AX,AX
017D:F0C8 je short 0xF0D6
017D:F0CA mov CX,AX
017D:F0CC push DX
017D:F0CD call near 0xF11C
017D:F0D0 pop DX
017D:F0D1 call near 0xF0D6
017D:F0D4 jmp short 0xF0FF
017D:F0D6 mov AX,word ptr DS:[0xCE78]
017D:F0D9 cmp AL,byte ptr DS:[0xCE70]
017D:F0DD jae short 0xF0E4
017D:F0E4 call near 0xF244
017D:F0E7 mov AX,word ptr DS:[0xCE78]
017D:F0EA cmp AL,byte ptr DS:[0xCE70]
017D:F0EE jae short 0xF0F3
017D:F0F3 jmp near 0xF3D3
017D:F0F6 les SI,word ptr DS:[0x39B7]
017D:F0FA mov word ptr DS:[DI],SI
017D:F0FC mov word ptr DS:[DI+2],ES
017D:F0FF mov AX,CX
017D:F101 add AX,0x000F
017D:F104 rcr AX,1
017D:F106 shr AX,1
017D:F108 shr AX,1
017D:F10A shr AX,1
017D:F10C add word ptr DS:[0x39B9],AX
017D:F110 push AX
017D:F111 mov AX,word ptr DS:[0x39B9]
017D:F114 cmp AX,word ptr DS:[0xCE68]
017D:F118 pop AX
017D:F119 ja short 0xF131
017D:F11B ret near
017D:F11C les DI,word ptr DS:[0x39B7]
017D:F120 mov AX,ES
017D:F122 add AX,CX
017D:F124 cmp AX,word ptr DS:[0xCE68]
017D:F128 jae short 0xF12B
017D:F12A ret near
017D:F1FB push DX
017D:F1FC call near 0xF2A7
017D:F1FF pop SI
017D:F200 jae short 0xF228
017D:F202 mov DX,SI
017D:F204 push DX
017D:F205 call near 0xF2FC
017D:F208 mov AX,0x3D00
017D:F20B int 0x21
017D:F20D pop DX
017D:F20E jb short 0xF228
017D:F228 ret near
017D:F229 call near 0xF1FB
017D:F22C jb short 0xF22F
017D:F22E ret near
017D:F244 push DX
017D:F245 call near 0xF229
017D:F248 pop DX
017D:F249 cmp BX,word ptr DS:[0xDBBA]
017D:F24D jne short 0xF260
017D:F24F call near 0xF2EA
017D:F252 jb short 0xF244
017D:F254 ret near
017D:F2A7 push DI
017D:F2A8 push ES
017D:F2A9 cmp word ptr DS:[0xDBBA],1
017D:F2AE jb short 0xF2D3
017D:F2B0 mov SI,DX
017D:F2B2 call near 0xF314
017D:F2B5 jb short 0xF2D3
017D:F2B7 call near 0xF3A7
017D:F2BA jb short 0xF2D3
017D:F2BC xor CX,CX
017D:F2BE mov CL,byte ptr ES:[DI+5]
017D:F2C2 mov BP,CX
017D:F2C4 mov CX,word ptr ES:[DI+3]
017D:F2C8 mov AX,word ptr ES:[DI+6]
017D:F2CC mov DX,word ptr ES:[DI+8]
017D:F2D0 call near 0xF2D6
017D:F2D3 pop ES
017D:F2D4 pop DI
017D:F2D5 ret near
017D:F2D6 push CX
017D:F2D7 mov BX,word ptr SS:[0xDBBA]
017D:F2DC mov CX,DX
017D:F2DE mov DX,AX
017D:F2E0 mov AX,0x4200
017D:F2E3 int 0x21
017D:F2E5 pop CX
017D:F2E6 ret near
017D:F2EA push DS
017D:F2EB push ES
017D:F2EC pop DS
017D:F2ED mov BX,word ptr SS:[0xDBBA]
017D:F2F2 mov DX,DI
017D:F2F4 mov AH,0x3F
017D:F2F6 int 0x21
017D:F2F8 cmp AX,CX
017D:F2FA pop DS
017D:F2FB ret near
017D:F2FC push SI
017D:F2FD push DI
017D:F2FE mov SI,DX
017D:F300 mov DI,word ptr DS:[0x38A6]
017D:F304 mov AL,byte ptr DS:[SI]
017D:F306 inc SI
017D:F307 mov byte ptr DS:[DI],AL
017D:F309 inc DI
017D:F30A or AL,AL
017D:F30C jne short 0xF304
017D:F30E pop DI
017D:F30F pop SI
017D:F310 mov DX,0x3826
017D:F313 ret near
017D:F314 push SS
017D:F315 pop ES
017D:F316 cmp word ptr DS:[SI+2],0x505C
017D:F31B je short 0xF36C
017D:F31D push SI
017D:F31E mov CX,0x0010
017D:F321 mov DX,CX
017D:F323 lods AL,byte ptr DS:[SI]
017D:F324 or AL,AL
017D:F326 loopne 0xF323
017D:F328 jne short 0xF32B
017D:F32A inc CX
017D:F32B sub CX,0x0010
017D:F32E neg CX
017D:F330 pop SI
017D:F331 xor DX,DX
017D:F333 mov AX,word ptr DS:[0xCE78]
017D:F336 mov DI,AX
017D:F338 shl DI,1
017D:F33A mov DI,word ptr DS:[DI+0x31FF]
017D:F33E add DI,2
017D:F341 push CX
017D:F342 push SI
017D:F343 repe cmps byte ptr DS:[SI],byte ptr ES:[DI]
017D:F345 pop SI
017D:F346 pop CX
017D:F347 je short 0xF3A5
017D:F349 mov BX,0x31FF
017D:F34C mov BP,0x00F7
017D:F34F mov DI,word ptr ES:[BX]
017D:F352 mov AX,BX
017D:F354 sub AX,0x31FF
017D:F357 shr AX,1
017D:F359 add BX,2
017D:F35C add DI,2
017D:F35F push CX
017D:F360 push SI
017D:F361 repe cmps byte ptr DS:[SI],byte ptr ES:[DI]
017D:F363 pop SI
017D:F364 pop CX
017D:F365 je short 0xF3A5
017D:F367 dec BP
017D:F368 jne short 0xF34F
017D:F36A stc
017D:F36B ret near
017D:F36C add SI,4
017D:F36F lods AL,byte ptr DS:[SI]
017D:F370 sub AL,0x40
017D:F372 mov DL,AL
017D:F374 xor BX,BX
017D:F376 mov CX,3
017D:F379 lods AL,byte ptr DS:[SI]
017D:F37A cmp AL,0x41
017D:F37C jb short 0xF380
017D:F37E sub AL,7
017D:F380 and AL,0x0F
017D:F382 shl BX,1
017D:F384 shl BX,1
017D:F386 shl BX,1
017D:F388 shl BX,1
017D:F38A or BL,AL
017D:F38C loop 0xF379
017D:F38E lods AL,byte ptr DS:[SI]
017D:F38F cmp AL,0x4F
017D:F391 cmc
017D:F392 rcl DL,1
017D:F394 lods AL,byte ptr DS:[SI]
017D:F395 sub AL,0x41
017D:F397 jb short 0xF3A3
017D:F399 shl AL,1
017D:F39B shl AL,1
017D:F39D shl AL,1
017D:F39F shl AL,1
017D:F3A1 or BH,AL
017D:F3A3 mov AX,BX
017D:F3A5 clc
017D:F3A6 ret near
017D:F3A7 les DI,word ptr SS:[0xDBBC]
017D:F3AC sub DI,5
017D:F3AF add DI,5
017D:F3B2 cmp DL,byte ptr ES:[DI+4]
017D:F3B6 jne short 0xF3BC
017D:F3B8 cmp AX,word ptr ES:[DI+2]
017D:F3BC ja short 0xF3AF
017D:F3BE mov DI,word ptr ES:[DI]
017D:F3C1 sub DI,0x000A
017D:F3C4 add DI,0x000A
017D:F3C7 cmp DL,byte ptr ES:[DI+2]
017D:F3CB jne short 0xF3D0
017D:F3CD cmp AX,word ptr ES:[DI]
017D:F3D0 ja short 0xF3C4
017D:F3D2 ret near
017D:F3D3 cmp byte ptr DS:[0xCE71],0
017D:F3D8 jne short 0xF402
017D:F3DA push CX
017D:F3DB push DI
017D:F3DC push DS
017D:F3DD push ES
017D:F3DE pop DS
017D:F3DF mov DX,DI
017D:F3E1 add DX,CX
017D:F3E3 mov CX,6
017D:F3E6 mov SI,DI
017D:F3E8 xor AX,AX
017D:F3EA lods AL,byte ptr DS:[SI]
017D:F3EB add AH,AL
017D:F3ED loop 0xF3EA
017D:F3EF cmp AH,0xAB
017D:F3F2 jne short 0xF3FE
017D:F3F4 mov SI,DI
017D:F3F6 lods AX,word ptr DS:[SI]
017D:F3F7 mov DI,AX
017D:F3F9 lods AL,byte ptr DS:[SI]
017D:F3FA or AL,AL
017D:F3FC je short 0xF40D
017D:F3FE stc
017D:F3FF pop DS
017D:F400 pop DI
017D:F401 pop CX
017D:F402 ret near
017D:F403 push CX
017D:F404 push DI
017D:F405 push DS
017D:F406 add SI,6
017D:F409 xor BP,BP
017D:F40B jmp short 0xF435
017D:F40D lods AX,word ptr DS:[SI]
017D:F40E mov CX,AX
017D:F410 sub SI,5
017D:F413 mov BP,SI
017D:F415 add DI,SI
017D:F417 add DI,0x0040
017D:F41A add SI,CX
017D:F41C dec SI
017D:F41D dec DI
017D:F41E sub CX,6
017D:F421 std
017D:F422 shr CX,1
017D:F424 jae short 0xF427
017D:F426 movs byte ptr ES:[DI],byte ptr DS:[SI]
017D:F427 dec SI
017D:F428 dec DI
017D:F429 rep movs word ptr ES:[DI],word ptr DS:[SI]
017D:F42B cld
017D:F42C mov SI,DI
017D:F42E add SI,2
017D:F431 mov DI,BP
017D:F433 xor BP,BP
017D:F435 shr BP,1
017D:F437 je short 0xF43E
017D:F439 jae short 0xF446
017D:F43B movs byte ptr ES:[DI],byte ptr DS:[SI]
017D:F43C jmp short 0xF435
017D:F43E lods AX,word ptr DS:[SI]
017D:F43F mov BP,AX
017D:F441 stc
017D:F442 rcr BP,1
017D:F444 jb short 0xF43B
017D:F446 xor CX,CX
017D:F448 shr BP,1
017D:F44A jne short 0xF452
017D:F44C lods AX,word ptr DS:[SI]
017D:F44D mov BP,AX
017D:F44F stc
017D:F450 rcr BP,1
017D:F452 jb short 0xF482
017D:F454 shr BP,1
017D:F456 jne short 0xF45E
017D:F458 lods AX,word ptr DS:[SI]
017D:F459 mov BP,AX
017D:F45B stc
017D:F45C rcr BP,1
017D:F45E rcl CX,1
017D:F460 shr BP,1
017D:F462 jne short 0xF46A
017D:F464 lods AX,word ptr DS:[SI]
017D:F465 mov BP,AX
017D:F467 stc
017D:F468 rcr BP,1
017D:F46A rcl CX,1
017D:F46C lods AL,byte ptr DS:[SI]
017D:F46D mov AH,0xFF
017D:F46F add AX,DI
017D:F471 xchg SI,AX
017D:F472 mov BX,DS
017D:F474 mov DX,ES
017D:F476 mov DS,DX
017D:F478 inc CX
017D:F479 inc CX
017D:F47A rep movs byte ptr ES:[DI],byte ptr DS:[SI]
017D:F47C mov DS,BX
017D:F47E mov SI,AX
017D:F480 jmp short 0xF435
017D:F482 lods AX,word ptr DS:[SI]
017D:F483 mov CL,AL
017D:F485 shr AX,1
017D:F487 shr AX,1
017D:F489 shr AX,1
017D:F48B or AH,0xE0
017D:F48E and CL,7
017D:F491 jne short 0xF46F
017D:F493 mov BX,AX
017D:F495 lods AL,byte ptr DS:[SI]
017D:F496 mov CL,AL
017D:F498 mov AX,BX
017D:F49A or CL,CL
017D:F49C jne short 0xF46F
017D:F49E stc
017D:F49F mov CX,DI
017D:F4A1 pop DS
017D:F4A2 pop DI
017D:F4A3 add SP,2
017D:F4A6 sub CX,DI
017D:F4A8 ret near
24C8:0100 jmp near 0x0967
24C8:0103 jmp near 0x09D9
24C8:0106 jmp near 0x09E2
24C8:0118 jmp near 0x19F7
24C8:0121 jmp near 0x1B7C
24C8:012D jmp near 0x1B7C
24C8:013C ret far
24C8:0151 jmp near 0x25E7
24C8:0154 jmp near 0x0975
24C8:0160 jmp near 0x0B0C
24C8:0163 jmp near 0x0C06
24C8:017B jmp near 0x0A68
24C8:0967 mov AH,0x0F
24C8:0969 int 0x10
24C8:096B cmp AL,0x13
24C8:096D je short 0x0974
24C8:096F mov AX,0x0013
24C8:0972 int 0x10
24C8:0974 ret far
24C8:0975 mov byte ptr CS:[0x01BD],AL
24C8:0979 pushf
24C8:097A sti
24C8:097B mov AX,0x0040
24C8:097E mov ES,AX
24C8:0980 mov DX,word ptr ES:[0x0063]
24C8:0985 add DL,6
24C8:0988 mov word ptr CS:[0x019F],DX
24C8:098D in DX,AL
24C8:098E and AL,8
24C8:0990 call near 0x09B8
24C8:0993 jae short 0x09B4
24C8:0995 call near 0x09B8
24C8:0998 jae short 0x09B4
24C8:099A mov DI,SI
24C8:099C mov byte ptr CS:[0x01A2],AH
24C8:09A1 call near 0x09B8
24C8:09A4 jae short 0x09B4
24C8:09A6 cmp SI,DI
24C8:09A8 not byte ptr CS:[0x01A1]
24C8:09AD jae short 0x09B4
24C8:09B4 popf
24C8:09B5 jmp near 0x0B0C
24C8:09B8 mov AH,AL
24C8:09BA xor SI,SI
24C8:09BC mov BX,word ptr SS:[BP]
24C8:09BF inc SI
24C8:09C0 jne short 0x09C3
24C8:09C3 in DX,AL
24C8:09C4 and AL,8
24C8:09C6 cmp AL,AH
24C8:09C8 jne short 0x09D7
24C8:09CA push AX
24C8:09CB mov AX,word ptr SS:[BP]
24C8:09CE sub AX,BX
24C8:09D0 cmp AX,0x0064
24C8:09D3 pop AX
24C8:09D4 jb short 0x09BF
24C8:09D7 stc
24C8:09D8 ret near
24C8:09D9 mov AX,0xA000
24C8:09DC mov CX,0xFA00
24C8:09DF xor BP,BP
24C8:09E1 ret far
24C8:09E2 push AX
24C8:09E3 push BX
24C8:09E4 push CX
24C8:09E5 push SI
24C8:09E6 push DI
24C8:09E7 push DS
24C8:09E8 push ES
24C8:09E9 push ES
24C8:09EA pop DS
24C8:09EB push CS
24C8:09EC pop ES
24C8:09ED mov DI,0x05BF
24C8:09F0 add DI,BX
24C8:09F2 mov AX,CX
24C8:09F4 mov SI,DX
24C8:09F6 repe cmps byte ptr DS:[SI],byte ptr ES:[DI]
24C8:09F8 je short 0x0A19
24C8:09FA mov byte ptr CS:[0x01BE],1
24C8:0A00 mov DI,0x05BF
24C8:0A03 add DI,BX
24C8:0A05 mov SI,DX
24C8:0A07 mov CX,AX
24C8:0A09 push CX
24C8:0A0A rep movs byte ptr ES:[DI],byte ptr DS:[SI]
24C8:0A0C pop CX
24C8:0A0D call near 0x0A21
24C8:0A10 mov DI,0x01BF
24C8:0A13 add DI,BX
24C8:0A15 mov AL,1
24C8:0A17 rep stos byte ptr ES:[DI],AL
24C8:0A19 pop ES
24C8:0A1A pop DS
24C8:0A1B pop DI
24C8:0A1C pop SI
24C8:0A1D pop CX
24C8:0A1E pop BX
24C8:0A1F pop AX
24C8:0A20 ret far
24C8:0A21 push DX
24C8:0A22 mov AX,BX
24C8:0A24 mov DL,3
24C8:0A26 div DL
24C8:0A28 xor AH,AH
24C8:0A2A mov BX,AX
24C8:0A2C mov AX,CX
24C8:0A2E cmp AX,0x0300
24C8:0A31 jae short 0x0A3B
24C8:0A33 div DL
24C8:0A35 xor AH,AH
24C8:0A37 mov CX,AX
24C8:0A39 pop DX
24C8:0A3A ret near
24C8:0A58 push CS
24C8:0A59 push CS
24C8:0A5A pop DS
24C8:0A5B pop ES
24C8:0A5C mov SI,0x05BF
24C8:0A5F mov DI,0x02BF
24C8:0A62 mov CX,0x0180
24C8:0A65 rep movs word ptr ES:[DI],word ptr DS:[SI]
24C8:0A67 ret near
24C8:0A68 push CX
24C8:0A69 push SI
24C8:0A6A push DI
24C8:0A6B push DS
24C8:0A6C push ES
24C8:0A6D call near 0x0A58
24C8:0A70 pop ES
24C8:0A71 pop DS
24C8:0A72 pop DI
24C8:0A73 pop SI
24C8:0A74 pop CX
24C8:0A75 ret far
24C8:0B0C cmp byte ptr CS:[0x01BE],0
24C8:0B12 je short 0x0B67
24C8:0B14 mov byte ptr CS:[0x01BE],0
24C8:0B1A push AX
24C8:0B1B push BX
24C8:0B1C push CX
24C8:0B1D push DX
24C8:0B1E push SI
24C8:0B1F push DI
24C8:0B20 push BP
24C8:0B21 push ES
24C8:0B22 push CS
24C8:0B23 pop ES
24C8:0B24 mov DI,0x01BF
24C8:0B27 mov CX,0x0100
24C8:0B2A xor AL,AL
24C8:0B2C repe scas AL,byte ptr ES:[DI]
24C8:0B2E je short 0x0B55
24C8:0B30 dec DI
24C8:0B31 inc CX
24C8:0B32 mov BX,CX
24C8:0B34 repne scas AL,byte ptr ES:[DI]
24C8:0B36 push CX
24C8:0B37 jne short 0x0B3A
24C8:0B39 inc CX
24C8:0B3A sub CX,BX
24C8:0B3C neg CX
24C8:0B3E mov DX,0x0100
24C8:0B41 sub DX,BX
24C8:0B43 mov BX,DX
24C8:0B45 add DX,DX
24C8:0B47 add DX,BX
24C8:0B49 add DX,0x05BF
24C8:0B4D call near 0x0B68
24C8:0B50 pop CX
24C8:0B51 or CX,CX
24C8:0B53 jne short 0x0B2A
24C8:0B55 mov DI,0x01BF
24C8:0B58 mov CX,0x0080
24C8:0B5B xor AX,AX
24C8:0B5D rep stos word ptr ES:[DI],AX
24C8:0B5F pop ES
24C8:0B60 pop BP
24C8:0B61 pop DI
24C8:0B62 pop SI
24C8:0B63 pop DX
24C8:0B64 pop CX
24C8:0B65 pop BX
24C8:0B66 pop AX
24C8:0B67 ret far
24C8:0B68 push SI
24C8:0B69 push DS
24C8:0B6A push ES
24C8:0B6B pop DS
24C8:0B6C mov SI,DX
24C8:0B6E pushf
24C8:0B6F cmp byte ptr DS:[0x01A1],0
24C8:0B74 je short 0x0B83
24C8:0B76 mov DX,word ptr DS:[0x019F]
24C8:0B7A in DX,AL
24C8:0B7B and AL,8
24C8:0B7D cmp AL,byte ptr DS:[0x01A2]
24C8:0B81 jne short 0x0B7A
24C8:0B83 cli
24C8:0B84 mov DX,0x03C8
24C8:0B87 mov AL,BL
24C8:0B89 out DX,AL
24C8:0B8A jmp short 0x0B8C
24C8:0B8C jmp short 0x0B8E
24C8:0B8E jmp short 0x0B90
24C8:0B90 jmp short 0x0B92
24C8:0B92 inc DX
24C8:0B93 cmp byte ptr CS:[0x01BD],0
24C8:0B99 jne short 0x0BA9
24C8:0B9B mov AX,CX
24C8:0B9D add CX,CX
24C8:0B9F add CX,AX
24C8:0BA1 lods AL,byte ptr DS:[SI]
24C8:0BA2 out DX,AL
24C8:0BA3 loop 0x0BA1
24C8:0BA5 popf
24C8:0BA6 pop DS
24C8:0BA7 pop SI
24C8:0BA8 ret near
24C8:0C06 mov DX,0x0140
24C8:0C09 mul DX
24C8:0C0B mov word ptr CS:[0x01A3],AX
24C8:0C0F ret far
24C8:19F7 push AX
24C8:19F8 push CX
24C8:19F9 push DI
24C8:19FA xor DI,DI
24C8:19FC xor AX,AX
24C8:19FE mov CX,0x7D00
24C8:1A01 rep stos word ptr ES:[DI],AX
24C8:1A03 pop DI
24C8:1A04 pop CX
24C8:1A05 pop AX
24C8:1A06 ret far
24C8:1B7C push CX
24C8:1B7D push SI
24C8:1B7E push DI
24C8:1B7F xor SI,SI
24C8:1B81 mov DI,SI
24C8:1B83 mov CX,0x7D00
24C8:1B86 rep movs word ptr ES:[DI],word ptr DS:[SI]
24C8:1B88 pop DI
24C8:1B89 pop SI
24C8:1B8A pop CX
24C8:1B8B ret far
24C8:25E7 mov word ptr CS:[0x2768],8
24C8:25EE mov word ptr CS:[0x276A],1
24C8:25F5 mov word ptr CS:[0x2535],SI
24C8:25FA mov word ptr CS:[0x2537],DS
24C8:25FF mov word ptr CS:[0x2539],ES
24C8:2604 mov CX,0x0098
24C8:2607 and AX,0x00FE
24C8:260A cmp AX,0x003E
24C8:260D jb short 0x2614
24C8:2614 mov BX,AX
24C8:2616 jmp near word ptr CS:[BX+0x25A9]
24C8:261D cmp byte ptr DS:[0x01A1],0
24C8:2622 jne short 0x2627
24C8:2627 ret near
24C8:264D mov DS,word ptr CS:[0x2537]
24C8:2652 mov ES,word ptr CS:[0x2539]
24C8:2657 push CS
24C8:2658 call near 0x1B7C
24C8:265B push CS
24C8:265C push CS
24C8:265D pop DS
24C8:265E pop ES
24C8:265F xor BX,BX
24C8:2661 push BX
24C8:2662 push CX
24C8:2663 push DX
24C8:2664 push word ptr SS:[BP]
24C8:2667 mov DI,0x05BF
24C8:266A add DI,BX
24C8:266C lea SI,DI-768
24C8:2670 push BX
24C8:2671 push CX
24C8:2672 push DI
24C8:2673 lods AL,byte ptr DS:[SI]
24C8:2674 sub AL,byte ptr DS:[DI]
24C8:2676 je short 0x2690
24C8:2678 mov BL,AL
24C8:267A xor AH,AH
24C8:267C div DH
24C8:267E xchg AL,AH
24C8:2680 inc AH
24C8:2682 or AL,AL
24C8:2684 jne short 0x268A
24C8:2686 dec AH
24C8:2688 mov AL,DH
24C8:268A cmp AH,DL
24C8:268C jb short 0x2690
24C8:268E add byte ptr DS:[DI],AL
24C8:2690 inc DI
24C8:2691 loop 0x2673
24C8:2693 pop DX
24C8:2694 pop CX
24C8:2695 pop BX
24C8:2696 call near 0x0A21
24C8:2699 call near 0x0B68
24C8:269C pop BX
24C8:269D call near 0x261D
24C8:26A0 pop DX
24C8:26A1 pop CX
24C8:26A2 pop BX
24C8:26A3 add BX,CX
24C8:26A5 cmp BX,0x02FD
24C8:26A9 jb short 0x2661
24C8:26AB dec DL
24C8:26AD jne short 0x265F
24C8:26AF ret far
24C8:26E3 mov word ptr CS:[0x261B],AX
24C8:26E7 xor BX,BX
24C8:26E9 push BX
24C8:26EA push DX
24C8:26EB push word ptr SS:[BP]
24C8:26EE mov SI,0x05BF
24C8:26F1 add SI,BX
24C8:26F3 add SI,BX
24C8:26F5 add SI,BX
24C8:26F7 mov DI,SI
24C8:26F9 mov AX,word ptr CS:[0x261B]
24C8:26FD push AX
24C8:26FE mov CX,AX
24C8:2700 add CX,CX
24C8:2702 add CX,AX
24C8:2704 mov AL,byte ptr DS:[SI]
24C8:2706 sub AL,DH
24C8:2708 jns short 0x270C
24C8:270A xor AL,AL
24C8:270C mov byte ptr DS:[SI],AL
24C8:270E inc SI
24C8:270F loop 0x2704
24C8:2711 pop CX
24C8:2712 mov DX,DI
24C8:2714 call near 0x0B68
24C8:2717 pop BX
24C8:2718 call near 0x261D
24C8:271B pop DX
24C8:271C pop BX
24C8:271D mov AX,word ptr CS:[0x261B]
24C8:2721 add BX,AX
24C8:2723 cmp BX,0x00FF
24C8:2727 jb short 0x26E9
24C8:2729 dec DL
24C8:272B jne short 0x26E7
24C8:272D ret near
24C8:272E push CS
24C8:272F push CS
24C8:2730 pop DS
24C8:2731 pop ES
24C8:2732 mov SI,0x05C2
24C8:2735 mov DI,0x02C2
24C8:2738 mov CX,0x017D
24C8:273B mov AX,word ptr DS:[DI]
24C8:273D xchg AX,word ptr DS:[SI]
24C8:273F stos word ptr ES:[DI],AX
24C8:2740 add SI,2
24C8:2743 loop 0x273B
24C8:2745 mov AX,0x0055
24C8:2748 mov DX,0x0316
24C8:274B call near 0x26E3
24C8:274E mov CX,0x00FF
24C8:2751 mov DX,0x0316
24C8:2754 jmp near 0x264D
47B2:0100 jmp short 0x0120
47B2:0106 jmp near 0x01DE
47B2:0109 jmp near 0x01C2
47B2:010C jmp near 0x01CB
47B2:0115 jmp near 0x01A7
47B2:0120 or AX,AX
47B2:0122 je short 0x0144
47B2:0124 push AX
47B2:0125 and AL,0x0F
47B2:0127 mov BX,2
47B2:012A call near 0x082B
47B2:012D pop AX
47B2:012E shr AX,1
47B2:0130 shr AX,1
47B2:0132 shr AX,1
47B2:0134 shr AX,1
47B2:0136 push AX
47B2:0137 mov BX,1
47B2:013A call near 0x082B
47B2:013D pop AX
47B2:013E mov BX,0x000D
47B2:0141 call near 0x082B
47B2:0144 mov BX,3
47B2:0147 call near 0x082B
47B2:014A mov word ptr CS:[0x011C],0x0100
47B2:0151 mov word ptr CS:[0x011E],CS
47B2:0156 mov word ptr CS:[0x0118],0x0100
47B2:015D mov word ptr CS:[0x011A],CS
47B2:0162 push CS
47B2:0163 call near 0x01C2
47B2:0166 mov BX,0x000F
47B2:0169 ret far
47B2:016A push BX
47B2:016B push DX
47B2:016C shr AL,1
47B2:016E shr AL,1
47B2:0170 shr AL,1
47B2:0172 mov DX,AX
47B2:0174 mov BX,0xF078
47B2:0177 cmp AH,BL
47B2:0179 jbe short 0x017D
47B2:017D xor AL,AL
47B2:017F div BH
47B2:0181 mul DL
47B2:0183 xchg AH,DH
47B2:0185 sub AH,BH
47B2:0187 neg AH
47B2:0189 cmp AH,BL
47B2:018B jbe short 0x018F
47B2:018D mov AH,BL
47B2:018F xor AL,AL
47B2:0191 div BH
47B2:0193 mul DL
47B2:0195 shr AX,1
47B2:0197 shr AX,1
47B2:0199 shr AX,1
47B2:019B shr AX,1
47B2:019D mov AH,DH
47B2:019F and AX,0x0FF0
47B2:01A2 or AL,AH
47B2:01A4 pop DX
47B2:01A5 pop BX
47B2:01A6 ret near
47B2:01A7 call near 0x016A
47B2:01AA mov AH,4
47B2:01AC call near 0x01B0
47B2:01AF ret far
47B2:01B0 push DX
47B2:01B1 mov DX,word ptr CS:[0x0285]
47B2:01B6 add DL,4
47B2:01B9 xchg AL,AH
47B2:01BB out DX,AL
47B2:01BC inc DX
47B2:01BD xchg AL,AH
47B2:01BF out DX,AL
47B2:01C0 pop DX
47B2:01C1 ret near
47B2:01C2 push BX
47B2:01C3 mov BX,8
47B2:01C6 call near 0x082B
47B2:01C9 pop BX
47B2:01CA ret far
47B2:01CB push AX
47B2:01CC xor AX,AX
47B2:01CE push BX
47B2:01CF mov BX,0x000C
47B2:01D2 call near 0x082B
47B2:01D5 pop BX
47B2:01D6 pop AX
47B2:01D7 ret far
47B2:01DE push ES
47B2:01DF mov word ptr CS:[0x0118],SI
47B2:01E4 mov word ptr CS:[0x011A],DS
47B2:01E9 les DI,word ptr DS:[SI]
47B2:01EB mov BX,word ptr DS:[SI+4]
47B2:01EE or byte ptr DS:[SI+6],3
47B2:01F2 mov byte ptr ES:[BX+DI],0
47B2:01F6 sub BX,4
47B2:01F9 cmp byte ptr ES:[DI+3],0
47B2:01FE jne short 0x0206
47B2:0200 cmp word ptr ES:[DI+1],BX
47B2:0204 jb short 0x020F
47B2:020F mov BX,6
47B2:0212 call near 0x082B
47B2:0215 pop ES
47B2:0216 ret far
47B2:02FF push CX
47B2:0300 mov CX,0x0200
47B2:0303 mov AH,AL
47B2:0305 in DX,AL
47B2:0306 or AL,AL
47B2:0308 jns short 0x030F
47B2:030F mov AL,AH
47B2:0311 out DX,AL
47B2:0312 clc
47B2:0313 pop CX
47B2:0314 ret near
47B2:0315 push CX
47B2:0316 push DX
47B2:0317 mov DX,word ptr DS:[0x0285]
47B2:031B add DL,0x0E
47B2:031E mov CX,0x0200
47B2:0321 in DX,AL
47B2:0322 or AL,AL
47B2:0324 js short 0x032B
47B2:0326 loop 0x0321
47B2:032B sub DL,4
47B2:032E in DX,AL
47B2:032F clc
47B2:0330 pop DX
47B2:0331 pop CX
47B2:0332 ret near
47B2:0333 mov DX,word ptr DS:[0x0285]
47B2:0337 add DL,0x0C
47B2:033A mov AH,AL
47B2:033C mov AL,0xF0
47B2:033E in DX,AL
47B2:033F or AL,AL
47B2:0341 js short 0x033E
47B2:0343 mov AL,AH
47B2:0345 out DX,AL
47B2:0346 ret near
47B2:0347 push DX
47B2:0348 mov DX,word ptr DS:[0x0285]
47B2:034C add DL,0x0E
47B2:034F xor AL,AL
47B2:0351 in DX,AL
47B2:0352 or AL,AL
47B2:0354 jns short 0x0351
47B2:0356 sub DL,4
47B2:0359 in DX,AL
47B2:035A pop DX
47B2:035B ret near
47B2:035C mov DX,word ptr DS:[0x0285]
47B2:0360 add DL,6
47B2:0363 mov AL,1
47B2:0365 out DX,AL
47B2:0366 in DX,AL
47B2:0367 in DX,AL
47B2:0368 in DX,AL
47B2:0369 in DX,AL
47B2:036A xor AL,AL
47B2:036C out DX,AL
47B2:036D mov BL,0x10
47B2:036F call near 0x0315
47B2:0372 cmp AL,0xAA
47B2:0374 je short 0x0380
47B2:0380 xor AX,AX
47B2:0382 or AX,AX
47B2:0384 ret near
47B2:0385 mov BX,2
47B2:0388 mov AL,0xE0
47B2:038A mov DX,word ptr DS:[0x0285]
47B2:038E add DL,0x0C
47B2:0391 call near 0x02FF
47B2:0394 jb short 0x03A8
47B2:0396 mov AL,0xAA
47B2:0398 call near 0x02FF
47B2:039B jb short 0x03A8
47B2:039D call near 0x0315
47B2:03A0 jb short 0x03A8
47B2:03A2 cmp AL,0x55
47B2:03A4 jne short 0x03A8
47B2:03A6 xor BX,BX
47B2:03A8 mov AX,BX
47B2:03AA or AX,AX
47B2:03AC ret near
47B2:03AD push AX
47B2:03AE push DX
47B2:03AF mov DX,word ptr CS:[0x0285]
47B2:03B4 add DL,0x0E
47B2:03B7 in DX,AL
47B2:03B8 mov byte ptr CS:[0x028E],1
47B2:03BE mov AL,0x20
47B2:03C0 cmp byte ptr CS:[0x0287],8
47B2:03C6 jb short 0x03CC
47B2:03CC out 0x20,AL
47B2:03CE pop DX
47B2:03CF pop AX
47B2:03D0 iret
47B2:03D1 mov byte ptr DS:[0x028E],0
47B2:03D6 mov AX,0x03AD
47B2:03D9 call near 0x04D8
47B2:03DC mov DX,CS
47B2:03DE mov AX,0x0291
47B2:03E1 call near 0x048A
47B2:03E4 xor CX,CX
47B2:03E6 mov DH,0x48
47B2:03E8 call near 0x0450
47B2:03EB mov AL,0x40
47B2:03ED call near 0x0333
47B2:03F0 mov AL,0x64
47B2:03F2 call near 0x033A
47B2:03F5 mov AL,0x14
47B2:03F7 call near 0x033A
47B2:03FA xor AL,AL
47B2:03FC call near 0x033A
47B2:03FF xor AL,AL
47B2:0401 call near 0x033A
47B2:0404 xor AX,AX
47B2:0406 mov CX,0xFFFF
47B2:0409 cmp byte ptr DS:[0x028E],0
47B2:040E jne short 0x0414
47B2:0410 loop 0x0409
47B2:0414 push AX
47B2:0415 call near 0x0524
47B2:0418 pop AX
47B2:0419 or AX,AX
47B2:041B ret near
47B2:041C mov AL,0xE1
47B2:041E call near 0x033A
47B2:0421 call near 0x0347
47B2:0424 mov AH,AL
47B2:0426 call near 0x0347
47B2:0429 cmp AX,0x0103
47B2:042C mov AX,0
47B2:042F adc AL,AH
47B2:0431 ret near
47B2:0450 push BX
47B2:0451 mov BX,AX
47B2:0453 mov AH,DL
47B2:0455 mov AL,byte ptr CS:[0x0288]
47B2:0459 mov DL,AL
47B2:045B or AL,4
47B2:045D out 0x0A,AL
47B2:045F xor AL,AL
47B2:0461 out 0x0C,AL
47B2:0463 mov AL,DH
47B2:0465 or AL,DL
47B2:0467 out 0x0B,AL
47B2:0469 xor DH,DH
47B2:046B shl DX,1
47B2:046D mov AL,BL
47B2:046F out DX,AL
47B2:0470 mov AL,BH
47B2:0472 out DX,AL
47B2:0473 mov AL,CL
47B2:0475 inc DX
47B2:0476 out DX,AL
47B2:0477 mov AL,CH
47B2:0479 out DX,AL
47B2:047A mov DL,byte ptr CS:[0x0289]
47B2:047F mov AL,AH
47B2:0481 out DX,AL
47B2:0482 mov AL,byte ptr CS:[0x0288]
47B2:0486 out 0x0A,AL
47B2:0488 pop BX
47B2:0489 ret near
47B2:048A push CX
47B2:048B mov CL,4
47B2:048D rol DX,CL
47B2:048F mov CX,DX
47B2:0491 and DX,0x000F
47B2:0494 and CX,-16
47B2:0497 add AX,CX
47B2:0499 adc DX,0
47B2:049C pop CX
47B2:049D ret near
47B2:049E mov DX,word ptr DS:[0x0295]
47B2:04A2 add AX,word ptr DS:[0x0293]
47B2:04A6 jae short 0x04AB
47B2:04AB ret near
47B2:04AC push ES
47B2:04AD push DI
47B2:04AE les DI,word ptr DS:[0x0293]
47B2:04B2 mov AX,word ptr ES:[DI+1]
47B2:04B6 xor DX,DX
47B2:04B8 mov DL,byte ptr ES:[DI+3]
47B2:04BC pop DI
47B2:04BD pop ES
47B2:04BE ret near
47B2:04BF mov AL,byte ptr DS:[0x0287]
47B2:04C2 add AL,8
47B2:04C4 cmp AL,0x10
47B2:04C6 jb short 0x04CA
47B2:04CA xor AH,AH
47B2:04CC shl AX,1
47B2:04CE shl AX,1
47B2:04D0 mov BX,AX
47B2:04D2 xor AX,AX
47B2:04D4 mov ES,AX
47B2:04D6 cli
47B2:04D7 ret near
47B2:04D8 pushf
47B2:04D9 push BX
47B2:04DA push CX
47B2:04DB push DX
47B2:04DC mov DX,AX
47B2:04DE push ES
47B2:04DF call near 0x04BF
47B2:04E2 mov AX,CS
47B2:04E4 xchg DX,word ptr ES:[BX]
47B2:04E7 mov word ptr DS:[0x0297],DX
47B2:04EB xchg AX,word ptr ES:[BX+2]
47B2:04EF mov word ptr DS:[0x0299],AX
47B2:04F2 pop ES
47B2:04F3 mov CL,byte ptr DS:[0x0287]
47B2:04F7 cmp CL,8
47B2:04FA jb short 0x0510
47B2:0510 mov AH,1
47B2:0512 shl AH,CL
47B2:0514 not AH
47B2:0516 in AL,0x21
47B2:0518 mov byte ptr DS:[0x029B],AL
47B2:051B and AL,AH
47B2:051D out 0x21,AL
47B2:051F pop DX
47B2:0520 pop CX
47B2:0521 pop BX
47B2:0522 popf
47B2:0523 ret near
47B2:0524 pushf
47B2:0525 push BX
47B2:0526 push ES
47B2:0527 call near 0x04BF
47B2:052A mov AX,word ptr DS:[0x0297]
47B2:052D mov word ptr ES:[BX],AX
47B2:0530 mov AX,word ptr DS:[0x0299]
47B2:0533 mov word ptr ES:[BX+2],AX
47B2:0537 pop ES
47B2:0538 cmp byte ptr DS:[0x0287],8
47B2:053D jb short 0x0544
47B2:0544 mov AL,byte ptr DS:[0x029B]
47B2:0547 out 0x21,AL
47B2:0549 pop BX
47B2:054A popf
47B2:054B ret near
47B2:054C push DS
47B2:054D push SI
47B2:054E lds SI,word ptr DS:[0x0293]
47B2:0552 lods AL,byte ptr DS:[SI]
47B2:0553 pop SI
47B2:0554 pop DS
47B2:0555 ret near
47B2:0556 mov CX,AX
47B2:0558 call near 0x049E
47B2:055B call near 0x048A
47B2:055E mov byte ptr DS:[0x029D],DL
47B2:0562 mov word ptr DS:[0x029E],AX
47B2:0565 call near 0x04AC
47B2:0568 sub CX,4
47B2:056B sub AX,CX
47B2:056D sbb DX,0
47B2:0570 mov word ptr DS:[0x02A0],AX
47B2:0573 mov word ptr DS:[0x02A2],DX
47B2:0577 sub AX,1
47B2:057A sbb DX,0
47B2:057D add AX,word ptr DS:[0x029E]
47B2:0581 adc DL,byte ptr DS:[0x029D]
47B2:0585 mov word ptr DS:[0x02A4],AX
47B2:0588 sub DL,byte ptr DS:[0x029D]
47B2:058C mov byte ptr DS:[0x02A6],DL
47B2:0590 ret near
47B2:0591 push DS
47B2:0592 push ES
47B2:0593 push AX
47B2:0594 push BX
47B2:0595 push CX
47B2:0596 push DX
47B2:0597 push SI
47B2:0598 push DI
47B2:0599 push BP
47B2:059A cld
47B2:059B mov AX,CS
47B2:059D mov DS,AX
47B2:059F mov ES,AX
47B2:05A1 mov AL,0x20
47B2:05A3 cmp byte ptr CS:[0x0287],8
47B2:05A9 jb short 0x05AF
47B2:05AF out 0x20,AL
47B2:05B1 mov DX,word ptr DS:[0x0285]
47B2:05B5 add DL,0x0E
47B2:05B8 in DX,AL
47B2:05B9 sti
47B2:05BA mov AX,word ptr DS:[0x02A0]
47B2:05BD or AX,word ptr DS:[0x02A2]
47B2:05C1 jne short 0x05D5
47B2:05C3 call near 0x0677
47B2:05C6 call near 0x065F
47B2:05C9 cmp byte ptr DS:[0x02A7],0
47B2:05CE je short 0x05D8
47B2:05D0 call near 0x0640
47B2:05D3 jmp short 0x05D8
47B2:05D8 pop BP
47B2:05D9 pop DI
47B2:05DA pop SI
47B2:05DB pop DX
47B2:05DC pop CX
47B2:05DD pop BX
47B2:05DE pop AX
47B2:05DF pop ES
47B2:05E0 pop DS
47B2:05E1 iret
47B2:05E2 mov CX,0xFFFF
47B2:05E5 cmp byte ptr DS:[0x02A6],0
47B2:05EA jne short 0x05F4
47B2:05EC inc byte ptr DS:[0x02A6]
47B2:05F0 mov CX,word ptr DS:[0x02A4]
47B2:05F4 sub CX,word ptr DS:[0x029E]
47B2:05F8 mov word ptr DS:[0x02A8],CX
47B2:05FC inc CX
47B2:05FD je short 0x060A
47B2:05FF sub word ptr DS:[0x02A0],CX
47B2:0603 sbb word ptr DS:[0x02A2],0
47B2:0608 jmp short 0x060E
47B2:060E mov DH,0x48
47B2:0610 mov DL,byte ptr DS:[0x029D]
47B2:0614 mov AX,word ptr DS:[0x029E]
47B2:0617 mov CX,word ptr DS:[0x02A8]
47B2:061B call near 0x0450
47B2:061E dec byte ptr DS:[0x02A6]
47B2:0622 inc byte ptr DS:[0x029D]
47B2:0626 mov word ptr DS:[0x029E],0
47B2:062C mov CX,word ptr DS:[0x02A8]
47B2:0630 mov AL,byte ptr DS:[0x02AA]
47B2:0633 call near 0x0333
47B2:0636 mov AL,CL
47B2:0638 call near 0x033A
47B2:063B mov AL,CH
47B2:063D jmp near 0x033A
47B2:0640 mov AL,byte ptr CS:[0x0288]
47B2:0644 or AL,4
47B2:0646 out 0x0A,AL
47B2:0648 call near 0x0524
47B2:064B xor AX,AX
47B2:064D mov byte ptr DS:[0x0292],AL
47B2:0650 mov word ptr DS:[0x02AB],AX
47B2:0653 mov word ptr DS:[0x0283],AX
47B2:0656 mov DX,word ptr DS:[0x0285]
47B2:065A add DL,0x0E
47B2:065D in DX,AL
47B2:065E ret near
47B2:065F call near 0x054C
47B2:0662 cmp AL,8
47B2:0664 jae short 0x0672
47B2:0666 cbw
47B2:0667 mov BX,AX
47B2:0669 shl BX,1
47B2:066B call near word ptr DS:[BX+0x02D0]
47B2:066F jb short 0x065F
47B2:0671 ret near
47B2:0677 push ES
47B2:0678 push AX
47B2:0679 push BX
47B2:067A push DX
47B2:067B les BX,word ptr DS:[0x0293]
47B2:067F mov AX,word ptr ES:[BX+1]
47B2:0683 xor DX,DX
47B2:0685 mov DL,byte ptr ES:[BX+3]
47B2:0689 add AX,4
47B2:068C adc DX,0
47B2:068F add AX,word ptr DS:[0x0293]
47B2:0693 adc DX,0
47B2:0696 ror DX,1
47B2:0698 ror DX,1
47B2:069A ror DX,1
47B2:069C ror DX,1
47B2:069E add DX,word ptr DS:[0x0295]
47B2:06A2 mov BX,AX
47B2:06A4 shr BX,1
47B2:06A6 shr BX,1
47B2:06A8 shr BX,1
47B2:06AA shr BX,1
47B2:06AC add DX,BX
47B2:06AE and AX,0x000F
47B2:06B1 mov word ptr DS:[0x0295],DX
47B2:06B5 mov word ptr DS:[0x0293],AX
47B2:06B8 pop DX
47B2:06B9 pop BX
47B2:06BA pop AX
47B2:06BB pop ES
47B2:06BC ret near
47B2:06BD push AX
47B2:06BE shr AX,1
47B2:06C0 shr AX,1
47B2:06C2 shr AX,1
47B2:06C4 shr AX,1
47B2:06C6 add DX,AX
47B2:06C8 pop AX
47B2:06C9 and AX,0x000F
47B2:06CC ret near
47B2:06CD push ES
47B2:06CE les SI,word ptr DS:[0x0118]
47B2:06D2 mov byte ptr ES:[SI+6],0
47B2:06D7 mov AL,byte ptr ES:[SI+7]
47B2:06DB shl AL,1
47B2:06DD jb short 0x0733
47B2:0733 mov byte ptr DS:[0x02FE],1
47B2:0738 mov byte ptr DS:[0x02A7],1
47B2:073D pop ES
47B2:073E clc
47B2:073F ret near
47B2:0740 push ES
47B2:0741 les DI,word ptr DS:[0x0293]
47B2:0745 mov AL,0x40
47B2:0747 call near 0x0333
47B2:074A mov AL,byte ptr ES:[DI+4]
47B2:074E mov byte ptr DS:[0x02FC],AL
47B2:0751 call near 0x033A
47B2:0754 mov AL,byte ptr ES:[DI+5]
47B2:0758 mov byte ptr DS:[0x02FD],AL
47B2:075B mov BX,0x02C5
47B2:075E xlat byte ptr DS:[BX+AL]
47B2:075F mov byte ptr DS:[0x02AA],AL
47B2:0762 pop ES
47B2:0763 mov AX,6
47B2:0766 call near 0x0556
47B2:0769 call near 0x05E2
47B2:076C call near 0x0771
47B2:076F clc
47B2:0770 ret near
47B2:0771 mov AL,byte ptr DS:[0x02AA]
47B2:0774 cmp AL,0x61
47B2:0776 jb short 0x0782
47B2:0782 and byte ptr DS:[0x02AA],0xFE
47B2:0787 ret near
47B2:082B push CX
47B2:082C push DX
47B2:082D push SI
47B2:082E push DI
47B2:082F push BP
47B2:0830 push DS
47B2:0831 push ES
47B2:0832 push CS
47B2:0833 pop DS
47B2:0834 mov word ptr DS:[0x028F],ES
47B2:0838 push CS
47B2:0839 pop ES
47B2:083A cmp BX,0x000E
47B2:083D jae short 0x0858
47B2:083F cmp BL,4
47B2:0842 jb short 0x0850
47B2:0844 cmp BL,0x0D
47B2:0847 je short 0x0850
47B2:0849 cmp byte ptr DS:[0x028E],0
47B2:084E je short 0x0858
47B2:0850 shl BX,1
47B2:0852 call near word ptr DS:[BX+0x02E0]
47B2:0856 jmp short 0x085B
47B2:085B pop ES
47B2:085C pop DS
47B2:085D pop BP
47B2:085E pop DI
47B2:085F pop SI
47B2:0860 pop DX
47B2:0861 pop CX
47B2:0862 ret near
47B2:0867 and AX,0xFFF8
47B2:086A mov BX,AX
47B2:086C sub AX,0x0210
47B2:086F cmp AX,0x0050
47B2:0872 ja short 0x08B0
47B2:0874 mov word ptr DS:[0x0285],BX
47B2:0878 xor AX,AX
47B2:087A ret near
47B2:087B cmp AL,0x0A
47B2:087D je short 0x088F
47B2:087F cmp AL,7
47B2:0881 je short 0x088F
47B2:088F mov byte ptr DS:[0x0287],AL
47B2:0892 xor AX,AX
47B2:0894 ret near
47B2:0895 and AL,7
47B2:0897 dec AL
47B2:0899 cmp AL,3
47B2:089B ja short 0x08B0
47B2:089D cmp AL,2
47B2:089F je short 0x08B0
47B2:08A1 mov byte ptr DS:[0x0288],AL
47B2:08A4 push BX
47B2:08A5 mov BX,0x028A
47B2:08A8 xlat byte ptr DS:[BX+AL]
47B2:08A9 mov byte ptr DS:[0x0289],AL
47B2:08AC pop BX
47B2:08AD xor AX,AX
47B2:08AF ret near
47B2:08B4 pushf
47B2:08B5 sti
47B2:08B6 call near 0x035C
47B2:08B9 jne short 0x08D1
47B2:08BB call near 0x0385
47B2:08BE jne short 0x08D1
47B2:08C0 call near 0x041C
47B2:08C3 jne short 0x08D1
47B2:08C5 call near 0x03D1
47B2:08C8 jne short 0x08D1
47B2:08CA mov AL,1
47B2:08CC call near 0x08D3
47B2:08CF xor AX,AX
47B2:08D1 popf
47B2:08D2 ret near
47B2:08D3 mov DX,word ptr DS:[0x0285]
47B2:08D7 add DL,0x0C
47B2:08DA or AL,AL
47B2:08DC mov AL,0xD1
47B2:08DE jne short 0x08E2
47B2:08E2 call near 0x033A
47B2:08E5 xor AX,AX
47B2:08E7 ret near
47B2:08E8 cmp byte ptr DS:[0x0292],0
47B2:08ED jne short 0x08B0
47B2:08EF inc byte ptr DS:[0x0292]
47B2:08F3 mov DX,word ptr DS:[0x028F]
47B2:08F7 mov AX,DI
47B2:08F9 call near 0x06BD
47B2:08FC mov word ptr DS:[0x0295],DX
47B2:0900 mov word ptr DS:[0x0293],AX
47B2:0903 xor AX,AX
47B2:0905 mov byte ptr DS:[0x02A7],AL
47B2:0908 mov word ptr DS:[0x02AB],AX
47B2:090B mov word ptr DS:[0x02AD],AX
47B2:090E mov AX,0x0591
47B2:0911 call near 0x04D8
47B2:0914 mov word ptr DS:[0x0283],0xFFFF
47B2:091A call near 0x065F
47B2:091D mov byte ptr DS:[0x02FE],0
47B2:0922 xor AX,AX
47B2:0924 ret near
47B2:0925 mov AX,1
47B2:0928 cmp byte ptr DS:[0x0292],0
47B2:092D je short 0x0937
47B2:0937 mov byte ptr DS:[0x02FE],1
47B2:093C ret near
47B2:095F mov CX,AX
47B2:0961 mov AX,1
47B2:0964 pushf
47B2:0965 cli
47B2:0966 mov BX,word ptr DS:[0x02AB]
47B2:096A or BX,BX
47B2:096C je short 0x0990
47B2:0990 popf
47B2:0991 ret near
4D1E:0100 jmp near 0x04FF
4D1E:0103 jmp near 0x0626
4D1E:0106 jmp near 0x0561
4D1E:010F jmp near 0x06F6
4D1E:0112 jmp near 0x05AB
4D1E:04DC push SS
4D1E:04DD pop ES
4D1E:04DE mov SI,BP
4D1E:04E0 lods AX,word ptr ES:[SI]
4D1E:04E2 add AX,2
4D1E:04E5 mov DI,AX
4D1E:04E7 push CX
4D1E:04E8 mov CX,9
4D1E:04EB mov AL,0x2E
4D1E:04ED repne scas AL,byte ptr ES:[DI]
4D1E:04EF pop CX
4D1E:04F0 jne short 0x04FC
4D1E:04F2 mov AX,word ptr CS:[0x04D9]
4D1E:04F6 stos word ptr ES:[DI],AX
4D1E:04F7 mov AL,byte ptr CS:[0x04DB]
4D1E:04FB stos byte ptr ES:[DI],AL
4D1E:04FC loop 0x04E0
4D1E:04FE ret near
4D1E:04FF or AX,AX
4D1E:0501 je short 0x051C
4D1E:0503 mov word ptr CS:[0x0115],AX
4D1E:0507 add AX,2
4D1E:050A mov word ptr CS:[0x0117],AX
4D1E:050E add AX,2
4D1E:0511 mov word ptr CS:[0x0119],AX
4D1E:0515 add AX,2
4D1E:0518 mov word ptr CS:[0x011B],AX
4D1E:051C mov AL,0xFE
4D1E:051E mov DX,word ptr CS:[0x0117]
4D1E:0523 out DX,AL
4D1E:0524 call near 0x04DC
4D1E:0527 mov AX,0x2001
4D1E:052A call near 0x1112
4D1E:052D mov AX,0x00BD
4D1E:0530 call near 0x1112
4D1E:0533 mov AX,0x4008
4D1E:0536 call near 0x1112
4D1E:0539 mov AX,0x0105
4D1E:053C call near 0x1109
4D1E:053F mov AX,4
4D1E:0542 call near 0x1109
4D1E:0545 call near 0x1185
4D1E:0548 push CS
4D1E:0549 call near 0x0561
4D1E:054C mov BX,0x0F00
4D1E:054F ret far
4D1E:0561 pushf
4D1E:0562 cli
4D1E:0563 call near 0x0EBA
4D1E:0566 xor AX,AX
4D1E:0568 mov byte ptr CS:[0x01DE],AL
4D1E:056C popf
4D1E:056D ret far
4D1E:056E push BX
4D1E:056F push DX
4D1E:0570 shr AL,1
4D1E:0572 shr AL,1
4D1E:0574 shr AL,1
4D1E:0576 mov DX,AX
4D1E:0578 mov BX,0xF078
4D1E:057B cmp AH,BL
4D1E:057D jbe short 0x0581
4D1E:0581 xor AL,AL
4D1E:0583 div BH
4D1E:0585 mul DL
4D1E:0587 xchg AH,DH
4D1E:0589 sub AH,BH
4D1E:058B neg AH
4D1E:058D cmp AH,BL
4D1E:058F jbe short 0x0593
4D1E:0593 xor AL,AL
4D1E:0595 div BH
4D1E:0597 mul DL
4D1E:0599 shr AX,1
4D1E:059B shr AX,1
4D1E:059D shr AX,1
4D1E:059F shr AX,1
4D1E:05A1 mov AH,DH
4D1E:05A3 and AX,0x0FF0
4D1E:05A6 or AL,AH
4D1E:05A8 pop DX
4D1E:05A9 pop BX
4D1E:05AA ret near
4D1E:05AB call near 0x056E
4D1E:05AE mov byte ptr CS:[0x04D8],AL
4D1E:05B2 mov byte ptr CS:[0x04D7],AL
4D1E:05B6 mov word ptr CS:[0x01E0],0xFFFF
4D1E:05BD ret far
4D1E:0626 push DS
4D1E:0627 push CS
4D1E:0628 pop DS
4D1E:0629 mov byte ptr DS:[0x01DF],AL
4D1E:062C mov AX,word ptr ES:[SI]
4D1E:062F mov DI,0x061C
4D1E:0632 mov word ptr DS:[DI],SI
4D1E:0634 mov word ptr DS:[DI+2],ES
4D1E:0637 mov word ptr DS:[DI+4],AX
4D1E:063A mov AX,word ptr ES:[SI+0x4000]
4D1E:063F mov word ptr DS:[DI+6],AX
4D1E:0642 mov AX,word ptr ES:[SI-32768]
4D1E:0647 mov word ptr DS:[DI+8],AX
4D1E:064A add SI,2
4D1E:064D mov word ptr DS:[0x011E],SI
4D1E:0651 mov word ptr DS:[0x0120],ES
4D1E:0655 sub SI,2
4D1E:0658 add SI,word ptr ES:[SI]
4D1E:065B mov word ptr DS:[0x0122],SI
4D1E:065F mov word ptr DS:[0x0124],ES
4D1E:0663 call near 0x0F53
4D1E:0666 call near 0x0F78
4D1E:0669 call near 0x068A
4D1E:066C mov AL,byte ptr DS:[0x04D8]
4D1E:066F mov byte ptr DS:[0x04D6],AL
4D1E:0672 call near 0x0F21
4D1E:0675 mov byte ptr DS:[0x04D7],AL
4D1E:0678 xor AX,AX
4D1E:067A mov word ptr DS:[0x0126],AX
4D1E:067D mov word ptr DS:[0x012C],AX
4D1E:0680 call near 0x0756
4D1E:0683 mov AL,0x80
4D1E:0685 mov byte ptr DS:[0x01DE],AL
4D1E:0688 pop DS
4D1E:0689 ret far
4D1E:068A push DS
4D1E:068B push DS
4D1E:068C pop ES
4D1E:068D lds SI,word ptr DS:[0x011E]
4D1E:0691 mov BP,SI
4D1E:0693 mov DI,0x022A
4D1E:0696 mov CX,0x0012
4D1E:0699 lods AX,word ptr DS:[SI]
4D1E:069A or AX,AX
4D1E:069C je short 0x06A0
4D1E:069E add AX,BP
4D1E:06A0 stos word ptr ES:[DI],AX
4D1E:06A1 loop 0x0699
4D1E:06A3 pop DS
4D1E:06A4 mov DI,0x024E
4D1E:06A7 mov CL,0x12
4D1E:06A9 mov AX,0x00FF
4D1E:06AC rep stos word ptr ES:[DI],AX
4D1E:06AE mov DI,0x0296
4D1E:06B1 mov CL,0x12
4D1E:06B3 rep stos word ptr ES:[DI],AX
4D1E:06B5 les SI,word ptr DS:[0x011E]
4D1E:06B9 mov word ptr DS:[0x0128],1
4D1E:06BF mov word ptr DS:[0x012A],0x0060
4D1E:06C5 mov CX,0x0012
4D1E:06C8 mov DI,0x01E2
4D1E:06CB mov SI,word ptr DS:[DI+0x48]
4D1E:06CE mov word ptr DS:[DI+0x24],SI
4D1E:06D1 mov word ptr DS:[DI],0xFFFF
4D1E:06D5 mov word ptr DS:[DI+0x021C],0
4D1E:06DB or SI,SI
4D1E:06DD je short 0x06E8
4D1E:06DF mov AX,CX
4D1E:06E1 call near 0x0E7E
4D1E:06E4 inc word ptr DS:[DI]
4D1E:06E6 mov CX,AX
4D1E:06E8 add DI,2
4D1E:06EB loop 0x06CB
4D1E:06ED xor AX,AX
4D1E:06EF mov word ptr DS:[0x013E],AX
4D1E:06F2 mov word ptr DS:[0x0140],AX
4D1E:06F5 ret near
4D1E:06F6 push DS
4D1E:06F7 mov AX,CS
4D1E:06F9 mov DS,AX
4D1E:06FB mov AL,byte ptr DS:[0x01DE]
4D1E:06FE or AL,AL
4D1E:0700 jns short 0x0723
4D1E:0702 dec byte ptr DS:[0x0127]
4D1E:0706 jns short 0x071A
4D1E:0708 call near 0x0730
4D1E:070B jne short 0x0723
4D1E:070D push DX
4D1E:070E push SI
4D1E:070F push DI
4D1E:0710 push BP
4D1E:0711 push ES
4D1E:0712 call near 0x0756
4D1E:0715 pop ES
4D1E:0716 pop BP
4D1E:0717 pop DI
4D1E:0718 pop SI
4D1E:0719 pop DX
4D1E:071A rol word ptr DS:[0x01E0],1
4D1E:071E jae short 0x0723
4D1E:0720 call near 0x0ECC
4D1E:0723 mov AL,byte ptr DS:[0x01DE]
4D1E:0726 mov BX,word ptr DS:[0x0128]
4D1E:072A mov CX,word ptr DS:[0x012A]
4D1E:072E pop DS
4D1E:072F ret far
4D1E:0730 push SI
4D1E:0731 push ES
4D1E:0732 les SI,word ptr DS:[0x061C]
4D1E:0736 mov AX,word ptr ES:[SI]
4D1E:0739 cmp word ptr DS:[0x0620],AX
4D1E:073D jne short 0x0753
4D1E:073F mov AX,word ptr ES:[SI+0x4000]
4D1E:0744 cmp word ptr DS:[0x0622],AX
4D1E:0748 jne short 0x0753
4D1E:074A mov AX,word ptr ES:[SI-32768]
4D1E:074F cmp word ptr DS:[0x0624],AX
4D1E:0753 pop ES
4D1E:0754 pop SI
4D1E:0755 ret near
4D1E:0756 les BX,word ptr DS:[0x011E]
4D1E:075A mov AX,word ptr ES:[BX+0x30]
4D1E:075E add word ptr DS:[0x0126],AX
4D1E:0762 mov DI,0x01E2
4D1E:0765 call near 0x07DA
4D1E:0768 mov CX,0x0012
4D1E:076B dec word ptr DS:[DI]
4D1E:076D jne short 0x07AD
4D1E:076F mov SI,word ptr DS:[DI+0x24]
4D1E:0772 or SI,SI
4D1E:0774 je short 0x0798
4D1E:0776 push CX
4D1E:0777 push DI
4D1E:0778 lods AX,word ptr ES:[SI]
4D1E:077A mov DX,DI
4D1E:077C sub DX,0x01E2
4D1E:0780 shr DX,1
4D1E:0782 mov BX,AX
4D1E:0784 and BX,0x0070
4D1E:0787 shr BX,1
4D1E:0789 shr BX,1
4D1E:078B shr BX,1
4D1E:078D call near word ptr DS:[BX+0x012E]
4D1E:0791 pop DI
4D1E:0792 pop CX
4D1E:0793 cmp word ptr DS:[DI],0
4D1E:0796 je short 0x076F
4D1E:0798 add DI,2
4D1E:079B loop 0x076B
4D1E:079D dec byte ptr DS:[0x012A]
4D1E:07A1 jne short 0x07AC
4D1E:07A3 mov byte ptr DS:[0x012A],0x60
4D1E:07A8 inc word ptr DS:[0x0128]
4D1E:07AC ret near
4D1E:07AD cmp byte ptr DS:[DI+0x00B4],0
4D1E:07B2 je short 0x0798
4D1E:07B4 mov SI,word ptr DS:[DI+0x24]
4D1E:07B7 or SI,SI
4D1E:07B9 je short 0x0798
4D1E:07BB push CX
4D1E:07BC push DI
4D1E:07BD dec byte ptr DS:[DI+0x00B4]
4D1E:07C1 mov AX,word ptr DS:[DI+0x00D8]
4D1E:07C5 add AL,AH
4D1E:07C7 mov byte ptr DS:[DI+0x00D8],AL
4D1E:07CB mov DX,DI
4D1E:07CD sub DX,0x01E2
4D1E:07D1 shr DX,1
4D1E:07D3 call near 0x0D8B
4D1E:07D6 pop DI
4D1E:07D7 pop CX
4D1E:07D8 jmp short 0x0798
4D1E:07DA cmp word ptr DS:[0x012C],0
4D1E:07DF jne short 0x080C
4D1E:07E1 mov AX,word ptr ES:[BX+0x2A]
4D1E:07E5 cmp AX,word ptr DS:[0x0128]
4D1E:07E9 jne short 0x080B
4D1E:080B ret near
4D1E:0831 call near 0x0E7E
4D1E:0834 mov byte ptr DS:[DI+0x6C],AH
4D1E:0837 mov AL,0x28
4D1E:0839 mul AH
4D1E:083B les SI,word ptr DS:[0x0122]
4D1E:083F add SI,AX
4D1E:0841 call near 0x090D
4D1E:0844 mov AX,word ptr ES:[SI+0x21]
4D1E:0848 mov word ptr DS:[DI+0x0090],AX
4D1E:084C mov AH,byte ptr ES:[SI+0x17]
4D1E:0850 mov AL,byte ptr ES:[SI+0x0A]
4D1E:0854 mov BH,byte ptr ES:[SI+2]
4D1E:0858 mov BL,byte ptr ES:[SI+0x0F]
4D1E:085C and BX,0x0303
4D1E:0860 ror BX,1
4D1E:0862 ror BX,1
4D1E:0864 or AX,BX
4D1E:0866 mov word ptr DS:[DI+0x0120],AX
4D1E:086A mov AX,word ptr ES:[SI+0x1E]
4D1E:086E mov word ptr DS:[DI+0x00FC],AX
4D1E:0872 mov AX,word ptr ES:[SI+0x26]
4D1E:0876 mov word ptr DS:[DI+0x018C],AX
4D1E:087A mov AH,byte ptr ES:[SI+4]
4D1E:087E mov BL,byte ptr ES:[SI+0x11]
4D1E:0882 shl BL,1
4D1E:0884 shl BL,1
4D1E:0886 shl BL,1
4D1E:0888 or AH,BL
4D1E:088A mov AL,byte ptr ES:[SI+0x0E]
4D1E:088E not AL
4D1E:0890 ror AL,1
4D1E:0892 shl AX,1
4D1E:0894 mov AL,byte ptr ES:[SI+0x20]
4D1E:0898 mov word ptr DS:[DI+0x0168],AX
4D1E:089C mov AL,byte ptr ES:[SI+0x1B]
4D1E:08A0 mov word ptr DS:[DI+0x01B0],AX
4D1E:08A4 mov AX,word ptr ES:[SI+0x23]
4D1E:08A8 mov byte ptr DS:[DI+0x00D9],AH
4D1E:08AC mov AH,AL
4D1E:08AE xor AL,AL
4D1E:08B0 mov word ptr DS:[DI+0x00B4],AX
4D1E:08B4 mov AL,byte ptr ES:[SI]
4D1E:08B7 mov byte ptr DS:[DI+0x02D0],AL
4D1E:08BB cmp AL,4
4D1E:08BD jne short 0x08ED
4D1E:08ED jmp near 0x0F95
4D1E:090D mov BP,word ptr DS:[0x013E]
4D1E:0911 mov CX,word ptr DS:[0x0140]
4D1E:0915 mov AX,word ptr DS:[DI+0x021C]
4D1E:0919 not AX
4D1E:091B or AX,AX
4D1E:091D js short 0x0923
4D1E:0923 and BP,AX
4D1E:0925 mov AL,byte ptr ES:[SI]
4D1E:0928 cmp AL,4
4D1E:092A jne short 0x0967
4D1E:0967 mov BX,BP
4D1E:0969 not BX
4D1E:096B and BX,0x01C0
4D1E:096F jne short 0x0999
4D1E:0971 mov BX,CX
4D1E:0973 not BX
4D1E:0975 test BX,0x01C0
4D1E:0979 je short 0x09CD
4D1E:097B and BX,0x01C0
4D1E:097F shr BX,1
4D1E:0981 shr BX,1
4D1E:0983 shr BX,1
4D1E:0985 shr BX,1
4D1E:0987 mov AX,word ptr DS:[BX+0x08ED]
4D1E:098B mov BX,word ptr DS:[BX+0x08EF]
4D1E:098F or AX,0x8080
4D1E:0992 or CX,BX
4D1E:0994 or BH,0x80
4D1E:0997 jmp short 0x09AB
4D1E:0999 shr BX,1
4D1E:099B shr BX,1
4D1E:099D shr BX,1
4D1E:099F shr BX,1
4D1E:09A1 mov AX,word ptr DS:[BX+0x08ED]
4D1E:09A5 mov BX,word ptr DS:[BX+0x08EF]
4D1E:09A9 or BP,BX
4D1E:09AB mov word ptr DS:[DI+0x021C],BX
4D1E:09AF mov word ptr DS:[0x013E],BP
4D1E:09B3 mov word ptr DS:[0x0140],CX
4D1E:09B7 mov BX,DX
4D1E:09B9 mov byte ptr DS:[BX+0x017F],AH
4D1E:09BD mov byte ptr DS:[BX+0x01A3],AL
4D1E:09C1 add AX,0x0303
4D1E:09C4 mov byte ptr DS:[BX+0x0191],AH
4D1E:09C8 mov byte ptr DS:[BX+0x01B5],AL
4D1E:09CC ret near
4D1E:0A82 lods AL,byte ptr ES:[SI]
4D1E:0A84 call near 0x0E7E
4D1E:0A87 push AX
4D1E:0A88 call near 0x0C47
4D1E:0A8B cmp byte ptr DS:[DI+0x6D],0
4D1E:0A8F je short 0x0A96
4D1E:0A96 pop AX
4D1E:0A97 mov AL,AH
4D1E:0A99 add AL,byte ptr DS:[DI+0x0091]
4D1E:0A9D xor AH,AH
4D1E:0A9F mov byte ptr DS:[DI+0x6D],AL
4D1E:0AA2 sub AX,0x0048
4D1E:0AA5 mov CL,byte ptr DS:[DI+0x00B5]
4D1E:0AA9 mov byte ptr DS:[DI+0x00B4],CL
4D1E:0AAD mov byte ptr DS:[DI+0x00D8],0x40
4D1E:0AB2 jmp near 0x10A9
4D1E:0C47 mov AH,AL
4D1E:0C49 mov AL,0x80
4D1E:0C4B sub AL,AH
4D1E:0C4D mov BX,word ptr DS:[DI+0x0120]
4D1E:0C51 mov CX,word ptr DS:[DI+0x00FC]
4D1E:0C55 or CL,CL
4D1E:0C57 je short 0x0C8D
4D1E:0C59 push AX
4D1E:0C5A jns short 0x0C60
4D1E:0C60 sub CL,4
4D1E:0C63 neg CL
4D1E:0C65 shr AL,CL
4D1E:0C67 mov AH,BL
4D1E:0C69 and AH,0x3F
4D1E:0C6C add AH,AL
4D1E:0C6E cmp AH,0x3F
4D1E:0C71 jbe short 0x0C75
4D1E:0C75 and BL,0xC0
4D1E:0C78 or BL,AH
4D1E:0C7A mov AH,BL
4D1E:0C7C mov SI,BX
4D1E:0C7E mov BX,0x01A3
4D1E:0C81 add BX,DX
4D1E:0C83 mov BL,byte ptr DS:[BX]
4D1E:0C85 mov AL,0x40
4D1E:0C87 call near 0x1101
4D1E:0C8A mov BX,SI
4D1E:0C8C pop AX
4D1E:0C8D or CH,CH
4D1E:0C8F je short 0x0CC2
4D1E:0C91 push AX
4D1E:0C92 jns short 0x0C98
4D1E:0C98 mov CL,4
4D1E:0C9A sub CL,CH
4D1E:0C9C shr AL,CL
4D1E:0C9E mov AH,BH
4D1E:0CA0 and AH,0x3F
4D1E:0CA3 add AH,AL
4D1E:0CA5 cmp AH,0x3F
4D1E:0CA8 jbe short 0x0CAC
4D1E:0CAC and BH,0xC0
4D1E:0CAF or BH,AH
4D1E:0CB1 mov AH,BH
4D1E:0CB3 push BX
4D1E:0CB4 mov BX,0x01B5
4D1E:0CB7 add BX,DX
4D1E:0CB9 mov BL,byte ptr DS:[BX]
4D1E:0CBB mov AL,0x40
4D1E:0CBD call near 0x1101
4D1E:0CC0 pop BX
4D1E:0CC1 pop AX
4D1E:0CC2 mov word ptr DS:[DI+0x0144],BX
4D1E:0CC6 cmp byte ptr DS:[DI+0x02D0],4
4D1E:0CCB jne short 0x0D4C
4D1E:0D4C mov CX,word ptr DS:[DI+0x0168]
4D1E:0D50 or CL,CL
4D1E:0D52 jne short 0x0D59
4D1E:0D54 mov byte ptr DS:[DI+0x01B1],CH
4D1E:0D58 ret near
4D1E:0D85 ret near
4D1E:0D8B mov CL,byte ptr DS:[DI+0x6D]
4D1E:0D8E xor CH,CH
4D1E:0D90 jcxz short 0x0D85
4D1E:0E7E push AX
4D1E:0E7F xor AX,AX
4D1E:0E81 lods AL,byte ptr ES:[SI]
4D1E:0E83 or AL,AL
4D1E:0E85 jns short 0x0EB3
4D1E:0E87 xor CX,CX
4D1E:0E89 mov CH,CL
4D1E:0E8B mov CL,AH
4D1E:0E8D mov AH,AL
4D1E:0E8F lods AL,byte ptr ES:[SI]
4D1E:0E91 or AL,AL
4D1E:0E93 js short 0x0E89
4D1E:0E95 and AX,0x7F7F
4D1E:0E98 and CX,0x7F7F
4D1E:0E9C shl CL,1
4D1E:0E9E shr CX,1
4D1E:0EA0 shl AL,1
4D1E:0EA2 shl AX,1
4D1E:0EA4 shr CX,1
4D1E:0EA6 rcr AX,1
4D1E:0EA8 shr CX,1
4D1E:0EAA rcr AX,1
4D1E:0EAC or CX,CX
4D1E:0EAE je short 0x0EB3
4D1E:0EB3 mov word ptr DS:[DI],AX
4D1E:0EB5 mov word ptr DS:[DI+0x24],SI
4D1E:0EB8 pop AX
4D1E:0EB9 ret near
4D1E:0EBA push DS
4D1E:0EBB push CS
4D1E:0EBC pop DS
4D1E:0EBD mov CX,0x0012
4D1E:0EC0 push CX
4D1E:0EC1 mov DX,CX
4D1E:0EC3 dec DX
4D1E:0EC4 call near 0x10D8
4D1E:0EC7 pop CX
4D1E:0EC8 loop 0x0EC0
4D1E:0ECA pop DS
4D1E:0ECB ret near
4D1E:0ECC mov AL,byte ptr DS:[0x04D6]
4D1E:0ECF cmp AL,byte ptr DS:[0x04D7]
4D1E:0ED3 jne short 0x0EE1
4D1E:0ED5 mov word ptr DS:[0x01E0],1
4D1E:0EDB and byte ptr DS:[0x01DE],0xBF
4D1E:0EE0 ret near
4D1E:0F21 mov AL,byte ptr CS:[0x04D6]
4D1E:0F25 push AX
4D1E:0F26 pushf
4D1E:0F27 cli
4D1E:0F28 call near 0x1176
4D1E:0F2B push AX
4D1E:0F2C and AL,0xF0
4D1E:0F2E mov AH,AL
4D1E:0F30 shr AH,1
4D1E:0F32 shr AH,1
4D1E:0F34 shr AH,1
4D1E:0F36 or AH,0x81
4D1E:0F39 mov AL,9
4D1E:0F3B call near 0x1158
4D1E:0F3E pop AX
4D1E:0F3F and AL,0x0F
4D1E:0F41 mov AH,AL
4D1E:0F43 shl AH,1
4D1E:0F45 or AH,0x81
4D1E:0F48 mov AL,0x0A
4D1E:0F4A call near 0x1158
4D1E:0F4D call near 0x116E
4D1E:0F50 popf
4D1E:0F51 pop AX
4D1E:0F52 ret near
4D1E:0F53 mov CX,0x0016
4D1E:0F56 mov AH,0xFF
4D1E:0F58 mov AL,CL
4D1E:0F5A dec AX
4D1E:0F5B cmp AL,6
4D1E:0F5D je short 0x0F75
4D1E:0F5F cmp AL,7
4D1E:0F61 je short 0x0F75
4D1E:0F63 cmp AL,0x0E
4D1E:0F65 je short 0x0F75
4D1E:0F67 cmp AL,0x0F
4D1E:0F69 je short 0x0F75
4D1E:0F6B add AL,0x80
4D1E:0F6D push AX
4D1E:0F6E call near 0x1112
4D1E:0F71 pop AX
4D1E:0F72 call near 0x1109
4D1E:0F75 loop 0x0F58
4D1E:0F77 ret near
4D1E:0F78 pushf
4D1E:0F79 cli
4D1E:0F7A call near 0x1176
4D1E:0F7D les SI,word ptr DS:[0x011E]
4D1E:0F81 add SI,0x0032
4D1E:0F84 lods AL,byte ptr ES:[SI]
4D1E:0F86 mov AH,AL
4D1E:0F88 mov AL,8
4D1E:0F8A call near 0x1158
4D1E:0F8D call near 0x11C4
4D1E:0F90 call near 0x116E
4D1E:0F93 popf
4D1E:0F94 ret near
4D1E:0F95 mov BX,DX
4D1E:0F97 mov AL,byte ptr DS:[BX+0x01A3]
4D1E:0F9B mov AH,byte ptr DS:[BX+0x01B5]
4D1E:0F9F mov BX,AX
4D1E:0FA1 call near 0x1002
4D1E:0FA4 xchg BH,BL
4D1E:0FA6 mov AH,byte ptr ES:[SI+0x1D]
4D1E:0FAA add SI,0x000D
4D1E:0FAD call near 0x102C
4D1E:0FB0 test BH,0x10
4D1E:0FB3 jne short 0x0FF4
4D1E:0FF4 ret near
4D1E:1002 mov AH,byte ptr ES:[SI+0x0E]
4D1E:1006 shr AX,1
4D1E:1008 mov AH,byte ptr ES:[SI+4]
4D1E:100C not AL
4D1E:100E shl AX,1
4D1E:1010 mov AL,byte ptr ES:[SI+0x11]
4D1E:1014 shl AL,1
4D1E:1016 shl AL,1
4D1E:1018 shl AL,1
4D1E:101A shl AL,1
4D1E:101C and AX,0x0F30
4D1E:101F or AH,AL
4D1E:1021 mov AL,0xC0
4D1E:1023 push BX
4D1E:1024 call near 0x10ED
4D1E:1027 pop BX
4D1E:1028 mov AH,byte ptr ES:[SI+0x1C]
4D1E:102C and AH,7
4D1E:102F mov AL,0xE0
4D1E:1031 call near 0x1101
4D1E:1034 mov AH,byte ptr ES:[SI+0x0A]
4D1E:1038 mov AL,byte ptr ES:[SI+2]
4D1E:103C shl AH,1
4D1E:103E shl AH,1
4D1E:1040 ror AX,1
4D1E:1042 ror AX,1
4D1E:1044 mov AL,0x40
4D1E:1046 call near 0x1101
4D1E:1049 mov AH,byte ptr ES:[SI+5]
4D1E:104D mov AL,byte ptr ES:[SI+8]
4D1E:1051 shl AL,1
4D1E:1053 shl AL,1
4D1E:1055 shl AL,1
4D1E:1057 shl AL,1
4D1E:1059 shl AX,1
4D1E:105B shl AX,1
4D1E:105D shl AX,1
4D1E:105F shl AX,1
4D1E:1061 mov AL,0x60
4D1E:1063 call near 0x1101
4D1E:1066 mov AH,byte ptr ES:[SI+6]
4D1E:106A mov AL,byte ptr ES:[SI+9]
4D1E:106E shl AL,1
4D1E:1070 shl AL,1
4D1E:1072 shl AL,1
4D1E:1074 shl AL,1
4D1E:1076 shl AX,1
4D1E:1078 shl AX,1
4D1E:107A shl AX,1
4D1E:107C shl AX,1
4D1E:107E mov AL,0x80
4D1E:1080 call near 0x1101
4D1E:1083 mov AL,byte ptr ES:[SI+0x0D]
4D1E:1087 ror AX,1
4D1E:1089 mov AL,byte ptr ES:[SI+7]
4D1E:108D ror AX,1
4D1E:108F mov AL,byte ptr ES:[SI+0x0C]
4D1E:1093 ror AX,1
4D1E:1095 mov AL,byte ptr ES:[SI+0x0B]
4D1E:1099 ror AX,1
4D1E:109B mov AL,byte ptr ES:[SI+3]
4D1E:109F and AX,0xF00F
4D1E:10A2 or AH,AL
4D1E:10A4 mov AL,0x20
4D1E:10A6 jmp short 0x1101
4D1E:10A9 add AX,0x0030
4D1E:10AC cmp AX,0x0060
4D1E:10AF jb short 0x10B3
4D1E:10B1 xor AX,AX
4D1E:10B3 mov BL,0x0C
4D1E:10B5 div BL
4D1E:10B7 mov CL,AL
4D1E:10B9 xchg AH,AL
4D1E:10BB xor AH,AH
4D1E:10BD add AX,AX
4D1E:10BF mov SI,AX
4D1E:10C1 mov AX,word ptr DS:[SI+0x0142]
4D1E:10C5 shl CL,1
4D1E:10C7 shl CL,1
4D1E:10C9 or AH,CL
4D1E:10CB mov SI,DX
4D1E:10CD add SI,SI
4D1E:10CF mov word ptr DS:[SI+0x015A],AX
4D1E:10D3 or AH,0x20
4D1E:10D6 jmp short 0x10E0
4D1E:10D8 mov SI,DX
4D1E:10DA add SI,SI
4D1E:10DC mov AX,word ptr DS:[SI+0x015A]
4D1E:10E0 mov CX,AX
4D1E:10E2 mov AL,0xA0
4D1E:10E4 mov AH,CL
4D1E:10E6 call near 0x10ED
4D1E:10E9 mov AL,0xB0
4D1E:10EB mov AH,CH
4D1E:10ED mov BL,DL
4D1E:10EF xor BH,BH
4D1E:10F1 add BX,0x017F
4D1E:10F5 mov BL,byte ptr DS:[BX]
4D1E:10F7 add AL,BL
4D1E:10F9 or BL,BL
4D1E:10FB jns short 0x1112
4D1E:10FD xor AL,0x80
4D1E:10FF jmp short 0x1109
4D1E:1101 add AL,BL
4D1E:1103 or BL,BL
4D1E:1105 jns short 0x1112
4D1E:1107 xor AL,0x80
4D1E:1109 push DX
4D1E:110A mov DX,word ptr CS:[0x0117]
4D1E:110F out DX,AL
4D1E:1110 jmp short 0x1119
4D1E:1112 push DX
4D1E:1113 mov DX,word ptr CS:[0x0115]
4D1E:1118 out DX,AL
4D1E:1119 in DX,AL
4D1E:111A in DX,AL
4D1E:111B in DX,AL
4D1E:111C in DX,AL
4D1E:111D in DX,AL
4D1E:111E in DX,AL
4D1E:111F in DX,AL
4D1E:1120 inc DX
4D1E:1121 mov AL,AH
4D1E:1123 out DX,AL
4D1E:1124 in DX,AL
4D1E:1125 in DX,AL
4D1E:1126 in DX,AL
4D1E:1127 in DX,AL
4D1E:1128 in DX,AL
4D1E:1129 in DX,AL
4D1E:112A in DX,AL
4D1E:112B in DX,AL
4D1E:112C in DX,AL
4D1E:112D in DX,AL
4D1E:112E in DX,AL
4D1E:112F in DX,AL
4D1E:1130 in DX,AL
4D1E:1131 in DX,AL
4D1E:1132 in DX,AL
4D1E:1133 in DX,AL
4D1E:1134 in DX,AL
4D1E:1135 in DX,AL
4D1E:1136 in DX,AL
4D1E:1137 in DX,AL
4D1E:1138 in DX,AL
4D1E:1139 in DX,AL
4D1E:113A in DX,AL
4D1E:113B in DX,AL
4D1E:113C in DX,AL
4D1E:113D in DX,AL
4D1E:113E in DX,AL
4D1E:113F in DX,AL
4D1E:1140 in DX,AL
4D1E:1141 in DX,AL
4D1E:1142 in DX,AL
4D1E:1143 in DX,AL
4D1E:1144 in DX,AL
4D1E:1145 in DX,AL
4D1E:1146 in DX,AL
4D1E:1147 pop DX
4D1E:1148 ret near
4D1E:1149 push AX
4D1E:114A push DX
4D1E:114B mov DX,word ptr CS:[0x0117]
4D1E:1150 in DX,AL
4D1E:1151 and AL,0xC0
4D1E:1153 jne short 0x1150
4D1E:1155 pop DX
4D1E:1156 pop AX
4D1E:1157 ret near
4D1E:1158 push AX
4D1E:1159 push DX
4D1E:115A mov DX,word ptr CS:[0x0117]
4D1E:115F push AX
4D1E:1160 in DX,AL
4D1E:1161 and AL,0xC0
4D1E:1163 jne short 0x1160
4D1E:1165 pop AX
4D1E:1166 out DX,AL
4D1E:1167 inc DX
4D1E:1168 mov AL,AH
4D1E:116A out DX,AL
4D1E:116B pop DX
4D1E:116C pop AX
4D1E:116D ret near
4D1E:116E call near 0x1149
4D1E:1171 push AX
4D1E:1172 mov AL,0xFE
4D1E:1174 jmp short 0x1179
4D1E:1176 push AX
4D1E:1177 mov AL,0xFF
4D1E:1179 push CX
4D1E:117A push DX
4D1E:117B mov DX,word ptr CS:[0x0117]
4D1E:1180 out DX,AL
4D1E:1181 pop DX
4D1E:1182 pop CX
4D1E:1183 pop AX
4D1E:1184 ret near
4D1E:1185 pushf
4D1E:1186 cli
4D1E:1187 call near 0x1176
4D1E:118A mov AX,0xFB06
4D1E:118D call near 0x1158
4D1E:1190 mov AX,0xF707
4D1E:1193 call near 0x1158
4D1E:1196 mov AX,0xF704
4D1E:1199 call near 0x1158
4D1E:119C inc AX
4D1E:119D call near 0x1158
4D1E:11A0 mov AX,0xFF09
4D1E:11A3 call near 0x1158
4D1E:11A6 inc AX
4D1E:11A7 call near 0x1158
4D1E:11AA call near 0x1149
4D1E:11AD mov AL,0
4D1E:11AF mov DX,word ptr CS:[0x0117]
4D1E:11B4 out DX,AL
4D1E:11B5 inc DX
4D1E:11B6 in DX,AL
4D1E:11B7 not AL
4D1E:11B9 and AL,0x20
4D1E:11BB mov byte ptr CS:[0x011D],AL
4D1E:11BF call near 0x116E
4D1E:11C2 popf
4D1E:11C3 ret near
4D1E:11C4 cmp byte ptr CS:[0x011D],0
4D1E:11CA je short 0x11F3
4D1E:11CC pushf
4D1E:11CD cli
4D1E:11CE xor DX,DX
4D1E:11D0 mov AL,DL
4D1E:11D2 xor AH,AH
4D1E:11D4 call near 0x11F4
4D1E:11D7 or AH,4
4D1E:11DA mov AL,0x18
4D1E:11DC call near 0x1158
4D1E:11DF lods AL,byte ptr ES:[SI]
4D1E:11E1 call near 0x11F4
4D1E:11E4 and AH,0xFB
4D1E:11E7 mov AL,0x18
4D1E:11E9 call near 0x1158
4D1E:11EC inc DX
4D1E:11ED cmp DL,0x1F
4D1E:11F0 jb short 0x11D0
4D1E:11F2 popf
4D1E:11F3 ret near
4D1E:11F4 mov BL,AL
4D1E:11F6 mov AL,0x18
4D1E:11F8 mov CX,8
4D1E:11FB and AH,0xFD
4D1E:11FE call near 0x1158
4D1E:1201 xor BH,BH
4D1E:1203 shl BX,1
4D1E:1205 and AH,0xFE
4D1E:1208 or AH,BH
4D1E:120A call near 0x1158
4D1E:120D or AH,2
4D1E:1210 call near 0x1158
4D1E:1213 loop 0x11FB
4D1E:1215 ret near
F000:0000 callback 0x0010
F000:0004 iret
F000:0005 iret
F000:0006 callback 8
F000:000A int 0x1C
F000:000C callback 0x0101
F000:0010 iret
F000:004E call far F000:00AE
F000:0053 callback 0x0074
F000:0057 iret
F000:0080 callback 0x0021
F000:0084 iret
F000:00A8 callback 0x0033
F000:00AC iret
F000:00AD ret far
F000:00AE callback 0x010A
F000:00B2 call far F000:00AD
F000:00B7 callback 0x010B
F000:00BB ret far
