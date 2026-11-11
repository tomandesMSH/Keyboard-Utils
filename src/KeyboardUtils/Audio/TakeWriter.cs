using NAudio.Wave;

namespace KeyboardUtils;

/// <summary>
/// Writes captured audio to a WAV file while it's being analysed (the player's take in learning mode). Thread-safe:
/// capture threads write, the UI thread disposes. Stops writing (quietly) once <c>max</c> is reached.
/// </summary>
sealed class TakeWriter : IDisposable
{
    readonly object gate = new();
    readonly long maxBytes;
    WaveFileWriter? writer;

    public TakeWriter(string path, WaveFormat format, TimeSpan max)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        writer = new WaveFileWriter(path, format);
        maxBytes = (long)(format.AverageBytesPerSecond * max.TotalSeconds) / format.BlockAlign * format.BlockAlign;
    }

    public string Path { get; }

    /// <summary>Seconds written so far.</summary>
    public double Seconds
    {
        get
        {
            lock (gate)
                return writer == null ? 0 : (double)writer.Length / writer.WaveFormat.AverageBytesPerSecond;
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        lock (gate)
        {
            if (writer == null)
                return;
            long room = maxBytes - writer.Length;
            if (room <= 0)
                return;
            writer.Write(data[..(int)Math.Min(data.Length, room)]);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            writer?.Dispose();
            writer = null;
        }
    }
}
