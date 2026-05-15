using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace App_Desktop.Audio;

internal static class WaveChunkWriter
{
    public static async Task WritePcm16MonoAsync(string path, byte[] pcm16, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await WriteAsciiAsync(stream, "RIFF", cancellationToken);
        await WriteInt32Async(stream, 36 + pcm16.Length, cancellationToken);
        await WriteAsciiAsync(stream, "WAVE", cancellationToken);
        await WriteAsciiAsync(stream, "fmt ", cancellationToken);
        await WriteInt32Async(stream, 16, cancellationToken);
        await WriteInt16Async(stream, 1, cancellationToken);
        await WriteInt16Async(stream, MicrophoneCaptureService.TargetChannels, cancellationToken);
        await WriteInt32Async(stream, MicrophoneCaptureService.TargetSampleRate, cancellationToken);
        await WriteInt32Async(stream, MicrophoneCaptureService.TargetBytesPerSecond, cancellationToken);
        await WriteInt16Async(stream, MicrophoneCaptureService.TargetChannels * (MicrophoneCaptureService.TargetBitsPerSample / 8), cancellationToken);
        await WriteInt16Async(stream, MicrophoneCaptureService.TargetBitsPerSample, cancellationToken);
        await WriteAsciiAsync(stream, "data", cancellationToken);
        await WriteInt32Async(stream, pcm16.Length, cancellationToken);
        await stream.WriteAsync(pcm16, cancellationToken);
    }

    private static Task WriteAsciiAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        return stream.WriteAsync(Encoding.ASCII.GetBytes(value), cancellationToken).AsTask();
    }

    private static Task WriteInt16Async(Stream stream, int value, CancellationToken cancellationToken)
    {
        return stream.WriteAsync(BitConverter.GetBytes((short)value), cancellationToken).AsTask();
    }

    private static Task WriteInt32Async(Stream stream, int value, CancellationToken cancellationToken)
    {
        return stream.WriteAsync(BitConverter.GetBytes(value), cancellationToken).AsTask();
    }
}
