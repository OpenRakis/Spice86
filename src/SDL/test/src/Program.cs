using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Drawing;
using SDLSharp;

namespace sdlcstest
{
    class Program
    {
        static void Pump()
        {
            foreach (Event ev in Events.Current)
            {
                switch (ev.type)
                {
                    case EventType.Quit:
                        Environment.Exit(0);
                        break;
                    case EventType.UserEvent:
                        if (ev.IsObjectEvent(out object? obj)) {
                          if (obj is Action a)
                            a();
                        }
                        break;
                    case EventType.KeyUp:
                        if (ev.keyboard.keysym.keycode == Keycode.F3)
                        {
                            var wind = Window.CurrentlyGrabbingInput();
                            if (wind != null)
                                wind.IsGrabbingInput = false;
                        }
                        break;
                }
                //Console.WriteLine(ev.ToString());
            }
        }

        void AskThings()
        {
            int selected = MessageBox.ShowWarning(
              "Title",
              "Body text",
              new[] {
                  new MessageBoxButton(1, "One"),
                  new MessageBoxButton(2, "Escape me") { Key = MessageBoxButtonDefaultKey.Escape },
                  new MessageBoxButton(3, "Two"),
                  new MessageBoxButton(4, "OK") { Key = MessageBoxButtonDefaultKey.Return },
              },
              colorScheme: new MessageBoxColors()
              {
                  Background = System.Drawing.Color.Sienna,
                  Text = System.Drawing.Color.HotPink,
                  ButtonBorder = System.Drawing.Color.Yellow,
                  ButtonBackground = System.Drawing.Color.Khaki,
                  ButtonSelected = System.Drawing.Color.Crimson,
              });
            Console.WriteLine($"Clicked {selected}");
        }

        static void PrintChannels()
        {
            Console.Write($"channels ({Mixer.Channels.Reserved}/{Mixer.Channels.Count} reserved playing={Mixer.Channels.Playing} paused={Mixer.Channels.Paused} volume={Mixer.Channels.Volume})");
            foreach (MixerChannel chan in Mixer.Channels)
            {
                Console.Write($" {chan}");
            }
            Console.WriteLine();
        }

        static void Main(string[] args)
        {
            SDL.Init(InitFlags.Everything);
            Log.Priorities.SetAll(LogPriority.Verbose);
            Log.OutputFunction = (cat, prio, msg) => {
              Console.WriteLine($"[{prio}] {cat}: {msg}");
            };
            Log.Message(LogCategory.Application, LogPriority.Debug, "Foo and bar!?");
            TTF.Init();
            Events.Watch += (ref Event e) =>
            {
                //Console.WriteLine(e.ToString());
            };

            Console.WriteLine("Registering timer");
            int callbackCount = 0;
            IDisposable? d = null;
            d = SDLSharp.Timer.AddTimer(500, n =>
            {
                Console.WriteLine($"{n}: Timer callback {callbackCount++}");
                if (callbackCount > 4 && d != null)
                {
                    Console.WriteLine("I've had it with this callback business!");
                    d.Dispose();
                }
                return 500 * (uint)callbackCount;
            });
            Console.WriteLine($"{d} registered");

            Console.WriteLine("Ok, here we go:");


            Console.WriteLine($"Running SDL version {SDL.RuntimeVersion} {SDL.RuntimeRevision} {SDL.RuntimeRevisionNumber}");
            Console.WriteLine($"\tSDL_image version {Image.RuntimeVersion}");
            Console.WriteLine($"\tSDL_ttf version {TTF.RuntimeVersion}");
            Console.WriteLine($"Video drivers: {string.Join("; ", Video.Drivers)}");
            Console.WriteLine($"Audio drivers: {string.Join("; ", Audio.Drivers)}");
            Console.WriteLine($"Audio input devices: {string.Join("; ", Audio.InputDevices)}");
            Console.WriteLine($"Audio output devices: {string.Join("; ", Audio.InputDevices)}");

            if (Clipboard.HasText())
            {
                Console.Write("Clipboard contains: ");
                Console.WriteLine(Clipboard.GetText());
            }
            else
            {
                Console.Write("Clipboard is empty. An empty string looks like: ");
                Console.WriteLine(Clipboard.GetText());
            }

            foreach (Display display in Video.Displays)
            {
                Console.WriteLine($"Display {display.Index}");
                Console.WriteLine($"    Name={display.Name}");
                Console.WriteLine($"    Bounds={display.Bounds}");
                Console.WriteLine($"    UsableBounds={display.UsableBounds}");
                Console.WriteLine($"    DPI={display.DPI}");
                Console.WriteLine($"    Modes.Current={display.Modes.Current}");
                Console.WriteLine($"    Modes.Desktop={display.Modes.Desktop}");
                for (int i = 0; i < display.Modes.Count; ++i)
                {
                    Console.WriteLine($"    Modes.[{i}]={display.Modes[i]}");
                }
            }
            for (int i = 0; i < Renderer.Drivers.Count; ++i)
            {
                RendererInfo ri = Renderer.Drivers[i];
                Console.WriteLine($"Render driver {i}");
                Console.WriteLine($"    Name={ri.Name}");
                Console.WriteLine($"    Flags={ri.Flags}");
                Console.WriteLine($"    MaxTextureWidth={ri.MaxTextureWidth}");
                Console.WriteLine($"    MaxTextureHeight={ri.MaxTextureHeight}");
                for (int j = 0; j < ri.Formats.Count; ++j)
                    Console.WriteLine($"    Formats.[{j}]={PixelFormat.GetName(ri.Formats[j])}");
            }

            foreach (JoystickDevice joy in Joystick.Devices)
            {
                Console.WriteLine($"{joy}");
                Console.WriteLine($"\tGuid={joy.Guid}");
                Console.WriteLine($"\tName={joy.Name}");
                Console.WriteLine($"\tIsController={joy.IsController}");
                using (Joystick j = joy.Open())
                {
                    Console.WriteLine($"\t\tOpened {j}");
                    Console.WriteLine($"\t\tID={j.ID}");
                    Console.WriteLine($"\t\tName={j.Name}");
                    Console.WriteLine($"\t\tGuid={j.Guid}");
                    Console.WriteLine($"\t\tPowerLevel={j.PowerLevel}");
                    Console.WriteLine($"\t\tNumAxes={j.Axes.Count}");
                    Console.WriteLine($"\t\tButtons={j.Buttons.Count}");
                    Console.WriteLine($"\t\tHats={j.Hats.Count}");
                    Console.WriteLine($"\t\tBalls={j.Balls.Count}");
                }

                if (joy.IsController)
                {
                    using (GameController c = joy.OpenController())
                    {
                        foreach (GameControllerAxis axis in Enum.GetValues(typeof(GameControllerAxis)).Cast<GameControllerAxis>())
                        {
                            if (axis == GameControllerAxis.Invalid || axis == GameControllerAxis.Max)
                                continue;

                            GameControllerBind? b = c.Axes.GetBind(axis);
                            Console.WriteLine($"\t\tAxis[{axis}/{GameController.AxisName(axis)}].Bind={b}");
                        }
                        foreach (GameControllerButton btn in Enum.GetValues(typeof(GameControllerButton)).Cast<GameControllerButton>())
                        {
                            if (btn == GameControllerButton.Invalid || btn == GameControllerButton.Max)
                                continue;
                            GameControllerBind? b = c.Buttons.GetBind(btn);
                            Console.WriteLine($"\t\tButtons[{btn}/{GameController.ButtonName(btn)}].Bind={b}");
                        }
                        Console.WriteLine($"\t\tMapping={c.Mapping}");
                    }
                }
            }

            foreach (string dev in Audio.OutputDevices)
            {
                var fmt = new AudioStreamFormat(44000, AudioDataFormat.Float32Bit, 1, 0, 1024);
                AllowedAudioStreamChange changes = AllowedAudioStreamChange.Any;
                Console.Write($"Requesting {fmt} from audio output '{dev}' allowing changes to {changes}...");

                try
                {
                    using (AudioOutputDevice a = Audio.OpenOutput(dev, changes, fmt, out AudioStreamFormat obtained))
                    {
                        Console.WriteLine($"\tgot {obtained}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\tFailed w/ error: {ex}");
                }
            }


            foreach (string dev in Audio.InputDevices)
            {
                var fmt = new AudioStreamFormat(44000, AudioDataFormat.Float32Bit, 1, 0, 1024);
                AllowedAudioStreamChange changes = AllowedAudioStreamChange.Any;
                Console.Write($"Requesting {fmt} from audio input '{dev}' allowing changes to {changes}...");

                try
                {
                    using (AudioInputDevice a = Audio.OpenInput(dev, changes, fmt, out AudioStreamFormat obtained))
                    {
                        Console.WriteLine($"\tgot {a} with format {obtained}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\tFailed w/ error: {ex}");
                }
            }

            Mixer.Open(44100, AudioDataFormat.Float32Bit, 2, 1024);
            Mixer.Init(MixerLoaders.MP3);

            Console.WriteLine($"Opened the mixer hz={Mixer.DeviceFrequency} f={Mixer.DeviceFormat} ch={Mixer.DeviceChannels}");
            Console.WriteLine($"\tAvailable music decoders {string.Join("; ", MusicTrack.Decoders)}");
            Console.WriteLine($"\tAvailable chunk decoders {string.Join("; ", MixerChunk.Decoders)}");


            PrintChannels();
            Mixer.Channels.Count = 8;
            PrintChannels();


            using var caketown = MusicTrack.Load("./assets/Caketown 1.mp3");
            Console.WriteLine($"Loaded {caketown}");
            Task.Run(async () =>
            {
                Mixer.Music.Play(caketown);

                Mixer.Music.Finished += (se, ev) => Console.WriteLine("Music finished");

                await Task.Delay(1000);
                Mixer.Music.Pause();
                Console.WriteLine($"{caketown} paused");
                await Task.Delay(1000);
                Mixer.Music.Resume();
                Console.WriteLine($"{caketown} resumed");
                await Task.Delay(1000);
                Mixer.Music.Rewind();
                Console.WriteLine($"{caketown} rewound");
                await Task.Delay(1000);
                Console.WriteLine($"{caketown} fading out");
                Mixer.Music.FadeOut(1000);
                Console.WriteLine($"{caketown} faded out");
                Mixer.Music.Halt();
                Console.WriteLine($"{caketown} halted");
            });

            using var alarm = MixerChunk.Load("./assets/wobbleboxx.com/Rise01.wav");

            WindowFlags flags = WindowFlags.Shown | WindowFlags.Resizable;

            using (Window window = Video.CreateWindow("Foo änd bar", Window.UndefinedPosition, Window.UndefinedPosition, 600, 400, flags))
            using (var renderer = Renderer.Create(window, -1, RendererFlags.Accelerated))
            {

                Console.WriteLine($"Created window {window}");

                Mixer.Channels.ChannelFinished += (se, ev) => Console.WriteLine($"{ev.Channel} finished");
                window.SetHitTest((Window w, in SDLSharp.Point p) =>
                {
                    Console.WriteLine($"Hit test at {(System.Drawing.Point)p} in window '{w}'");
                    Events.Current.Push((Action)(() =>
                    {
                        if (Mixer.Channels.Play(alarm, 1))
                            Console.Write("Playing sound ");
                        else
                            Console.Write("NOT playing sound ");
                        PrintChannels();
                    }));
                    return HitTestResult.Normal;
                });


                Texture? tex, tex2, solidTex, blendedTex, shadedTex;
                tex = null;
                tex2 = null;
                solidTex = null;
                blendedTex = null;
                shadedTex = null;
                int lineHeight;

                using (TTFFont f = TTF.OpenFont("./assets/OGAFGJF1.ttf", 18))
                {
                    Console.WriteLine($"Opened font {f}:");
                    Console.WriteLine($"\tFamilyName={f.FamilyName}");
                    Console.WriteLine($"\tStyleName={f.StyleName}");
                    Console.WriteLine($"\tHeight={f.Height}");
                    Console.WriteLine($"\tFaces={f.Faces}");
                    Console.WriteLine($"\tStyle={f.Style}");
                    Console.WriteLine($"\tAscent={f.Ascent}");
                    Console.WriteLine($"\tDescent={f.Descent}");
                    Console.WriteLine($"\tHinting={f.Hinting}");
                    Console.WriteLine($"\tKerning={f.Kerning}");
                    Console.WriteLine($"\tOutline={f.Outline}");
                    Console.WriteLine($"\tLineSkip={f.LineSkip}");
                    Console.WriteLine($"\tMonospace={f.Monospace}");

                    foreach (char c in "åbƜ1")
                    {
                        if (f.ProvidesGlyph(c))
                        {
                            Console.WriteLine($"\tProvides[{c}]=true");
                            GlyphMetrics metrics = f.GetMetrics(c);
                            Console.WriteLine($"\tMetrics[{c}]={metrics}");
                        }
                        else
                        {
                            Console.WriteLine($"\tProvides[{c}]=false");
                        }
                    }

                    using (Surface solid = f.RenderSolid("Hello solid world", System.Drawing.Color.White))
                        solidTex = renderer.CreateTexture(solid);
                    using (Surface shaded = f.RenderShaded("Hello shaded world", System.Drawing.Color.White, System.Drawing.Color.Gray))
                        shadedTex = renderer.CreateTexture(shaded);
                    using (Surface blended = f.RenderBlended("Hello blended world", System.Drawing.Color.White))
                        blendedTex = renderer.CreateTexture(blended);
                    lineHeight = f.LineSkip;
                }

                try
                {
                    string s = "./assets/Genetica Texture Pack 11 - Cartoon Backdrops/Rendered Textures/City Night.jpg";
                    /*
                    using (var read = RWOps.FromStream(System.IO.File.OpenRead(s)))
                    {
                        using (var surf = Image.Load(read))
                            tex2 = renderer.CreateTexture(surf);
                    }*/

                    using (Surface surf = Image.Load(s))
                        tex2 = renderer.CreateTexture(surf);

                    using (var fmt = new PixelFormat(renderer.Info.Formats.First()))
                    using (var surf = Surface.Create(
                      64,
                      64,
                      24,
                      (uint)fmt.DataFormat
                    ))
                    {
                        surf.Fill(fmt.Encode(System.Drawing.Color.Gold));
                        var top = new Rect(0, 0, 64, 32);
                        surf.Fill(top, fmt.Encode(System.Drawing.Color.Pink));

                        Console.WriteLine($"Filled {surf}");

                        window.SetIcon(surf);
                        tex = renderer.CreateTexture(surf);
                        Console.WriteLine($"Created {tex}");
                        Cursor.Current = new Cursor(surf, 32, 32);
                    }

                    RendererInfo ri = renderer.Info;
                    Console.WriteLine($"Renderer {ri.Name}");
                    Console.WriteLine($"    Flags={ri.Flags}");
                    Console.WriteLine($"    MaxTextureWidth={ri.MaxTextureWidth}");
                    Console.WriteLine($"    MaxTextureHeight={ri.MaxTextureHeight}");
                    for (int i = 0; i < ri.Formats.Count; ++i)
                        Console.WriteLine($"    Formats.[{i}]={PixelFormat.GetName(ri.Formats[i])}");
                    Console.WriteLine($"    BlendMode={renderer.BlendMode}");
                    Console.WriteLine($"    Color={(System.Drawing.Color)renderer.Color}");
                    Console.WriteLine($"    Scale={renderer.Scale}");
                    Console.WriteLine($"    ClipRect={renderer.ClipRect}");
                    Console.WriteLine($"    Viewport={renderer.Viewport}");
                    Console.WriteLine($"    IntegerScale={renderer.IntegerScale}");
                    Console.WriteLine($"    OutputSize={renderer.OutputSize}");
                    Console.WriteLine($"    LogicalSize={renderer.LogicalSize}");


                    for (int i = 0; i < 1300; ++i)
                    {
                        Pump();
                        Thread.Sleep(10);
                        renderer.Color = System.Drawing.Color.CornflowerBlue;
                        renderer.Clear();
                        renderer.Copy(tex2, new Rectangle(new System.Drawing.Point(0, 0), tex2.Size));
                        renderer.Color = System.Drawing.Color.Red;
                        renderer.DrawRect(200, 100, 400, 200);
                        renderer.Copy(solidTex, new Rectangle(new System.Drawing.Point(50, 50), solidTex.Size));
                        renderer.Copy(blendedTex, new Rectangle(new System.Drawing.Point(50, 50 + lineHeight), blendedTex.Size));
                        renderer.Copy(shadedTex, new Rectangle(new System.Drawing.Point(50, 50 + lineHeight + lineHeight), shadedTex.Size));
                        renderer.Present();
                    }

                    window.Title = "100 pts!";
                    Thread.Sleep(1000);

                    for (int i = 0; i < 100; ++i)
                    {
                        Pump();
                        Thread.Sleep(100);
                        renderer.Color = System.Drawing.Color.CornflowerBlue;
                        renderer.Clear();
                        renderer.Color = System.Drawing.Color.Red;
                        int tw = tex.Width;
                        int th = tex.Height;
                        renderer.Copy(
                          tex,
                          new Rectangle(window.Size.Width / 2 - tw / 2, window.Size.Height / 2 - th / 2, tw, th)
                        );
                        renderer.DrawRect(200, 100, 400, 200);
                        renderer.Present();

                        Size sz = window.Size;
                        //Console.WriteLine($"SZ: ({sz.Width},{sz.Height})");
                        window.Size = new Size(sz.Width + 4, sz.Height);
                        sz = window.Size;
                        //Console.WriteLine($"SZ2: ({sz.Width},{sz.Height})");
                    }

                }
                finally
                {
                    if (tex != null)
                        tex.Dispose();
                    if (tex2 != null)
                        tex2.Dispose();
                    if (solidTex != null)
                        solidTex.Dispose();
                    if (blendedTex != null)
                        blendedTex.Dispose();
                    if (shadedTex != null)
                        shadedTex.Dispose();
                }
            }

            Console.WriteLine("Ok, I'm leaving");
            Mixer.Close();
            SDL.Quit();
            TTF.Quit();
            Console.WriteLine("Bye.");
        }
    }
}
