using System;
using System.IO;
using System.Runtime.InteropServices;
using Bufdio.Exceptions;
using Bufdio.Utilities;
using Bufdio.Utilities.Extensions;
using FFmpeg.AutoGen;

namespace Bufdio.Decoders.FFmpeg;

/// <summary>
/// A class that uses FFmpeg for decoding and demuxing specified audio source.
/// This class cannot be inherited.
/// <para>Implements: <see cref="IAudioDecoder"/>.</para>
/// </summary>
public sealed unsafe class FFmpegDecoder : IAudioDecoder
{
    private const int StreamBufferSize = 4096;
    private const AVMediaType MediaType = AVMediaType.AVMEDIA_TYPE_AUDIO;
    private readonly object _syncLock = new object();
    private readonly AVFormatContext* _formatCtx;
    private readonly AVCodecContext* _codecCtx;
    private readonly AVPacket* _currentPacket;
    private readonly AVFrame* _currentFrame;
    private readonly FFmpegResampler _resampler;
    private avio_alloc_context_read_packet _reads;
    private avio_alloc_context_seek _seeks;
    private readonly int _streamIndex;
    private readonly Stream _inputStream;
    private readonly byte[] _inputStreamBuffer;
    private bool _disposed;

    private FFmpegDecoder(string url, Stream stream, FFmpegDecoderOptions options, bool useUrl = false)
    {
        if (useUrl)
        {
            Ensure.NotNull(url, nameof(url));
        }
        else
        {
            Ensure.NotNull(stream, nameof(stream));
        }

        _formatCtx = ffmpeg.avformat_alloc_context();

        if (!useUrl)
        {
            _reads = ReadsImpl;
            _seeks = SeeksImpl;

            _inputStream = stream;
            _inputStreamBuffer = new byte[StreamBufferSize];

            var buffer = (byte*)ffmpeg.av_malloc(StreamBufferSize);
            var avio = ffmpeg.avio_alloc_context(buffer, StreamBufferSize, 0, null, _reads, null, _seeks);

            Ensure.That<FFmpegException>(avio != null, "FFmpeg - Unable to allocate avio context.");

            _formatCtx->pb = avio;
        }

        // Open and read operations (like av_read_frame) are blocked by default.
        // We need to set http, udp and rstp read timeout, in case connection interrupted.
        AVDictionary* dict = null;
        ffmpeg.av_dict_set_int(&dict, "stimeout", 10, 0);
        ffmpeg.av_dict_set_int(&dict, "timeout", 10, 0);

        var formatCtx = _formatCtx;
        ffmpeg.avformat_open_input(&formatCtx, useUrl ? url : null, null, &dict).FFGuard();
        ffmpeg.av_dict_free(&dict);

        ffmpeg.avformat_find_stream_info(_formatCtx, null).FFGuard();

        AVCodec* codec = null;
        _streamIndex = ffmpeg.av_find_best_stream(_formatCtx, MediaType, -1, -1, &codec, 0).FFGuard();

        // The given source can be a video or contains multiple streams.
        // Since we will only work with audio stream, let's discard other streams.
        for (var i = 0; i < _formatCtx->nb_streams; i++)
        {
            if (i != _streamIndex)
            {
                _formatCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
            }
        }

        _codecCtx = ffmpeg.avcodec_alloc_context3(codec);

        ffmpeg.avcodec_parameters_to_context(_codecCtx, _formatCtx->streams[_streamIndex]->codecpar).FFGuard();
        ffmpeg.avcodec_open2(_codecCtx, codec, null).FFGuard();

        options ??= new FFmpegDecoderOptions();

        var channelLayout = _codecCtx->channel_layout <= 0
            ? ffmpeg.av_get_default_channel_layout(_codecCtx->channels)
            : (long)_codecCtx->channel_layout;

        _resampler = new FFmpegResampler(
            channelLayout,
            _codecCtx->sample_rate,
            _codecCtx->sample_fmt,
            options.Channels,
            options.SampleRate);

        var rational = ffmpeg.av_q2d(_formatCtx->streams[_streamIndex]->time_base);
        var duration = _formatCtx->streams[_streamIndex]->duration * rational * 1000.00;
        duration = duration > 0 ? duration : _formatCtx->duration / 1000.00;

        StreamInfo = new AudioStreamInfo(_codecCtx->channels, _codecCtx->sample_rate, duration.Milliseconds());

        _currentPacket = ffmpeg.av_packet_alloc();
        _currentFrame = ffmpeg.av_frame_alloc();
    }

    /// <summary>
    /// Initializes <see cref="FFmpegDecoder"/> by providing audio URL.
    /// The audio URL can be URL or path to local audio file.
    /// </summary>
    /// <param name="url">Audio URL or audio file path to decode.</param>
    /// <param name="options">An optional FFmpeg decoder options.</param>
    /// <exception cref="ArgumentNullException">Thrown when the given url is <c>null</c>.</exception>
    /// <exception cref="FFmpegException">Thrown when errors occured during setups.</exception>
    public FFmpegDecoder(string url, FFmpegDecoderOptions options = default) : this(url, null, options, true)
    {
    }

    /// <summary>
    /// Initializes <see cref="FFmpegDecoder"/> by providing source audio stream.
    /// </summary>
    /// <param name="stream">Source of audio stream to decode.</param>
    /// <param name="options">An optional FFmpeg decoder options.</param>
    /// <exception cref="ArgumentNullException">Thrown when the given stream is <c>null</c>.</exception>
    /// <exception cref="FFmpegException">Thrown when errors occured during setups.</exception>
    public FFmpegDecoder(Stream stream, FFmpegDecoderOptions options = default) : this(null, stream, options)
    {
    }

    /// <inheritdoc />
    public AudioStreamInfo StreamInfo { get; }

    /// <inheritdoc />
    public AudioDecoderResult DecodeNextFrame()
    {
        lock (_syncLock)
        {
            ffmpeg.av_frame_unref(_currentFrame);

            while (true)
            {
                int code;

                do
                {
                    ffmpeg.av_packet_unref(_currentPacket);
                    code = ffmpeg.av_read_frame(_formatCtx, _currentPacket);

                    // This might be end-of-file error
                    if (code.FFIsError())
                    {
                        ffmpeg.av_packet_unref(_currentPacket);
                        return new AudioDecoderResult(null, false, code.FFIsEOF(), code.FFErrorToText());
                    }

                } while (_currentPacket->stream_index != _streamIndex);

                ffmpeg.avcodec_send_packet(_codecCtx, _currentPacket);
                ffmpeg.av_packet_unref(_currentPacket);

                code = ffmpeg.avcodec_receive_frame(_codecCtx, _currentFrame);

                // Break if all inputs was received
                if (code != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    break;
                }
            }

            // Handle unknown channel layout so the resampler can process the frame
            if (_currentFrame->channel_layout <= 0)
            {
                var channelLayout = (ulong)ffmpeg.av_get_default_channel_layout(_codecCtx->channels);
                _currentFrame->channel_layout = channelLayout;
            }

            // Converts samples from received frame using resampler
            if (!_resampler.TryConvert(*_currentFrame, out var data, out var error))
            {
                return new AudioDecoderResult(null, false, false, error);
            }

            // Retrieve the best or most accurate presentation timestamp
            var pts = _currentFrame->best_effort_timestamp;
            pts = pts >= 0 ? pts : _currentFrame->pts;
            pts = pts >= 0 ? pts : 0;

            // Calculate FFmpeg's presentation timestamp in milliseconds value
            var rational = ffmpeg.av_q2d(_formatCtx->streams[_streamIndex]->time_base);
            var presentationTime = Math.Round(pts * rational * 1000.0, 2);

            return new AudioDecoderResult(new AudioFrame(presentationTime, data), true, false);
        }
    }

    /// <inheritdoc />
    public bool TrySeek(TimeSpan position, out string error)
    {
        lock (_syncLock)
        {
            var tb = _formatCtx->streams[_streamIndex]->time_base;
            var pos = (long)(position.TotalSeconds * ffmpeg.AV_TIME_BASE);
            var ts = ffmpeg.av_rescale_q(pos, ffmpeg.av_get_time_base_q(), tb);

            var code = ffmpeg.avformat_seek_file(_formatCtx, _streamIndex, 0, ts, long.MaxValue, 0);
            ffmpeg.avcodec_flush_buffers(_codecCtx);

            error = code.FFIsError() ? code.FFErrorToText() : null;
            return !code.FFIsError();
        }
    }

    private int ReadsImpl(void* opaque, byte* buf, int buf_size)
    {
        buf_size = Math.Min(buf_size, StreamBufferSize);
        var length = _inputStream.Read(_inputStreamBuffer, 0, buf_size);
        Marshal.Copy(_inputStreamBuffer, 0, (IntPtr)buf, length);

        return length;
    }

    private long SeeksImpl(void* opaque, long offset, int whence)
    {
        return whence switch
        {
            ffmpeg.AVSEEK_SIZE => _inputStream.Length,
            < 3 => _inputStream.Seek(offset, (SeekOrigin)whence),
            _ => -1
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var packet = _currentPacket;
        ffmpeg.av_packet_free(&packet);

        var frame = _currentFrame;
        ffmpeg.av_frame_free(&frame);

        var formatCtx = _formatCtx;
        if (_inputStream != null)
        {
            ffmpeg.av_freep(&formatCtx->pb->buffer);
            ffmpeg.avio_context_free(&formatCtx->pb);
        }

        ffmpeg.avformat_close_input(&formatCtx);
        ffmpeg.avcodec_close(_codecCtx);

        _resampler?.Dispose();
        _reads = null;
        _seeks = null;
        _disposed = true;
    }
}
