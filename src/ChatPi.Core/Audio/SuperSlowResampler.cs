namespace ChatPi.Core.Audio;

/// <summary>
/// First-order hold, equivalent to a triangular FIR at the output rate:
/// h[k] = 1 - |k| / factor for |k| &lt; factor. One input frame of lookahead
/// aligns output to the source timeline. Convex weights add no overshoot or
/// oscillatory ringing. This deliberately trades image rejection for a slow rolloff.
/// </summary>
public sealed class SuperSlowResampler : IPcmResampler
{
    private readonly int _factor, _channels;
    private double[] _left, _right;
    private int _phase;
    private bool _hasLeft, _hasRight, _ending, _finalInterval, _finished, _disposed;

    public SuperSlowResampler(int factor, int channels)
    {
        if (factor is not (2 or 4)) throw new ArgumentOutOfRangeException(nameof(factor));
        if (channels is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channels));
        _factor = factor;
        _channels = channels;
        _left = new double[channels];
        _right = new double[channels];
    }

    public int Process(double[] input, int offsetFrames, int frames, double[] output, out int consumed, bool end = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offsetFrames);
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        if (input.Length % _channels != 0 || output.Length % _channels != 0 ||
            offsetFrames > input.Length / _channels - frames)
            throw new ArgumentException("Invalid interleaved frame buffer.");
        if (end && frames != 0) throw new ArgumentException("Drain requires empty input.");
        if ((_ending || _finished) && frames != 0) throw new InvalidOperationException("Input has already ended.");
        if (end) _ending = true;
        var used = 0;
        var written = 0;
        var capacity = output.Length / _channels;
        while (!_finished && written < capacity)
        {
            if (!_hasLeft)
            {
                if (used == frames) break;
                ReadFrame(_left);
                _hasLeft = true;
            }
            if (!_hasRight)
            {
                if (used < frames) ReadFrame(_right);
                else if (_ending)
                {
                    // Hold the last sample for the final interval; no zero-padding transient.
                    _left.CopyTo(_right, 0);
                    _finalInterval = true;
                }
                else break;
                _hasRight = true;
            }
            var mix = _phase / (double)_factor;
            for (var channel = 0; channel < _channels; channel++)
                output[written * _channels + channel] = _phase == 0 || _finalInterval
                    ? _left[channel] : _left[channel] * (1 - mix) + _right[channel] * mix;
            written++;
            if (++_phase == _factor)
            {
                _phase = 0;
                _hasRight = false;
                if (_finalInterval) { _hasLeft = false; _finished = true; }
                else (_left, _right) = (_right, _left);
            }
        }
        if (_ending && !_hasLeft) _finished = true;
        consumed = used;
        return written;

        void ReadFrame(double[] destination)
        {
            input.AsSpan((offsetFrames + used) * _channels, _channels).CopyTo(destination);
            used++;
        }
    }

    public void Dispose() => _disposed = true;
}
