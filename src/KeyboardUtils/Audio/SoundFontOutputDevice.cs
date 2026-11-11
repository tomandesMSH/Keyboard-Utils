using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using MeltySynth;
using NAudio.Wave;

namespace KeyboardUtils;

/// <summary>
/// A MIDI output that plays through a SoundFont (the piano sound pack, <see cref="PianoSoundModule"/>) instead of
/// Windows' GS Wavetable synth: MIDI events go to MeltySynth, its sound to the speakers. Channel 10 - the metronome's
/// wood blocks - is played as short clicks generated here, since a piano-only SoundFont has no drum kit.
/// </summary>
sealed class SoundFontOutputDevice : IOutputDevice, ISampleProvider
{
    public const int SampleRate = 44100;
    const int ClickChannel = 9, AccentClick = 76;

    readonly Synthesizer synth;
    readonly WaveOut? output;
    readonly object gate = new();
    readonly List<(int Position, bool Accent)> clicks = [];
    float[] left = new float[4096], right = new float[4096];

    /// <param name="playToSpeakers">False only for tests, which pull the sound with <see cref="Read"/> themselves.</param>
    public SoundFontOutputDevice(string soundFontPath, bool playToSpeakers = true)
    {
        synth = new Synthesizer(soundFontPath, new SynthesizerSettings(SampleRate) { EnableReverbAndChorus = true });
        if (!playToSpeakers)
            return;
        output = new WaveOut { BufferMilliseconds = 30, NumberOfBuffers = 3 };
        output.Init(this);
        output.Play();
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    public event EventHandler<MidiEventSentEventArgs>? EventSent;

    public void PrepareForEventsSending()
    {
    }

    public void SendEvent(MidiEvent midiEvent)
    {
        lock (gate)
        {
            switch (midiEvent)
            {
                case NoteOnEvent on when on.Channel == ClickChannel:
                    if (on.Velocity > 0)
                        clicks.Add((0, on.NoteNumber == AccentClick));
                    break;
                case NoteOnEvent on when on.Velocity == 0:
                    synth.NoteOff(on.Channel, on.NoteNumber);
                    break;
                case NoteOnEvent on:
                    synth.NoteOn(on.Channel, on.NoteNumber, on.Velocity);
                    break;
                case NoteOffEvent off when off.Channel != ClickChannel:
                    synth.NoteOff(off.Channel, off.NoteNumber);
                    break;
                case ControlChangeEvent cc:
                    synth.ProcessMidiMessage(cc.Channel, 0xB0, cc.ControlNumber, cc.ControlValue);
                    break;
                case ProgramChangeEvent program:
                    synth.ProcessMidiMessage(program.Channel, 0xC0, program.ProgramNumber, 0);
                    break;
                case PitchBendEvent bend:
                    synth.ProcessMidiMessage(bend.Channel, 0xE0, bend.PitchValue & 0x7F, bend.PitchValue >> 7);
                    break;
            }
        }
        EventSent?.Invoke(this, new MidiEventSentEventArgs(midiEvent));
    }

    /// <summary>Called by the sound card's thread: the synth's next stereo block, with any metronome clicks mixed in.</summary>
    public int Read(Span<float> buffer)
    {
        int frames = buffer.Length / 2;
        if (left.Length < frames)
        {
            left = new float[frames];
            right = new float[frames];
        }
        lock (gate)
        {
            synth.Render(left.AsSpan(0, frames), right.AsSpan(0, frames));
            MixClicks(frames);
        }
        for (int i = 0; i < frames; i++)
        {
            buffer[2 * i] = left[i];
            buffer[2 * i + 1] = right[i];
        }
        return frames * 2;
    }

    /// <summary>A wood-block-like tick: a short, fast-decaying tone, higher on the bar's first beat.</summary>
    void MixClicks(int frames)
    {
        const double Length = 0.04;
        for (int c = clicks.Count - 1; c >= 0; c--)
        {
            var (position, accent) = clicks[c];
            double frequency = accent ? 2000 : 1500, amplitude = accent ? 0.5 : 0.35;
            int end = Math.Min(frames, (int)(Length * SampleRate) - position);
            for (int i = 0; i < end; i++)
            {
                double t = (double)(position + i) / SampleRate;
                float sample = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * t) * Math.Exp(-t / 0.008));
                left[i] += sample;
                right[i] += sample;
            }
            position += frames;
            if (position >= Length * SampleRate)
                clicks.RemoveAt(c);
            else
                clicks[c] = (position, accent);
        }
    }

    public void Dispose()
    {
        output?.Stop();
        output?.Dispose();
    }
}
