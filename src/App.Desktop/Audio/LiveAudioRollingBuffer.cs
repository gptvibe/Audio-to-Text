using System;
using System.Collections.Generic;
using System.Linq;

namespace App_Desktop.Audio;

internal sealed class LiveAudioRollingBuffer
{
    private readonly object _sync = new();
    private readonly int _bytesPerSecond;
    private readonly int _maxBytes;
    private readonly List<byte> _buffer = [];
    private long _totalBytes;

    public LiveAudioRollingBuffer(int bytesPerSecond, TimeSpan maxDuration)
    {
        _bytesPerSecond = bytesPerSecond;
        _maxBytes = Math.Max(bytesPerSecond, (int)(bytesPerSecond * maxDuration.TotalSeconds));
    }

    public TimeSpan TotalDuration
    {
        get
        {
            lock (_sync)
            {
                return TimeSpan.FromSeconds((double)_totalBytes / _bytesPerSecond);
            }
        }
    }

    public void Append(byte[] pcm16)
    {
        if (pcm16.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            _buffer.AddRange(pcm16);
            _totalBytes += pcm16.Length;

            if (_buffer.Count > _maxBytes)
            {
                _buffer.RemoveRange(0, _buffer.Count - _maxBytes);
            }
        }
    }

    public bool TryCreateWindow(TimeSpan duration, out byte[] pcm16, out TimeSpan start, out TimeSpan actualDuration)
    {
        lock (_sync)
        {
            if (_buffer.Count == 0)
            {
                pcm16 = [];
                start = TimeSpan.Zero;
                actualDuration = TimeSpan.Zero;
                return false;
            }

            var desiredBytes = Math.Min(_buffer.Count, Math.Max(0, (int)(duration.TotalSeconds * _bytesPerSecond)));
            var bufferStartByte = _totalBytes - _buffer.Count;
            var startByte = Math.Max(bufferStartByte, _totalBytes - desiredBytes);
            var offset = (int)(startByte - bufferStartByte);
            var count = _buffer.Count - offset;

            pcm16 = _buffer.Skip(offset).Take(count).ToArray();
            start = TimeSpan.FromSeconds((double)startByte / _bytesPerSecond);
            actualDuration = TimeSpan.FromSeconds((double)count / _bytesPerSecond);
            return pcm16.Length > 0;
        }
    }

    public bool TryCreateRecent(TimeSpan duration, out byte[] pcm16)
    {
        return TryCreateWindow(duration, out pcm16, out _, out _);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _buffer.Clear();
            _totalBytes = 0;
        }
    }
}
