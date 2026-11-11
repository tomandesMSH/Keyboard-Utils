using NAudio.Wave;

namespace KeyboardUtils;

/// <summary>
/// Tidies a microphone take (16-bit mono) before it's listened to: the silence before the first sound and after the
/// last is cut off (with a little air left), and quiet takes are made louder - up to a peak just under full scale,
/// at most 18 dB, so the room's hiss isn't blown up.
/// </summary>
static class TakeProcessing
{
    const double FrameSeconds = 0.02;
    const double LeadSeconds = 0.3, TailSeconds = 0.6;
    const double TargetPeak = 0.89;   // -1 dBFS
    const double MaxGain = 8;         // +18 dB

    /// <summary>The part of the take worth keeping (sample range), found by loudness against the take's own noise floor.</summary>
    public static (int Start, int End) Trim(ReadOnlySpan<short> samples, int sampleRate)
    {
        int frame = Math.Max(1, (int)(sampleRate * FrameSeconds));
        int frames = samples.Length / frame;
        if (frames < 3)
            return (0, samples.Length);
        var rms = new double[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            foreach (short s in samples.Slice(f * frame, frame))
                sum += (double)s * s;
            rms[f] = Math.Sqrt(sum / frame) / 32768;
        }
        // Sound = clearly above the quietest tenth of the take (the room), and above -45 dBFS anyway.
        double floor = rms.Order().ElementAt(frames / 10);
        double threshold = Math.Max(Math.Pow(10, -45 / 20.0), floor * 4);
        int first = Array.FindIndex(rms, r => r > threshold);
        int last = Array.FindLastIndex(rms, r => r > threshold);
        if (first < 0)
            return (0, samples.Length); // nothing but quiet: keep it as it is
        int start = Math.Max(0, first * frame - (int)(LeadSeconds * sampleRate));
        int end = Math.Min(samples.Length, (last + 1) * frame + (int)(TailSeconds * sampleRate));
        return (start, end);
    }

    /// <summary>How much louder the take may be made (1 = unchanged, never quieter).</summary>
    public static double Gain(ReadOnlySpan<short> samples)
    {
        int peak = 0;
        foreach (short s in samples)
            peak = Math.Max(peak, Math.Abs((int)s));
        if (peak == 0)
            return 1;
        return Math.Clamp(TargetPeak * 32768 / peak, 1, MaxGain);
    }

    /// <summary>Trims and levels the WAV at <paramref name="path"/> in place; its length afterwards in seconds.</summary>
    public static double Process(string path)
    {
        short[] samples;
        WaveFormat format;
        using (var reader = new WaveFileReader(path))
        {
            format = reader.WaveFormat;
            if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 || format.Channels != 1)
                return reader.TotalTime.TotalSeconds;
            var bytes = new byte[reader.Length];
            int read = reader.Read(bytes, 0, bytes.Length);
            samples = new short[read / 2];
            Buffer.BlockCopy(bytes, 0, samples, 0, read - read % 2);
        }
        var (start, end) = Trim(samples, format.SampleRate);
        var kept = samples.AsSpan(start, end - start);
        double gain = Gain(kept);
        var output = new byte[kept.Length * 2];
        for (int i = 0; i < kept.Length; i++)
        {
            short value = (short)Math.Clamp(Math.Round(kept[i] * gain), short.MinValue, short.MaxValue);
            output[2 * i] = (byte)value;
            output[2 * i + 1] = (byte)(value >> 8);
        }
        using (var writer = new WaveFileWriter(path, format))
            writer.Write(output, 0, output.Length);
        return (double)kept.Length / format.SampleRate;
    }

    /// <summary>
    /// Saves the take as MP3 (about a tenth of the WAV) with Windows' own encoder; where that's missing (some N
    /// editions of Windows), as WAV. Returns the file written.
    /// </summary>
    public static string SaveCompressed(string wavPath, string targetWithoutExtension)
    {
        string mp3 = targetWithoutExtension + ".mp3";
        try
        {
            using var reader = new WaveFileReader(wavPath);
            MediaFoundationEncoder.EncodeToMp3(reader, mp3, 128_000);
            return mp3;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or NotSupportedException)
        {
            try { File.Delete(mp3); } catch (IOException) { }
            string wav = targetWithoutExtension + ".wav";
            File.Copy(wavPath, wav, overwrite: true);
            return wav;
        }
    }
}
