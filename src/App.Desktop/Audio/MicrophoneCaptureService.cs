using System;
using System.Collections.Generic;
using System.Linq;
using App.Models.Domain;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace App_Desktop.Audio;

internal sealed class MicrophoneCaptureService : IDisposable
{
    public const int TargetSampleRate = 16000;
    public const int TargetChannels = 1;
    public const int TargetBitsPerSample = 16;
    public const int TargetBytesPerSecond = TargetSampleRate * TargetChannels * (TargetBitsPerSample / 8);

    private readonly object _sync = new();
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private bool _isPaused;
    private readonly List<float> _sampleQueue = [];
    private double _resamplePosition;

    public event EventHandler<byte[]>? Pcm16AudioAvailable;

    public IReadOnlyList<MicrophoneDeviceInfo> GetDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;

        try
        {
            defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID;
        }
        catch
        {
            // Some systems have no communications default; the device list below is still useful.
        }

        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new MicrophoneDeviceInfo
            {
                Id = device.ID,
                Name = device.FriendlyName,
                Backend = "WASAPI",
                IsDefault = string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)
            })
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void Start(string deviceId)
    {
        Stop();

        using var enumerator = new MMDeviceEnumerator();
        _device = enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(device => string.Equals(device.ID, deviceId, StringComparison.OrdinalIgnoreCase))
            ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        _capture = new WasapiCapture(_device);
        _capture.DataAvailable += Capture_DataAvailable;
        _capture.RecordingStopped += Capture_RecordingStopped;

        lock (_sync)
        {
            _sampleQueue.Clear();
            _resamplePosition = 0;
            _isPaused = false;
        }

        _capture.StartRecording();
    }

    public void Pause()
    {
        lock (_sync)
        {
            _isPaused = true;
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            _isPaused = false;
            _sampleQueue.Clear();
            _resamplePosition = 0;
        }
    }

    public void Stop()
    {
        var capture = _capture;
        _capture = null;

        if (capture is not null)
        {
            capture.DataAvailable -= Capture_DataAvailable;
            capture.RecordingStopped -= Capture_RecordingStopped;
            try
            {
                capture.StopRecording();
            }
            catch
            {
                // Stop is best effort; disposal below releases the endpoint.
            }

            capture.Dispose();
        }

        _device?.Dispose();
        _device = null;

        lock (_sync)
        {
            _sampleQueue.Clear();
            _resamplePosition = 0;
            _isPaused = false;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        var capture = _capture;
        if (capture is null || e.BytesRecorded <= 0)
        {
            return;
        }

        byte[]? pcm;
        lock (_sync)
        {
            if (_isPaused)
            {
                return;
            }

            var monoSamples = ConvertToMonoSamples(e.Buffer, e.BytesRecorded, capture.WaveFormat);
            pcm = ResampleToPcm16(monoSamples, capture.WaveFormat.SampleRate);
        }

        if (pcm.Length > 0)
        {
            Pcm16AudioAvailable?.Invoke(this, pcm);
        }
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Pcm16AudioAvailable?.Invoke(this, []);
        }
    }

    private static List<float> ConvertToMonoSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var frameSize = bytesPerSample * channels;
        var frameCount = bytesRecorded / frameSize;
        var samples = new List<float>(frameCount);
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);

        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameOffset = frame * frameSize;
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += ReadSample(buffer, frameOffset + channel * bytesPerSample, bytesPerSample, isFloat);
            }

            samples.Add(Math.Clamp(sum / channels, -1f, 1f));
        }

        return samples;
    }

    private byte[] ResampleToPcm16(IReadOnlyList<float> monoSamples, int sourceSampleRate)
    {
        if (monoSamples.Count == 0 || sourceSampleRate <= 0)
        {
            return [];
        }

        _sampleQueue.AddRange(monoSamples);
        var ratio = (double)sourceSampleRate / TargetSampleRate;
        var pcm = new List<byte>(Math.Max(256, (int)(monoSamples.Count / ratio) * 2));

        while (_resamplePosition + 1 < _sampleQueue.Count)
        {
            var index = (int)_resamplePosition;
            var fraction = _resamplePosition - index;
            var sample = _sampleQueue[index] + (_sampleQueue[index + 1] - _sampleQueue[index]) * fraction;
            var intSample = (short)Math.Clamp((int)Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);
            pcm.Add((byte)(intSample & 0xff));
            pcm.Add((byte)((intSample >> 8) & 0xff));
            _resamplePosition += ratio;
        }

        var consumed = Math.Max(0, (int)_resamplePosition - 1);
        if (consumed > 0)
        {
            _sampleQueue.RemoveRange(0, consumed);
            _resamplePosition -= consumed;
        }

        return pcm.ToArray();
    }

    private static float ReadSample(byte[] buffer, int offset, int bytesPerSample, bool isFloat)
    {
        if (isFloat && bytesPerSample >= 4)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(buffer, offset) / 32768f,
            3 => ReadInt24(buffer, offset) / 8388608f,
            4 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0f
        };
    }

    private static int ReadInt24(byte[] buffer, int offset)
    {
        var sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        if ((sample & 0x800000) != 0)
        {
            sample |= unchecked((int)0xff000000);
        }

        return sample;
    }
}
