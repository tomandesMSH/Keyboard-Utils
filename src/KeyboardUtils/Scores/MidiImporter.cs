using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace KeyboardUtils;

static class MidiImporter
{
    public static Score Import(string path)
    {
        var midiFile = MidiFile.Read(path);
        var tempoMap = midiFile.GetTempoMap();
        var score = new Score { Title = Path.GetFileNameWithoutExtension(path) };

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
}
