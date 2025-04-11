rem grab your Open Watcom V2 copy from https://github.com/open-watcom/open-watcom-v2/releases
set WATCOM=f:\projects\fun\dos_games_rev\tools\open-watcom-2_0-c-win-x64
set WATCOM_BIN=%WATCOM%\binnt64
set INCLUDE=%WATCOM%\h
set PATH=%WATCOM_BIN%;%PATH%

wasm.exe hello.asm
wlink.exe format dos file hello.obj name hello.exe

wasm.exe exec.asm
wlink.exe format dos file exec.obj name exec.exe

wcl.exe -bt=dos c_exec.c

wasm.exe tsr.asm
wlink.exe format dos com file tsr.obj name tsr.com

pause