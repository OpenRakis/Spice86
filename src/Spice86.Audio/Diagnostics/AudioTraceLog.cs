namespace Spice86.Audio.Diagnostics;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Async isolated file-based audio trace logging.
/// Disabled by default. Enable at runtime via SetEnabled(true) or pre-enable with SPICE86_AUDIO_TRACE=1.
/// Trace file path controlled by SPICE86_AUDIO_TRACE_FILE env var (defaults to "spice86.audio.trace.log").
/// </summary>
public sealed class AudioTraceLog
{
    private static readonly AudioTraceLog _instance = new();
    private static bool _runtimeEnabled = DetermineEnabled();
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly Task _writerTask;
    private readonly AutoResetEvent _signal = new(false);
    private bool _shouldQuit;

    private AudioTraceLog()
    {
        if (!_runtimeEnabled)
        {
            _writerTask = Task.CompletedTask;
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _shouldQuit = true;
            _signal.Set();
            try
            {
                _writerTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Best effort shutdown
            }
        };

        _writerTask = WriterLoopAsync();
    }

    /// <summary>Gets whether audio tracing is enabled (default: false unless explicitly enabled).</summary>
    public static bool IsEnabled => _runtimeEnabled;

    /// <summary>Enable or disable audio tracing at runtime.</summary>
    public static void SetEnabled(bool enabled)
    {
        _runtimeEnabled = enabled;
    }

    /// <summary>Enqueue a trace message with stage name and message content.</summary>
    public static void Trace(string stage, string message)
    {
        if (!_runtimeEnabled)
        {
            return;
        }

        _instance.EnqueueTrace(stage, message);
    }

    private void EnqueueTrace(string stage, string message)
    {
        string timestamp = DateTime.UtcNow.ToString("O");
        string entry = $"{timestamp}|{Thread.CurrentThread.ManagedThreadId}|{stage}|{message}";
        _queue.Enqueue(entry);
        _signal.Set();
    }

    private async Task WriterLoopAsync()
    {
        string filePath = GetTraceFilePath();

        using (FileStream fileStream = new(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 65536, FileOptions.SequentialScan))
        {
            using (StreamWriter writer = new(fileStream, Encoding.UTF8, 65536, leaveOpen: true))
            {
                while (!_shouldQuit || !_queue.IsEmpty)
                {
                    if (_queue.IsEmpty && !_shouldQuit)
                    {
                        await Task.Run(() => _signal.WaitOne(25)).ConfigureAwait(false);
                    }

                    while (_queue.TryDequeue(out string? entry))
                    {
                        await writer.WriteLineAsync(entry).ConfigureAwait(false);
                    }

                    if (!_shouldQuit)
                    {
                        await writer.FlushAsync().ConfigureAwait(false);
                    }
                }

                await writer.FlushAsync().ConfigureAwait(false);
            }
        }
    }

    private static bool DetermineEnabled()
    {
        string? traceEnv = Environment.GetEnvironmentVariable("SPICE86_AUDIO_TRACE");
        return traceEnv == "1" || traceEnv == "true";
    }

    private static string GetTraceFilePath()
    {
        string? pathEnv = Environment.GetEnvironmentVariable("SPICE86_AUDIO_TRACE_FILE");
        return string.IsNullOrWhiteSpace(pathEnv) ? "spice86.audio.trace.log" : pathEnv;
    }
}
