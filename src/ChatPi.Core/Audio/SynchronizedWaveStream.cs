using NAudio.Wave;

namespace ChatPi.Core.Audio;

// Decoder reads and UI seeks never mutate the source concurrently.
internal sealed class SynchronizedWaveStream(WaveStream source) : WaveStream
{
    private readonly object _gate = new();
    public override WaveFormat WaveFormat => source.WaveFormat;
    public override long Length => source.Length;
    public override long Position
    {
        get { lock (_gate) return source.Position; }
        set { lock (_gate) source.Position = value; }
    }
    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (_gate) return source.Read(buffer, offset, count);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { lock (_gate) source.Dispose(); }
        base.Dispose(disposing);
    }
}
