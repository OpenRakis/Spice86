namespace Spice86.MicroBenchmarkTemplate;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;
using NSubstitute;
using System.Collections.Concurrent;

/// <summary>
/// Benchmark comparing ConcurrentDictionary vs Dictionary+lock for mixer channel iteration.
/// Mirrors DOSBox Staging's std::map pattern for channel storage.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class MixerChannelIterationBenchmark {
    private ConcurrentDictionary<string, MixerChannel>? _concurrentChannels;
    private Dictionary<string, MixerChannel>? _dictionaryChannels;
    private readonly object _lock = new();
    private const int ChannelCount = 8; // Typical number of active channels
    private const int IterationCount = 1000; // Simulate mixing frames

    [GlobalSetup]
    public void Setup() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        
        _concurrentChannels = new ConcurrentDictionary<string, MixerChannel>();
        _dictionaryChannels = new Dictionary<string, MixerChannel>();

        // Create typical game audio channels
        string[] channelNames = { "SB", "OPL", "CDAUDIO", "PCSPKR", "GUS", "MIDI", "TANDY", "DISNEY" };
        
        for (int i = 0; i < ChannelCount; i++) {
            string name = channelNames[i];
            MixerChannel channel = new MixerChannel(
                _ => { },
                name,
                new HashSet<ChannelFeature> { ChannelFeature.Stereo },
                logger
            );
            channel.Enable(true);
            
            _concurrentChannels[name] = channel;
            _dictionaryChannels[name] = channel;
        }
    }

    [Benchmark(Baseline = true)]
    public int ConcurrentDictionary_Iteration() {
        int frameCount = 0;
        for (int iter = 0; iter < IterationCount; iter++) {
            foreach (MixerChannel channel in _concurrentChannels!.Values) {
                if (channel.IsEnabled) {
                    frameCount++;
                }
            }
        }
        return frameCount;
    }

    [Benchmark]
    public int Dictionary_WithLock_Iteration() {
        int frameCount = 0;
        for (int iter = 0; iter < IterationCount; iter++) {
            lock (_lock) {
                foreach (MixerChannel channel in _dictionaryChannels!.Values) {
                    if (channel.IsEnabled) {
                        frameCount++;
                    }
                }
            }
        }
        return frameCount;
    }

    [Benchmark]
    public int Dictionary_CachedSnapshot_Iteration() {
        int frameCount = 0;
        MixerChannel[] snapshot;
        lock (_lock) {
            snapshot = _dictionaryChannels!.Values.ToArray();
        }
        
        for (int iter = 0; iter < IterationCount; iter++) {
            foreach (MixerChannel channel in snapshot) {
                if (channel.IsEnabled) {
                    frameCount++;
                }
            }
        }
        return frameCount;
    }

    [Benchmark]
    public void ConcurrentDictionary_AddRemove() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        
        for (int i = 0; i < 100; i++) {
            string name = $"TEMP{i}";
            MixerChannel channel = new MixerChannel(
                _ => { },
                name,
                new HashSet<ChannelFeature>(),
                logger
            );
            
            _concurrentChannels!.TryAdd(name, channel);
            _concurrentChannels.TryRemove(name, out _);
        }
    }

    [Benchmark]
    public void Dictionary_WithLock_AddRemove() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        
        for (int i = 0; i < 100; i++) {
            string name = $"TEMP{i}";
            MixerChannel channel = new MixerChannel(
                _ => { },
                name,
                new HashSet<ChannelFeature>(),
                logger
            );
            
            lock (_lock) {
                _dictionaryChannels![name] = channel;
                _dictionaryChannels.Remove(name);
            }
        }
    }
}
