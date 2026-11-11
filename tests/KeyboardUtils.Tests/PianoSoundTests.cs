using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace KeyboardUtils.Tests;

public class PianoSoundTests
{
    /// <summary>The real SoundFont, when a developer has it unpacked (it's 55 MB, so not in the repo).</summary>
    static string? SoundFont()
    {
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("KBU_TEST_SF2"),
            File.Exists(PianoSoundModule.SoundFontPath) ? PianoSoundModule.SoundFontPath : null,
        ];
        return candidates.FirstOrDefault(p => p != null && File.Exists(p));
    }

    static float Peak(SoundFontOutputDevice device, int frames)
    {
        var buffer = new float[frames * 2];
        Assert.Equal(buffer.Length, device.Read(buffer));
        return buffer.Max(Math.Abs);
    }

    [Fact]
    public void The_pack_lives_in_the_modules_folder()
    {
        Assert.EndsWith(Path.Combine("KeyboardUtils", "modules", "piano-sound"), PianoSoundModule.Dir);
        Assert.Equal(PianoSoundModule.Dir, Path.GetDirectoryName(PianoSoundModule.SoundFontPath));
        Assert.EndsWith(".sf2", PianoSoundModule.SoundFontPath);
    }

    [Fact]
    public void Plays_a_note_and_the_metronome()
    {
        if (SoundFont() is not { } path)
            return; // no SoundFont on this machine - nothing to play
        using var device = new SoundFontOutputDevice(path, playToSpeakers: false);
        Assert.Equal(0, Peak(device, 4410));

        device.SendEvent(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100));
        Assert.True(Peak(device, 4410) > 0.01f);
        device.SendEvent(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0));
        Peak(device, 44100 * 3); // let it ring out
        Assert.True(Peak(device, 4410) < 0.01f);

        device.SendEvent(new NoteOnEvent((SevenBitNumber)76, (SevenBitNumber)120) { Channel = (FourBitNumber)9 });
        Assert.True(Peak(device, 4410) > 0.1f);
        Assert.True(Peak(device, 4410) < 0.01f); // a click is short
    }
}
