# Generated code idioms

Each section shows one small ASM fixture and the program-only C# the code generator
produces for it. Regenerate with `SPICE86_WRITE_CODEGEN_IDIOMS=1 dotnet test --project tests/Spice86.Tests -- --filter-class '*CodegenIdiomsDocTest*'`.

## Fail-as-untested guards

Unobserved conditional edges and indirect targets become guards that throw FailAsUntested instead of executing unchecked code.

```asm
; Build: fasm idiom_fail_as_untested.asm ../idiom_fail_as_untested.bin
use16

start:
    mov byte [0x0600], 0x01
    mov al, byte [0x0500]
    test al, al
    jnz never_taken
    mov byte [0x0601], 0xDD
    cmp al, 7
    je indirect
    hlt
indirect:
    mov bx, never_taken
    jmp bx
never_taken:
    mov byte [0x0601], 0xEE
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        byte byte1 = 0;
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        byte1 = DsGlobals.Byte0500;
        if (byte1 != 0) {
            VerifySpeculativeEntryOrFail(biosSegment, 0x001B, Signature_001B_mov);
            DsGlobals.Byte0601 = 0xEE;
            VerifySpeculativeEntryOrFail(biosSegment, 0x0020, Signature_0020_hlt);
            return Hlt();
        }
        DsGlobals.Byte0601 = 0xDD;
        if (byte1 == 7) {
            VerifySpeculativeRunOrFail(biosSegment, 0x0016, SpeculativeRun_0016);
            BX = 0x001B;
            throw FailAsUntested("Untested indirect jump at F000:0019");
        }
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Typed method signatures

A method with register parameters and results becomes a typed C# method returning a tuple.

```asm
; Build: fasm idiom_typed_signature.asm ../idiom_typed_signature.bin
use16

start:
    mov byte [0x0600], 0x01
    mov cx, 5
    call compute
    mov byte [0x0601], al
    mov byte [0x0602], dl
    hlt
compute:
    mov ax, cx
    add ax, 0x30
    mov dx, cx
    add dx, 0x40
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: CX. Outputs: AL, DL. Changes: AX, DX.</summary>
    public virtual (byte result, byte result2) unknown_F000_0013_F0013(ushort word1) {
        CheckExternalEvents(biosSegment, 0x0013);
        AX = word1;
        AX += 0x0030;
        DX = (ushort)(word1 + 0x0040);
        NearReturn();
        return (AL, DL);
    }

    public virtual Action unknown_F000_0013_F0013_Asm() {
        (AL, DL) = unknown_F000_0013_F0013(CX);
        return Returned;
    }

    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        byte result = 0;
        byte result2 = 0;
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        (result, result2) = NearCall(biosSegment, 0x000B, () => unknown_F000_0013_F0013(5));
        DsGlobals.Byte0601 = result;
        DsGlobals.Byte0602 = result2;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Stack save, restore and release

Pushes and pops lower to typed stack operations, with discarded slots released instead of popped.

```asm
; Build: fasm idiom_stack_save_restore.asm ../idiom_stack_save_restore.bin
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 0x1111
    mov bx, 0x2222
    mov cx, 0x3333
    push ax
    push bx
    push cx
    mov ax, 0xDD
    mov byte [0x0601], al
    pop cx
    pop bx
    pop ax
    mov byte [0x0602], ah
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        Stack.Push16(0x1111);
        Stack.Push16(0x2222);
        Stack.Push16(0x3333);
        DsGlobals.Byte0601 = 0xDD;
        Stack.Release(1);
        Stack.Release(1);
        AX = Stack.Pop16();
        DsGlobals.Byte0602 = AH;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Named Div16 arguments

A 32/16 division becomes a Div16 call with named divisor and dividend arguments.

```asm
; Build: fasm idiom_div16_named.asm ../idiom_div16_named.bin
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 0x0000
    mov dx, 0x0002
    mov cx, 0x0003
    div cx
    mov byte [0x0601], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        (AX, DX) = Div16(divisor: 3, dividend: 0x00020000);
        DsGlobals.Byte0601 = AL;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Dead store elimination

A store overwritten before any read disappears from the generated code.

```asm
; Build: fasm idiom_dead_store.asm ../idiom_dead_store.bin
; a store overwritten before any read is eliminated. Stores go to scratch in the DS=2000h
; segment (the shape elimination applies to); the result bytes are written through ES=0.
use16

start:
    mov ax, 0x2000
    mov ds, ax
    mov byte [es:0x0600], 0x01
    mov word [0x20], 0x1111
    mov word [0x20], 0x2222
    mov ax, word [0x20]
    mov byte [es:0x0601], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: ES. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        CheckExternalEvents(biosSegment, 0xFFF0);
        DS = 0x2000;
        EsGlobals.Byte0600 = 1;
        Segment2000Globals.Word0020 = 0x2222;
        EsGlobals.Byte0601 = 0x22;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Wide value merging

Byte and word pieces written separately merge into one wide read when the program reads them as a single value.

```asm
; Build: fasm idiom_wide_values.asm ../idiom_wide_values.bin
; two byte stores merge into a word read; two word stores merge into a dword read.
use16

start:
    mov byte [0x0600], 0x01
    mov byte [0x0610], 0x34
    mov byte [0x0611], 0x12
    mov ax, word [0x0610]
    mov byte [0x0601], ah
    mov word [0x0612], 0x5678
    mov word [0x0614], 0x1234
    mov eax, dword [0x0612]
    shr eax, 16
    mov byte [0x0602], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        DsGlobals.Byte0610 = 0x34;
        DsGlobals.Byte0611 = 0x12;
        AX = DsGlobals.Word0610;
        DsGlobals.Byte0601 = AH;
        DsGlobals.Word0612 = 0x5678;
        DsGlobals.Word0614 = 0x1234;
        EAX = DsGlobals.Dword0612 >> 16;
        DsGlobals.Byte0602 = AL;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Flag idioms

A known flag folds into its branch while a data-dependent comparison keeps its tested condition.

```asm
; Build: fasm idiom_flag_idioms.asm ../idiom_flag_idioms.bin
; a known flag (stc) folds into its conditional branch; a data-dependent compare
; (input read through CS so the value is opaque) keeps its flag tested in the branch.
use16

start:
    mov byte [0x0600], 0x01
    stc
    jc carry_taken
    mov byte [0x0601], 0x00
    jmp after_carry
carry_taken:
    mov byte [0x0601], 0x01
after_carry:
    mov si, input
    mov ax, [cs:si]
    cmp ax, 5
    ja above
    mov byte [0x0602], 0x00
    jmp done
above:
    mov byte [0x0602], 0x01
done:
    hlt

input: dw 7

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        ushort word1;
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        DsGlobals.Byte0601 = 1;
        word1 = SegmentF000Globals.Word002C;
        if (word1 > 5) {
            DsGlobals.Byte0602 = 1;
        }
        else {
            VerifySpeculativeEntryOrFail(biosSegment, 0x001F, Signature_001F_mov);
            DsGlobals.Byte0602 = 0;
            VerifySpeculativeEntryOrFail(biosSegment, 0x0024, Signature_0024_jmp);
        }
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Call sites

Calls with stack arguments, discarded outputs and 32-bit operand width each get their own typed call helper.

```asm
; Build: fasm idiom_call_sites.asm ../idiom_call_sites.bin
; a near call with a stack argument, a call whose output is discarded, and an o32 call.
; fasm has no o32 form, so the 66h-prefixed encodings are written out (see cc_call_32bit).
use16

start:
    mov byte [0x0600], 0x01
    push 0x0042
    call with_arg
    add sp, 2
    mov byte [0x0601], al
    call discarded
    db 66h, 0E8h            ; o32 call wide32 (rel32)
    dd wide32 - $ - 4
    mov byte [0x0602], al
    hlt
with_arg:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    pop bp
    ret
discarded:
    mov ax, 0x9999
    ret
wide32:
    mov eax, 0x55
    db 66h, 0C3h            ; o32 ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: BP. Outputs: AL. Changes: AX, BP.</summary>
    public virtual byte unknown_F000_001D_F001D(ushort arg0) {
        CheckExternalEvents(biosSegment, 0x001D);
        Stack.Push16(BP);
        AX = arg0;
        BP = Stack.Pop16();
        NearReturn();
        return AL;
    }

    public virtual Action unknown_F000_001D_F001D_Asm() {
        AL = unknown_F000_001D_F001D(new GeneratedStackFrame(Memory, State.SegmentRegisters, SP).GetWord(2));
        return Returned;
    }

    public readonly struct unknown_F000_001D_F001DFrame(IMemory memory, SegmentRegisters segmentRegisters, ushort entrySp) {
        private readonly GeneratedStackFrame _slots = new(memory, segmentRegisters, entrySp);

        /// <summary>Word at SS:[entry SP + 0x0002].</summary>
        public ushort Arg0 { get => _slots.GetWord(0x0002); set => _slots.SetWord(0x0002, value); }
    }

    /// <summary>Inputs: none. Outputs: none. Changes: AX.</summary>
    public virtual Action unknown_F000_0025_F0025() {
        CheckExternalEvents(biosSegment, 0x0025);
        AX = 0x9999;
        return NearRet();
    }

    /// <summary>Inputs: none. Outputs: AL. Changes: EAX.</summary>
    public virtual byte unknown_F000_0029_F0029() {
        CheckExternalEvents(biosSegment, 0x0029);
        EAX = 0x00000055;
        NearReturn32();
        return AL;
    }

    public virtual Action unknown_F000_0029_F0029_Asm() {
        AL = unknown_F000_0029_F0029();
        return Returned;
    }

    /// <summary>Inputs: BP, DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        byte result = 0;
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        AL = CallWithStackArguments(biosSegment, 0x000A, () => unknown_F000_001D_F001D((ushort)0x0042), (ushort)0x0042);
        SP += 2;
        DsGlobals.Byte0601 = AL;
        NearCall(biosSegment, 0x0013, unknown_F000_0025_F0025Entry);
        result = NearCall32(biosSegment, 0x0019, () => unknown_F000_0029_F0029());
        DsGlobals.Byte0602 = result;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Locals naming

In-place index updates become role-named locals instead of register temporaries.

```asm
; Build: fasm idiom_locals_naming.asm ../idiom_locals_naming.bin
use16

start:
    mov byte [0x0600], 0x01
    mov si, 0
loop_top:
    mov byte [si+0x0610], 0xAA
    inc si
    cmp si, 4
    jb loop_top
    mov ax, si
    mov byte [0x0601], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        ushort index = 0;
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        index = 0;
        do {
            CheckExternalEvents(biosSegment, 0x0008);
            UInt8[DS, index + 0x0610] = 0xAA;
            index++;
        } while (index < 4);
        AX = index;
        DsGlobals.Byte0601 = AL;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Interrupt handler contracts

An observed interrupt handler carries its Inputs/Outputs contract as a doc comment.

```asm
; Build: fasm idiom_contracts.asm ../idiom_contracts.bin
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 0
    mov ds, ax
    mov word [0x0200], handler
    mov word [0x0202], 0xF000
    mov byte [0x0601], 0xDD
    int 0x80
    mov byte [0x0602], al
    hlt
handler:
    mov al, 0x42
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: none. Outputs: AL. Changes: AL, flags, DF.</summary>
    /// Segment values at entry, from every call site: DS = 0x0000.
    public virtual Action unknown_F000_0021_F0021() {
        CheckExternalEvents(biosSegment, 0x0021);
        AL = 0x42;
        return InterruptRet();
    }

    /// <summary>Inputs: DS, flags, DF. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        AX = 0;
        DS = 0;
        Segment0000Globals.Dword0200 = 0xF0000021u;
        Segment0000Globals.Byte0601 = 0xDD;
        InterruptCall(biosSegment, 0x001D, 0x80, biosSegment, 0x0021, unknown_F000_0021_F0021Entry);
        Segment0000Globals.Byte0602 = AL;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Relaxation: unknown memory is RAM

With UnknownMemoryIsRam, an access to memory not known to be RAM is generated as a plain typed access.

```asm
; Build: fasm idiom_relaxation_base.asm ../idiom_relaxation_base.bin
; unknown-memory-is-RAM: an access to the VGA framebuffer segment, memory that is not
; known to be RAM. Strict mode treats it as device memory; the relaxation treats it as RAM.
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 0xA000
    mov es, ax
    mov byte [es:0x0010], 0x42
    mov al, [es:0x0010]
    mov byte [0x0601], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        AX = 0xA000;
        ES = 0xA000;
        SegmentA000Globals.Byte0010 = 0x42;
        DsGlobals.Byte0601 = SegmentA000Globals.Byte0010;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: UnknownMemoryIsRam

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Relaxation: accesses stay in segment

With AccessesStayInSegment, an indexed access with an unproved range is generated without segment-wrap handling.

```asm
; Build: fasm idiom_relaxation_segment.asm ../idiom_relaxation_segment.bin
; accesses-stay-in-segment: an indexed access whose index is read through CS, so its range
; is not proved and strict mode must handle the segment-wrap case.
use16

start:
    mov byte [0x0600], 0x01
    mov si, input
    mov bx, [cs:si]
    mov byte [bx], 0x42
    mov al, [bx]
    mov byte [0x0601], al
    hlt
input: dw 0x0610

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        ushort index;
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        index = SegmentF000Globals.Word0014;
        UInt8[DS, index] = 0x42;
        DsGlobals.Byte0601 = UInt8[DS, index];
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: AccessesStayInSegment

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Relaxation: abandoned stack slots

With AbandonedStackSlotsUnobserved, a private typed stack argument's bytes are treated as unobserved after the call.

```asm
; Build: fasm idiom_relaxation_stack.asm ../idiom_relaxation_stack.bin
; abandoned-stack-slots: nested typed stack calls like cc_calls_keep_stack_offset.
; SS:SP are set up because typed call helpers assume a real stack; results go through ES=0.
use16

start:
    mov ax, 0x2000
    mov ss, ax
    mov sp, 0x1000
    mov byte [es:0x0600], 0x01
    push 0x5678
    call callee_p
    mov byte [es:0x0601], al
    mov byte [es:0x0602], dl
    hlt
callee_p:
    push 0x1234
    call callee_q
    mov bx, sp
    mov dx, [ss:bx+2]
    ret 2
callee_q:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    pop bp
    ret 2

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: none. Outputs: AL, DL. Changes: AX, DX.</summary>
    public virtual (byte result, byte result2) unknown_F000_001E_F001E() {
        AL = CallWithStackSpace(biosSegment, 0x0024, 0x2, () => unknown_F000_002D_F002D(0x1234));
        DX = SsUInt16[SP + 2];
        NearReturn(2);
        return (AL, DL);
    }

    public virtual Action unknown_F000_001E_F001E_Asm() {
        (AL, DL) = unknown_F000_001E_F001E();
        return Returned;
    }

    /// <summary>Inputs: none. Outputs: AL. Changes: AX.</summary>
    public virtual byte unknown_F000_002D_F002D(ushort arg0) {
        CheckExternalEvents(biosSegment, 0x002D);
        AX = arg0;
        NearReturn(2);
        return AL;
    }

    public virtual Action unknown_F000_002D_F002D_Asm() {
        AL = unknown_F000_002D_F002D(new GeneratedStackFrame(Memory, State.SegmentRegisters, SP).GetWord(2));
        return Returned;
    }

    public readonly struct unknown_F000_002D_F002DFrame(IMemory memory, SegmentRegisters segmentRegisters, ushort entrySp) {
        private readonly GeneratedStackFrame _slots = new(memory, segmentRegisters, entrySp);

        /// <summary>Word at SS:[entry SP + 0x0002].</summary>
        public ushort Arg0 { get => _slots.GetWord(0x0002); set => _slots.SetWord(0x0002, value); }
    }

    /// <summary>Inputs: ES. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        byte result = 0;
        byte result2 = 0;
        CheckExternalEvents(biosSegment, 0xFFF0);
        AX = 0x2000;
        SS = 0x2000;
        SP = 0x1000;
        EsGlobals.Byte0600 = 1;
        Stack.Push16(0x5678);
        (result, result2) = NearCall(biosSegment, 0x0014, () => unknown_F000_001E_F001E());
        EsGlobals.Byte0601 = result;
        EsGlobals.Byte0602 = result2;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: AbandonedStackSlotsUnobserved

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Relaxation: saved slots

With SavedSlotsWrittenOnlyByPops, a slot written by a save push is treated as written only by its pop across call boundaries.

```asm
; Build: fasm idiom_relaxation_saved.asm ../idiom_relaxation_saved.bin
; saved-slots: a method saves registers around a call and restores them after.
use16

start:
    mov byte [0x0600], 0x01
    mov bx, 0x11
    mov cx, 0x22
    call worker
    mov byte [0x0601], bl
    mov byte [0x0602], cl
    hlt
worker:
    push bx
    push cx
    mov bx, 0x99
    mov cx, 0x88
    call leaf
    pop cx
    pop bx
    ret
leaf:
    mov ax, 0x55
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: CX, BX. Outputs: CL, BL. Changes: AX, CX, BX.</summary>
    public virtual (byte result, byte result2) unknown_F000_0017_F0017() {
        CheckExternalEvents(biosSegment, 0x0017);
        Stack.Save(BX, CX);
        NearCall(biosSegment, 0x0022, unknown_F000_0025_F0025Entry);
        (CX, BX) = Stack.Restore2();
        NearReturn();
        return (CL, BL);
    }

    public virtual Action unknown_F000_0017_F0017_Asm() {
        (CL, BL) = unknown_F000_0017_F0017();
        return Returned;
    }

    /// <summary>Inputs: none. Outputs: none. Changes: AX.</summary>
    public virtual Action unknown_F000_0025_F0025() {
        CheckExternalEvents(biosSegment, 0x0025);
        AX = 0x0055;
        return NearRet();
    }

    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        byte result = 0;
        byte result2 = 0;
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        BX = 0x0011;
        CX = 0x0022;
        (result, result2) = NearCall(biosSegment, 0x000E, () => unknown_F000_0017_F0017());
        DsGlobals.Byte0601 = result2;
        DsGlobals.Byte0602 = result;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: SavedSlotsWrittenOnlyByPops

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Jump table switch

An indexed jump table with every target observed at one site lowers to a switch with one case per target.

```asm
; Build: fasm idiom_jump_table_switch.asm ../idiom_jump_table_switch.bin
; an indexed jump table lowered to a switch. One indirect jump site executes both table
; entries in a loop, so the switch keeps two cases (a single-case switch simplifies away).
use16

start:
    mov byte [0x0600], 0x01
    mov word [0x0620], 0
loop_start:
    mov bx, [0x0620]
    cmp bx, 2
    jae done
    shl bx, 1
    jmp word [cs:table+bx]
target0:
    mov byte [0x0601], 0xD0
    jmp next
target1:
    mov byte [0x0602], 0xD1
    jmp next
next:
    inc word [0x0620]
    jmp loop_start
done:
    hlt
table:
    dw target0
    dw target1

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        DsGlobals.Word0620 = 0;
        while (true) {
            CheckExternalEvents(biosSegment, 0x000B);
            BX = DsGlobals.Word0620;
            if (BX >= 2) {
                break;
            }
            BX <<= 1;
            switch (UInt16[CS, BX + 0x2F]) {
                case 0x001A:
                    DsGlobals.Byte0601 = 0xD0;
                    break;
                case 0x0021:
                    DsGlobals.Byte0602 = 0xD1;
                    break;
                default:
                    throw FailAsUntested($"Untested near jump target 0x{UInt16[CS, BX + 0x2F]:X4} at F000:0016");
            }
            DsGlobals.Word0620 += 1;
        }
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Memory accessors

Absolute accesses become typed segment globals and indexed accesses become wrapping 16-bit indexers.

```asm
; Build: fasm idiom_memory_accessors.asm ../idiom_memory_accessors.bin
; absolute accesses lower to typed segment globals; accesses through an index read from
; memory lower to wrapping 16-bit indexers.
use16

start:
    mov byte [0x0600], 0x01
    mov word [0x0610], 0x5678
    mov ax, word [0x0610]
    mov byte [0x0601], al
    mov word [0x0620], 0x0600
    mov bx, [0x0620]
    mov byte [bx+2], 0x42
    mov ax, [0x0620]
    mov bx, ax
    mov cl, [bx+2]
    mov byte [0x0603], cl
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        ushort index;
        byte byte1;
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        DsGlobals.Word0610 = 0x5678;
        AX = DsGlobals.Word0610;
        DsGlobals.Byte0601 = AL;
        DsGlobals.Word0620 = 0x0600;
        index = DsGlobals.Word0620;
        UInt8[DS, index + 2] = 0x42;
        byte1 = UInt8[DS, DsGlobals.Word0620 + 2];
        DsGlobals.Byte0603 = byte1;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Stack frames

A bp frame with a mov sp,bp epilogue becomes a typed frame struct with named slots.

```asm
; Build: fasm idiom_stack_frames.asm ../idiom_stack_frames.bin
; a bp frame with a local slot and a mov sp,bp epilogue.
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 3
    call framed
    mov byte [0x0601], al
    hlt
framed:
    push bp
    mov bp, sp
    sub sp, 2
    mov [bp-2], ax
    mov ax, [bp-2]
    add ax, 0x40
    mov sp, bp
    pop bp
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: AX, BP. Outputs: AL. Changes: AX, BP.</summary>
    public virtual byte unknown_F000_000F_F000F(ushort word1) {
        ushort index;
        unknown_F000_000F_F000FFrame frame = new(Memory, State.SegmentRegisters, SP);
        CheckExternalEvents(biosSegment, 0x000F);
        Stack.Push16(BP);
        index = SP;
        SP -= 2;
        frame.Local4 = word1;
        AX = (ushort)(frame.Local4 + 0x0040);
        SP = index;
        BP = Stack.Pop16();
        NearReturn();
        return AL;
    }

    public virtual Action unknown_F000_000F_F000F_Asm() {
        AL = unknown_F000_000F_F000F(AX);
        return Returned;
    }

    public readonly struct unknown_F000_000F_F000FFrame(IMemory memory, SegmentRegisters segmentRegisters, ushort entrySp) {
        private readonly GeneratedStackFrame _slots = new(memory, segmentRegisters, entrySp);

        /// <summary>Word at SS:[entry SP - 0x0004].</summary>
        public ushort Local4 { get => _slots.GetWord(-0x0004); set => _slots.SetWord(-0x0004, value); }
    }

    /// <summary>Inputs: BP, DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        byte result = 0;
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        AX = 3;
        result = NearCall(biosSegment, 0x000B, () => unknown_F000_000F_F000F(3));
        DsGlobals.Byte0601 = result;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```

## Fault dispatch

A CPU fault with an observed handler becomes a try/catch that dispatches the fault to the handler.

```asm
; Build: fasm idiom_fault_dispatch.asm ../idiom_fault_dispatch.bin
; a divide-by-zero fault dispatches to the observed int 0 handler.
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 0
    mov ds, ax
    mov word [0x0000], fault_handler
    mov word [0x0002], 0xF000
    xor cx, cx
    mov ax, 5
    div cx
    mov byte [0x0601], 0xFF
    hlt
fault_handler:
    mov byte [0x0601], 0x42
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
```

```csharp
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

public partial class CfgGeneratedOverrides {
    /// <summary>Inputs: DS. Does not return.</summary>
    public virtual Action unknown_F000_0023_F0023() {
        CheckExternalEvents(biosSegment, 0x0023);
        DsGlobals.Byte0601 = 0x42;
        return Hlt();
    }

    /// <summary>Inputs: CX, DX, DS. Does not return.</summary>
    public virtual Action unknown_F000_FFF0_FFFF0() {
        CheckExternalEvents(biosSegment, 0xFFF0);
        DsGlobals.Byte0600 = 1;
        DS = 0;
        Segment0000Globals.Dword0000 = 0xF0000023u;
        CX = Alu16.Xor(CX, CX);
        AX = 5;
        try {
            (AX, DX) = Div16(divisor: CX, dividend: (uint)DX << 16 | (uint)AX);
        }
        catch (CpuException cpuException) {
            return DispatchCpuFault(cpuException, biosSegment, 0x001B, [new CpuFaultTarget(new SegmentedAddress(biosSegment, 0x0023), unknown_F000_0023_F0023)], []);
        }
        VerifySpeculativeRunOrFail(biosSegment, 0x001D, SpeculativeRun_001D);
        Segment0000Globals.Byte0601 = 0xFF;
        return Hlt();
    }

}

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Microsoft.Extensions.Logging;
using Spice86.Shared.Emulator.Memory;
using System;
using System.Collections.Generic;

namespace Spice86.Generated;

// Relaxations: none

public sealed class CfgGeneratedOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        return new CfgGeneratedOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;
    }
}
```
