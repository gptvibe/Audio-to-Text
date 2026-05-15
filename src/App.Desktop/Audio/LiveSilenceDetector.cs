using System;

namespace App_Desktop.Audio;

internal static class LiveSilenceDetector
{
    public static bool ContainsSpeech(byte[] pcm16, double rmsThreshold = 0.012, double peakThreshold = 0.035)
    {
        if (pcm16.Length < 2)
        {
            return false;
        }

        double sumSquares = 0;
        double peak = 0;
        var sampleCount = pcm16.Length / 2;

        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt16(pcm16, i * 2) / 32768.0;
            var absolute = Math.Abs(sample);
            peak = Math.Max(peak, absolute);
            sumSquares += sample * sample;
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        return rms >= rmsThreshold || peak >= peakThreshold;
    }
}
