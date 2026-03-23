async
text 10-11
graphic 16-19
13443017 Instructions per seconds on average over run.
Executed 552811357 instructions in 39081ms. 14145271 Instructions per seconds on average over run.

--RenderingMode Sync
8-9
15-18
Executed 980762570 instructions in 67822ms. 14460832 Instructions per seconds on average over run.


I want to avoid expensive per-scanline pixel conversion and unnecessary frame publishing when VRAM and VGA registers are unchanged.

Two main optimizations:
- Skip `Renderer.RenderScanline()` processing for if no VRAM/registers changed since last published frame. Caution: programs can write mid frame so it's not because a line was not rendered that the next line will not have to. As soon as a register / vram change, the skipping should not happen anymore for this frame. Also, internal state updates should still happen for the frame so that state is as if rendering did happen from the emulated world perspective..
- Skip copying front buffer to the output in `Renderer.Render()` if no new frame was published (so if all scanlines were skipped).