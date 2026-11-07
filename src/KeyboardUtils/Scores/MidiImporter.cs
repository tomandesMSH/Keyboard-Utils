using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace KeyboardUtils;

static class MidiImporter
{
    public static Score Import(string path)
    {
        var midiFile = MidiFile.Read(path);
        var tempoMap = midiFile.GetTempoMap();
        var score = new Score
        {
            Title = Path.GetFileNameWithoutExtension(path),
            Timing = ReadTiming(midiFile, tempoMap),
            KeyFifths = ReadKey(midiFile),
        };

        foreach (var trackChunk in midiFile.GetTrackChunks())
        {
            var notes = trackChunk.GetNotes().ToList();
            if (notes.Count == 0)
                continue;

            var name = trackChunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text;
            var part = new ScorePart
            {
                Name = string.IsNullOrWhiteSpace(name) ? T($"Část {score.Parts.Count + 1}", $"Part {score.Parts.Count + 1}") : name,
            };

            foreach (var note in notes)
            {
                double start = note.TimeAs<MetricTimeSpan>(tempoMap).TotalSeconds;
                double duration = note.LengthAs<MetricTimeSpan>(tempoMap).TotalSeconds;
                part.Notes.Add(new ScoreNote
                {
                    Pitch = note.NoteNumber,
                    StartSeconds = start,
                    DurationSeconds = duration,
                    Velocity = note.Velocity,
                });
            }
            score.Parts.Add(part);
        }

        return score;
    }

    /// <summary>
    /// The opening key signature. "C major" is treated as unknown: many programs write it whatever the music is,
    /// and the guess from the notes comes out as C anyway when the music really is in C.
    /// </summary>
    static int? ReadKey(MidiFile midiFile)
    {
        var key = midiFile.GetTrackChunks()
            .SelectMany(chunk => chunk.GetTimedEvents())
            .Where(e => e.Event is KeySignatureEvent)
            .OrderBy(e => e.Time)
            .Select(e => (int)((KeySignatureEvent)e.Event).Key)
            .FirstOrDefault();
        return key is not 0 and >= -7 and <= 7 ? key : null;
    }

    /// <summary>Tempo changes and the opening time signature; null for SMPTE-timed files, which have no beats.</summary>
    static ScoreTiming? ReadTiming(MidiFile midiFile, TempoMap tempoMap)
    {
        if (midiFile.TimeDivision is not TicksPerQuarterNoteTimeDivision division)
            return null;
        double ticksPerQuarter = division.TicksPerQuarterNote;
        var tempo = new List<TempoPoint>();
        void Add(long time, Tempo value) => tempo.Add(new TempoPoint(
            TimeConverter.ConvertTo<MetricTimeSpan>(time, tempoMap).TotalSeconds,
            time / ticksPerQuarter,
            1_000_000.0 / value.MicrosecondsPerQuarterNote));

        Add(0, tempoMap.GetTempoAtTime(new MidiTimeSpan(0)));
        foreach (var change in tempoMap.GetTempoChanges().Where(c => c.Time > 0))
            Add(change.Time, change.Value);

        var signature = tempoMap.GetTimeSignatureAtTime(new MidiTimeSpan(0));
        return new ScoreTiming(tempo, signature.Numerator, signature.Denominator);
    }
}
