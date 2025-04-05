# NativeDosTests
some assembler and C/C++ example for serveral DOS APIs based on  [Open Watcom V2](https://github.com/open-watcom/open-watcom-v2) toolchain

| file | description |
| ---- | ----------- |
| build.bat | creates the DOS executables - WATCOM path variable in first line needs to be adjusted |
| clean.bat | cleans up the built mess |
| hello.asm | simple DOS 21h/ah=9 hello print on console |
| exec.asm | DOS 21h/ax=4B00 program starter, starting hello example |
| c_exec.c | same as exec but based on C stdlib |
| tsr.asm | TSR that hooks DOS 21h/ah=9 and replaces 'l' with 'X' in console print - 'hello' becomes 'heXXO'|

