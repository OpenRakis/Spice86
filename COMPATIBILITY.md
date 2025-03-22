A lot of programs are not running.

This is mainly because:
 - Some VGA features are not implemented (eg. smooth scrolling).
 - Quite a lot of DOS kernel interrupts are not implemented.
 - TSR / sub programs / XMS is not implemented

Here is a list of old games I tested with what worked and what didn't:

| Program | State | Comment | Update date |
|--|--|--|--|
| Alley Cat | :see_no_evil: Crashes | CGA not implemented | 2021/09/26  |
| Alone in the dark | :see_no_evil: Crashes | Terminate Process and Remain Resident not implemented. | 2021/09/26  |
| Another World | :see_no_evil: Crashes | Fails with unimplemented int 15.6 (Which is weird) | 2021/09/26  |
| Arachnophobia | :see_no_evil: Crashes | Int 10.11 operation "GET INT 1F pointer" not implemented. | 2021/09/26  |
| Arkanoid 2 : Revenge of Doh | :see_no_evil: Crashes | Timer latch read mode not implemented. | 2021/09/26  |
| Blake Stone: Aliens of Gold | :sunglasses: Fully playable |  | 2023/10/02  |
| Bio Menace | :sunglasses: Fully playable |  | 2023/09/30  |
| Betrayal at Krondor | :sunglasses: Fully playable |  | 2023/06/25  |
| Cryo Dune | :sunglasses: Fully playable | | 2021/09/26  |
| Double dragon 3 | :see_no_evil: Crashes | Int 10.3 (text mode) not implemented. | 2021/09/26  |
| Duke Nukem | :sunglasses: Fully playable | But with no PC Speaker sound effects | 2023/06/18  |
| Duke Nukem II | :sunglasses: Fully playable | | 2023/08/23  |
| Dragon's Lair | :see_no_evil: Crashes | Terminates without displaying anything and without error. | 2021/09/26  |
| Dragon's Lair 3 | :see_no_evil: Crashes | Int 10.3 (text mode) not implemented. | 2021/09/26  |
| Dune 2 | :sunglasses: Fully playable |  | 2023/06/25  |
| F-15 Strike Eagle II | :see_no_evil: Crashes | Launching sub programs not implemented (int 21, 4B) | 2021/09/26  |
| Flight Simulator 5 | :see_no_evil: Crashes | Launching sub programs not implemented (int 21, 4B) | 2021/09/26  |
| Home Alone | :see_no_evil: Crashes | Int 10.8 (text mode) not implemented | 2021/09/26  |
| Hero Quest |  :sunglasses: Fully playable | | 2022/09/02  |
| KGB | :sunglasses: Fully playable | | 2021/09/26  |
| Monkey Island | :sunglasses: Fully playable | 2025/03/22 |
| Oliver & Compagnie | :see_no_evil: Crashes | Int 10.11 operation "GET INT 1F pointer" not implemented. | 2021/09/26  |
| Prince of persia | :sunglasses: Fully playable | No music is played...? | 2021/09/26  |
| Plan 9 From Outer Space| :confused: Not playable | Black screen. | 2021/09/26  |
| Populous | :sunglasses: Fully playable | No music or sound is played...? | 2021/10/01  |
| Quest for glory 3 | :see_no_evil: Crashes | Int 2F not implemented (Himem XMS Driver) | 2021/09/26  |
| SimCity | :see_no_evil: Crashes | Int 10.11 operation "ROM 8x8 double dot pointer" not implemented. | 2021/09/26  |
| Stunts | :see_no_evil: Crashes | Crashes at startup with the message "Invalid Group Index 0x7". | 2022/09/02  |
| Space Quest : The Sarien Encounter | :confused: Not playable | Program exits with code 1. | 2021/09/26  |
| Space Quest IV : Roger Wilco and the Time Rippers | :confused: Not playable | Program exits with code 1. | 2021/09/26  |
| Starvega | :see_no_evil: Crashes | Int 10.11 operation "GET INT 1F pointer" not implemented. | 2021/09/26  |
| Super Tetris | :see_no_evil: Crashes | Int 10.8 (text mode) not implemented | 2021/09/26  |
| Top Gun : Danger Zone | :see_no_evil: Crashes | Accesses stdin via dos file API and this is not implemented. | 2021/09/26  |
| Ultima IV : The Quest of the Avatar | :confused: Not playable | Black screen. | 2021/09/26  |
