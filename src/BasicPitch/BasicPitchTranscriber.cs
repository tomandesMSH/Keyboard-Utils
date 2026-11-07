using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BasicPitch;

/// <summary>A detected note: MIDI pitch, start/end in seconds, and amplitude 0-1 (scale by 127 for velocity).</summary>
public readonly record struct BasicPitchNote(int Pitch, double StartSeconds, double EndSeconds, double Amplitude);

/// <summary>
/// Polyphonic audio-to-notes with Spotify's Basic Pitch model (https://github.com/spotify/basic-pitch, Apache-2.0),
/// run through ONNX Runtime. A C# port of basic_pitch/inference.py (windowing) and note_creation.py
/// (output_to_notes_polyphonic) with their default settings; pitch bends are not computed.
/// </summary>
public sealed class BasicPitchTranscriber : IDisposable
{
    /// <summary>The model's input rate. Audio must be mono at this rate.</summary>
    public const int SampleRate = 22050;

    // constants.py
    const int FftHop = 256;
    const int AudioNSamples = SampleRate * 2 - FftHop;      // 43844: one 2 s window
    const int AnnotNFrames = 172;                            // model frames per window (86 fps * 2 s)
    const int AnnotationsFps = SampleRate / FftHop;          // 86
    const int NOverlappingFrames = 30;
    const int OverlapLen = NOverlappingFrames * FftHop;      // 7680
    const int HopSize = AudioNSamples - OverlapLen;          // 36164
    const int MidiOffset = 21;                               // bin 0 = A0
    const int MaxFreqIdx = 87;

    // inference.py defaults
    const float OnsetThreshold = 0.5f;
    const float FrameThreshold = 0.3f;
    const double MinimumNoteLengthMs = 127.7;
    const int EnergyTolerance = 11;

    readonly InferenceSession session;
    readonly string inputName;

    /// <param name="modelPath">nmp.onnx from basic_pitch/saved_models/icassp_2022.</param>
    public BasicPitchTranscriber(string modelPath)
    {
        session = new InferenceSession(modelPath);
        inputName = session.InputMetadata.Keys.First();
    }

    /// <summary>Notes in <paramref name="audio"/> (mono, <see cref="SampleRate"/> Hz, -1..1). <paramref name="progress"/> gets 0-1.</summary>
    public List<BasicPitchNote> Transcribe(float[] audio, IProgress<double>? progress, CancellationToken cancel)
    {
        var (note, onset) = Infer(audio, progress, cancel);
        int minNoteLen = (int)Math.Round(MinimumNoteLengthMs / 1000 * ((double)SampleRate / FftHop));
        var events = OutputToNotesPolyphonic(note, onset, minNoteLen);
        return events
            .Select(e => new BasicPitchNote(e.Pitch, FrameToTime(e.Start), FrameToTime(e.End), e.Amplitude))
            .OrderBy(n => n.StartSeconds).ThenBy(n => n.Pitch)
            .ToList();
    }

    /// <summary>
    /// inference.py run_inference: pad with half an overlap in front, cut into overlapping 2 s windows, run the model
    /// on each, drop the overlapping frames at both ends of every window and stitch the rest (unwrap_output).
    /// </summary>
    (float[,] Note, float[,] Onset) Infer(float[] audio, IProgress<double>? progress, CancellationToken cancel)
    {
        int originalLength = audio.Length;
        var padded = new float[OverlapLen / 2 + audio.Length];
        Array.Copy(audio, 0, padded, OverlapLen / 2, audio.Length);

        int windows = Math.Max(1, (padded.Length + HopSize - 1) / HopSize);
        int trim = NOverlappingFrames / 2;
        int keptPerWindow = AnnotNFrames - 2 * trim;
        int totalFrames = (int)Math.Floor(originalLength * ((double)AnnotationsFps / SampleRate));
        var note = new float[totalFrames, 88];
        var onset = new float[totalFrames, 88];

        var input = new DenseTensor<float>([1, AudioNSamples, 1]);
        for (int w = 0; w < windows; w++)
        {
            cancel.ThrowIfCancellationRequested();
            int start = w * HopSize;
            for (int i = 0; i < AudioNSamples; i++)
                input[0, i, 0] = start + i < padded.Length ? padded[start + i] : 0;

            using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, input)]);
            var (noteOut, onsetOut) = PickOutputs(results);
            for (int f = 0; f < keptPerWindow; f++)
            {
                int target = w * keptPerWindow + f;
                if (target >= totalFrames)
                    break;
                for (int b = 0; b < 88; b++)
                {
                    note[target, b] = noteOut[0, trim + f, b];
                    onset[target, b] = onsetOut[0, trim + f, b];
                }
            }
            progress?.Report((w + 1) / (double)windows);
        }
        return (note, onset);
    }

    /// <summary>
    /// The model has three outputs: contour (264 bins, unused here), note and onset (88 bins each). inference.py
    /// maps StatefulPartitionedCall:1 to note and :2 to onset.
    /// </summary>
    static (Tensor<float> Note, Tensor<float> Onset) PickOutputs(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        var byName = results.ToDictionary(r => r.Name, r => r.AsTensor<float>());
        if (byName.TryGetValue("StatefulPartitionedCall:1", out var note) && byName.TryGetValue("StatefulPartitionedCall:2", out var onset))
            return (note, onset);
        throw new InvalidOperationException("Unexpected Basic Pitch model outputs: " + string.Join(", ", byName.Keys));
    }

    readonly record struct NoteEvent(int Start, int End, int Pitch, double Amplitude);

    /// <summary>note_creation.py output_to_notes_polyphonic (infer_onsets and melodia_trick on, no frequency limits).</summary>
    static List<NoteEvent> OutputToNotesPolyphonic(float[,] frames, float[,] onsets, int minNoteLen)
    {
        int nFrames = frames.GetLength(0);
        var events = new List<NoteEvent>();
        if (nFrames < 3)
            return events;

        onsets = InferredOnsets(onsets, frames);

        // Onset peaks (local maxima in time, argrelmax: strictly greater than both neighbours, edges excluded) above
        // the threshold, processed from the latest to the earliest.
        var peaks = new List<(int Time, int Bin)>();
        for (int t = 1; t < nFrames - 1; t++)
            for (int b = 0; b < 88; b++)
                if (onsets[t, b] > onsets[t - 1, b] && onsets[t, b] > onsets[t + 1, b] && onsets[t, b] >= OnsetThreshold)
                    peaks.Add((t, b));
        peaks.Reverse();

        var remaining = (float[,])frames.Clone();
        foreach (var (start, bin) in peaks)
        {
            if (start >= nFrames - 1)
                continue;
            // Follow the note while its frame energy stays up, tolerating short dips.
            int i = start + 1, k = 0;
            while (i < nFrames - 1 && k < EnergyTolerance)
            {
                k = remaining[i, bin] < FrameThreshold ? k + 1 : 0;
                i++;
            }
            i -= k;
            if (i - start <= minNoteLen)
                continue;
            ClearBand(remaining, start, i, bin);
            events.Add(new NoteEvent(start, i, bin + MidiOffset, Mean(frames, start, i, bin)));
        }

        // "Melodia trick": notes with no detected onset - grow from the strongest leftover energy both ways.
        while (Max(remaining, out int mid, out int midBin) > FrameThreshold)
        {
            remaining[mid, midBin] = 0;
            int i = mid + 1, k = 0;
            while (i < nFrames - 1 && k < EnergyTolerance)
            {
                k = remaining[i, midBin] < FrameThreshold ? k + 1 : 0;
                ClearBand(remaining, i, i + 1, midBin);
                i++;
            }
            int end = i - 1 - k;

            i = mid - 1;
            k = 0;
            while (i > 0 && k < EnergyTolerance)
            {
                k = remaining[i, midBin] < FrameThreshold ? k + 1 : 0;
                ClearBand(remaining, i, i + 1, midBin);
                i--;
            }
            int start = i + 1 + k;

            if (end - start <= minNoteLen)
                continue;
            events.Add(new NoteEvent(start, end, midBin + MidiOffset, Mean(frames, start, end, midBin)));
        }
        return events;
    }

    /// <summary>
    /// note_creation.py get_infered_onsets: rises in the frame activations (min over lags 1-2, negatives clipped),
    /// rescaled to the onsets' maximum and max-combined with the predicted onsets.
    /// </summary>
    static float[,] InferredOnsets(float[,] onsets, float[,] frames)
    {
        int nFrames = frames.GetLength(0);
        var diff = new float[nFrames, 88];
        float maxDiff = 0, maxOnset = 0;
        for (int t = 0; t < nFrames; t++)
            for (int b = 0; b < 88; b++)
            {
                maxOnset = Math.Max(maxOnset, onsets[t, b]);
                if (t < 2)
                    continue; // the first n_diff rows are zeroed
                float d1 = frames[t, b] - frames[t - 1, b];
                float d2 = frames[t, b] - frames[t - 2, b];
                float d = Math.Max(0, Math.Min(d1, d2));
                diff[t, b] = d;
                maxDiff = Math.Max(maxDiff, d);
            }
        var combined = new float[nFrames, 88];
        for (int t = 0; t < nFrames; t++)
            for (int b = 0; b < 88; b++)
                combined[t, b] = Math.Max(onsets[t, b], maxDiff > 0 ? maxOnset * diff[t, b] / maxDiff : 0);
        return combined;
    }

    /// <summary>Zero frames [from, to) in the bin and its two neighbours (a note's energy smears into them).</summary>
    static void ClearBand(float[,] remaining, int from, int to, int bin)
    {
        for (int t = from; t < to; t++)
        {
            remaining[t, bin] = 0;
            if (bin < MaxFreqIdx)
                remaining[t, bin + 1] = 0;
            if (bin > 0)
                remaining[t, bin - 1] = 0;
        }
    }

    static double Mean(float[,] frames, int from, int to, int bin)
    {
        double sum = 0;
        for (int t = from; t < to; t++)
            sum += frames[t, bin];
        return to > from ? sum / (to - from) : 0;
    }

    static float Max(float[,] values, out int time, out int bin)
    {
        float best = float.MinValue;
        time = bin = 0;
        for (int t = 0; t < values.GetLength(0); t++)
            for (int b = 0; b < 88; b++)
                if (values[t, b] > best)
                {
                    best = values[t, b];
                    time = t;
                    bin = b;
                }
        return best;
    }

    /// <summary>
    /// inference.py model_frames_to_time: frame times, minus a small per-window correction (the window isn't an
    /// exact number of frames long).
    /// </summary>
    static double FrameToTime(int frame)
    {
        double original = frame * (double)FftHop / SampleRate;
        int windowNumber = frame / AnnotNFrames;
        double windowOffset = (double)FftHop / SampleRate * (AnnotNFrames - AudioNSamples / (double)FftHop) + 0.0018;
        return original - windowOffset * windowNumber;
    }

    public void Dispose() => session.Dispose();
}
