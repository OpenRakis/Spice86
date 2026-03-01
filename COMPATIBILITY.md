A lot of programs do run. A lot of programs do not run.

If it crashes, this is mainly because:

- Some video features are not implemented (eg. smooth scrolling, CGA, full EGA...).
- Some of DOS kernel interrupts are not implemented.
- Some BIOS features are not implemented.

Here is a list of old games I tested with what worked and what didn't:

| Program | State | Comment | Update date |
|--|--|--|--|
| Alpha Waves | :sunglasses: Fully playable | | 2026/02/22  |
| Alley Cat | :see_no_evil: Crashes | CGA not implemented | 2021/09/26  |
| Alone in the dark | :see_no_evil: Crashes | Complains about 386 CPU. | 2026/02/22  |
| Another World | :sunglasses: Fully playable | | 2026/02/22  |
| Arachnophobia | :see_no_evil: Crashes | Int 10.11 operation "GET INT 1F pointer" not implemented. | 2021/09/26  |
| Arkanoid 2 : Revenge of Doh | :see_no_evil: Crashes | Timer latch read mode not implemented. | 2021/09/26  |
| Blake Stone: Aliens of Gold | :sunglasses: Fully playable |  | 2023/10/02  |
| Cadaver | :sunglasses: Fully playable | | 2026/02/22  |
| Cruise for a Corpse | :sunglasses: Fully playable | | 2026/02/22  |
| Bio Menace | :sunglasses: Fully playable |  | 2023/09/30  |
| Betrayal at Krondor | :sunglasses: Fully playable |  | 2023/06/25  |
| Cryo Dune | :sunglasses: Fully playable | | 2021/09/26  |
| Day of the Tentacle | :sunglasses: Fully playable | Disable logs with -s to not distrub game Adlib OPL music driver startup with unexpected delays | 2026/02/22  |
| Doom8088 | :sunglasses: Fully playable | | 2026/02/22  |
| Double dragon 3 | :see_no_evil: Crashes | Int 10.3 (text mode) not implemented. | 2021/09/26  |
| Duke Nukem | :sunglasses: Fully playable | But with no PC Speaker sound effects | 2023/06/18  |
| Duke Nukem II | :sunglasses: Fully playable | | 2023/08/23  |
| Dragon's Lair | :see_no_evil: Crashes | Terminates without displaying anything and without error. | 2021/09/26  |
| Dragon's Lair 3 | :see_no_evil: Crashes | Int 10.3 (text mode) not implemented. | 2021/09/26  |
| Dune 2 | :sunglasses: Fully playable |  | 2023/06/25  |
| Enviro-Kids | :sunglasses: Fully playable | | 2026/02/22  |
| F-15 Strike Eagle II | :see_no_evil: Crashes | Crashes with invalid opcode (which is weird) | 2026/02/22  |
| Flight Simulator 5 | :see_no_evil: Crashes | Crashes with invalid opcode (which is weird) | 2026/02/22  |
| Home Alone | :see_no_evil: Crashes | Int 10.8 (text mode) not implemented | 2021/09/26  |
| Hero Quest |  :sunglasses: Fully playable | | 2022/09/02  |
| Indiana Jones and The Fate of Atlantis |  :sunglasses: Fully playable | | 2026/02/22  |
| Jill of the Jungle |  :sunglasses: Fully playable | | 2026/02/22  |
| KGB | :sunglasses: Fully playable | | 2021/09/26  |
| Knights of Xentar | :sunglasses: Fully playable | | 2026/02/22  |
| Legends of Kyrandia | :sunglasses: Fully playable | | 2026/02/22  |
| Lost Eden |  :sunglasses: Fully playable | | 2026/02/22  |
| Monkey Island | :sunglasses: Fully playable | | 2025/03/22  |
| Monkey Island 2 | :sunglasses: Fully playable | | 2026/02/22  |
| Oliver & Compagnie | :see_no_evil: Crashes | Int 10.11 operation "GET INT 1F pointer" not implemented. | 2021/09/26  |
| Oh No! More Lemmings | :sunglasses: Fully playable | | 2026/02/22  |
| Prince of persia | :sunglasses: Fully playable | No OPL music is played...? | 2021/09/26  |
| Prince of persia 2 | :sunglasses: Fully playable | | 2022/02/26  |
| Planet X3 | :sunglasses: Fully playable | | 2026/02/22  |
| Plan 9 From Outer Space| :confused: Not playable | Black screen. | 2021/09/26  |
| Populous | :sunglasses: Fully playable | No music or sound is played...? | 2021/10/01  |
| Quest for glory 3 | :see_no_evil: Crashes | Int 2F not implemented (Himem XMS Driver) | 2021/09/26  |
| Rules of Engagement 2 | :sunglasses: Fully playable | | 2026/02/22  |
| SimCity | :see_no_evil: Crashes | Int 10.11 operation "ROM 8x8 double dot pointer" not implemented. | 2021/09/26  |
| Stunts | :sunglasses: | Fully playable | 2025/03/22  |
| Space Quest : The Sarien Encounter | :confused: Not playable | Program exits with code 1. | 2021/09/26  |
| Space Quest IV : Roger Wilco and the Time Rippers | :confused: Not playable | Program exits with code 1. | 2021/09/26  |
| Starvega | :see_no_evil: Crashes | Int 10.11 operation "GET INT 1F pointer" not implemented. | 2021/09/26  |
| Super Tetris | :see_no_evil: Crashes | Int 10.8 (text mode) not implemented | 2021/09/26  |
| Top Gun : Danger Zone | :see_no_evil: Crashes | Accesses stdin via dos file API and this is not implemented. | 2021/09/26  |
| Ultima IV : The Quest of the Avatar | :confused: Not playable | Black screen. | 2021/09/26  |
| Ultima V | :sunglasses: Fully playable | | 2026/02/22  |
| Where in Space is Carmen Sandiego | :sunglasses: Fully playable | | 2026/02/22  |
| Wizardry 7 | :sunglasses: Fully playable | | 2026/02/22  |
