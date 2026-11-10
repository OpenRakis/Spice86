# NativeDosTests
some assembler and C/C++ example for several DOS APIs based on  [Open Watcom V2](https://github.com/open-watcom/open-watcom-v2) toolchain
| file | description |
| ---- | ----------- |
| build.bat | creates the DOS executables - WATCOM path variable in first line needs to be adjusted |
| clean.bat | cleans up the built mess |
| hello.asm | simple DOS 21h/ah=9 hello print on console |
| exec.asm | DOS 21h/ax=4B00 program starter, starting hello example |
| c_exec.c | same as exec but based on C stdlib |
| tsr.asm | TSR that hooks DOS 21h/ah=9 and replaces 'l' with 'X' in console print - 'hello' becomes 'heXXO'|
| compiler_idioms.c | C89 word/long idiom probe: a `* 2UL + w` long fold loop, a signed `/2`, a `!x` and a `1..9` range test printed on console |

## compiler_idioms.exe provenance

Built from `compiler_idioms.c` with the Open Watcom V2 `Current-build` snapshot
`ow-snapshot.tar.xz` (SHA-256 `0fcc05d629e5999a24f910d83cbe074ff3067162cfb99a19be8aadbfad6aea34`,
`wcl` reports `Version 2.0 beta Nov 10 2026 08:01:06 (64-bit)`, wlink `07:50:07`):

```text
WATCOM=<extracted snapshot root>
INCLUDE=$WATCOM/h
PATH=$WATCOM/binl64:$WATCOM/binl:$PATH
wcl -bt=dos -ms -0 -ot compiler_idioms.c -fe=compiler_idioms.exe
```

Result: `compiler_idioms.exe` 9814 bytes, SHA-256
`424e6db4ebd9534b0e4ecedc55ffe6368b55485a57e2af93a36ee8549d14f1b4`. The `.exe` is gitignored
(`*.exe`); it is force-added as a test resource.

