using NAudio.Wave;
using System.Runtime.InteropServices;

namespace ChatPi.Core.Audio;

public static class DeviceFormats
{
    public static WaveFormat AsStandard(WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Extensible) return format;
        var pointer = WaveFormat.MarshalToPtr(format);
        try
        {
            return WaveFormat.MarshalFromPtr(pointer) is WaveFormatExtensible extensible
                ? extensible.ToStandardWaveFormat() : format;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    // NAudio's public extensible constructor treats 32 bits as float. Build the
    // header explicitly so integer PCM32 remains integer PCM32, with unchanged samples.
    public static WaveFormat AsExtensible(WaveFormat format)
    {
        if (format.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat))
            throw new ArgumentException("PCM or float required.", nameof(format));
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)WaveFormatEncoding.Extensible);
            writer.Write((short)format.Channels);
            writer.Write(format.SampleRate);
            writer.Write(format.AverageBytesPerSecond);
            writer.Write((short)format.BlockAlign);
            writer.Write((short)format.BitsPerSample);
            writer.Write((short)22);
            writer.Write((short)format.BitsPerSample);
            writer.Write(format.Channels == 1 ? 4 : 3); // Front-center or front L/R.
            writer.Write(new Guid(format.Encoding == WaveFormatEncoding.Pcm
                ? "00000001-0000-0010-8000-00aa00389b71"
                : "00000003-0000-0010-8000-00aa00389b71").ToByteArray());
        }
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        return WaveFormat.FromFormatChunk(reader, 40);
    }
}
