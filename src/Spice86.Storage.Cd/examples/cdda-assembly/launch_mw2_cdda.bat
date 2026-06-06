@echo off

set MW2IMG=C:\jeux\isos\MechW2.cue
set CDDAROOT=C:\Users\noalm\source\repos\Spice86\src\Spice86.Storage.Cd\examples\cdda-assembly

echo [36m[CDDA Launcher][0m Preparing MechWarrior 2 CD image...

echo [33m[STEP][0m imgmount d "%MW2IMG%" -t cdrom
imgmount d "%MW2IMG%" -t cdrom
if errorlevel 1 goto imgmount_failed

echo [33m[STEP][0m mount c %CDDAROOT%
mount c %CDDAROOT%
if errorlevel 1 goto mount_failed

echo [33m[STEP][0m switching to C:
c:

echo [32m[RUN ][0m launching CDDA.COM (loop mode + ANSI dashboard)
CDDA.COM
goto end

:imgmount_failed
echo [31m[ERR ][0m IMGMOUNT failed for %MW2IMG%
goto end

:mount_failed
echo [31m[ERR ][0m MOUNT failed for %CDDAROOT%

:end
