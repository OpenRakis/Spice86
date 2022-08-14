using System;
using Bufdio.Exceptions;
using Bufdio.Utilities;
using Bufdio.Utilities.Extensions;
using FFmpeg.AutoGen;

namespace Bufdio.Decoders.FFmpeg;

internal sealed unsafe class FFmpegResampler : IDisposable
{
    private const int LogOffset = 0;
    private readonly SwrContext* _swrCtx;
    private readonly AVFrame* _dstFrame;
    private readonly long _dstChannelLayout;
    private readonly int _dstChannels;
    private readonly int _dstSampleRate;
    private readonly int _bytesPerSample;
    private bool _disposed;

    public FFmpegResampler(
        long srcChannelLayout,
        int srcSampleRate,
        AVSampleFormat srcSampleFormat,
        int dstChannels,
        int dstSampleRate)
    {
        _dstChannels = dstChannels;
        _dstSampleRate = dstSampleRate;

        _dstChannelLayout = ffmpeg.av_get_default_channel_layout(_dstChannels);
        _bytesPerSample = ffmpeg.av_get_bytes_per_sample(BufdioLib.Constants.FFmpegSampleFormat);

        _swrCtx = ffmpeg.swr_alloc_set_opts(
            null,
            _dstChannelLayout,
            BufdioLib.Constants.FFmpegSampleFormat,
            _dstSampleRate,
            srcChannelLayout,
            srcSampleFormat,
            srcSampleRate,
            LogOffset,
            null);

        Ensure.That<FFmpegException>(_swrCtx != null, "FFmpeg - Unable to allocate swr context.");
        ffmpeg.swr_init(_swrCtx).FFGuard();

        _dstFrame = ffmpeg.av_frame_alloc();
    }

    public bool TryConvert(AVFrame source, out byte[] result, out string error)
    {
        ffmpeg.av_frame_unref(_dstFrame);

        _dstFrame->channels = _dstChannels;
        _dstFrame->sample_rate = _dstSampleRate;
        _dstFrame->channel_layout = (ulong)_dstChannelLayout;
        _dstFrame->format = (int)BufdioLib.Constants.FFmpegSampleFormat;

        var code = ffmpeg.swr_convert_frame(_swrCtx, _dstFrame, &source);

        if (code.FFIsError())
        {
            result = null;
            error = code.FFErrorToText();

            return false;
        }

        var size = _dstFrame->nb_samples * _bytesPerSample * _dstFrame->channels;
        var data = new byte[size];

        try
        {
            fixed (byte* h = &data[0])
            {
                Buffer.MemoryCopy(_dstFrame->data[0], h, size, size);
            }
        }
        catch (Exception ex)
        {
            result = null;
            error = ex.Message;

            return false;
        }

        result = data;
        error = null;

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var dstFrame = _dstFrame;
        ffmpeg.av_frame_free(&dstFrame);

        var swrCtx = _swrCtx;
        ffmpeg.swr_free(&swrCtx);

        _disposed = true;
    }
}
