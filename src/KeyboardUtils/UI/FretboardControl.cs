using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>
/// The fingerboard of a string instrument, in place of the piano keyboard: strings across (the lowest at the bottom),
/// the guitar's frets or the violin family's finger places, and dots where to play - what sounds in playback, what to
/// play next in learning mode (blue), what the instrument is heard playing (orange), a wrong note (red).
/// It can be played like the real thing: clicks on the frets press strings (the left hand), and the zone on the right
/// is the right hand - strum across the strings or pluck one (guitar), or draw the bow along one, a click being
/// pizzicato (violin, viola, cello). Dark in both themes, like the falling notes.
/// </summary>
sealed class FretboardControl : Control, IThemed
{
    static readonly Color Board = Color.FromArgb(38, 30, 26);
    static readonly Color Ebony = Color.FromArgb(26, 24, 24);
    static readonly Color FretColor = Color.FromArgb(170, 170, 176);
    static readonly Color StringColor = Color.FromArgb(214, 200, 168);
    static readonly Color LabelColor = Color.FromArgb(190, 190, 196);
    static readonly Color HintColor = Color.FromArgb(150, 190, 255);
    static readonly Color HeldColor = Color.FromArgb(255, 152, 0);
    static readonly Color StoppedColor = Color.FromArgb(230, 228, 220);

    /// <summary>How far ahead the next notes are shown (faintly), in seconds.</summary>
    const double LookAheadSeconds = 1.2;

    /// <summary>A plucked string rings visibly this long; a bowed one while the bow moves.</summary>
    const long RingMs = 450;

    /// <summary>The bow stops sounding when the mouse rests this long.</summary>
    const long BowRestMs = 400;

    /// <summary>A click in the bow zone shorter than this, without moving, is pizzicato.</summary>
    const long PizzicatoMs = 150;

    Instrument instrument = Instrument.ById("guitar");
    Score? score;
    double currentSeconds;
    readonly HashSet<int> held = [];
    readonly System.Windows.Forms.Timer flashTimer = new();
    (int Pitch, Color Color)? flash;

    /// <summary>The left hand: string → semitones above the open string where it's pressed.</summary>
    readonly Dictionary<int, int> stopped = [];

    /// <summary>When each string was last sounded (for the vibration), and whether it's being bowed.</summary>
    readonly Dictionary<int, long> ringingSince = [];

    /// <summary>A string plucked by the song's playback: the fret it was played at (it vibrates from there).</summary>
    readonly Dictionary<int, int> playedFret = [];
    readonly System.Windows.Forms.Timer animation = new() { Interval = 30 };
    readonly Stopwatch clock = Stopwatch.StartNew();

    // The right hand at work.
    bool playing;
    float lastY;
    long lastMoveMs, downMs;
    Point downAt;
    int? bowedString, bowedPitch, bowPending;
    bool bowMoved;

    public FretboardControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(24, 24, 28);
        flashTimer.Tick += (_, _) =>
        {
            flashTimer.Stop();
            flash = null;
            Invalidate();
        };
        animation.Tick += (_, _) => OnAnimationTick();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            flashTimer.Dispose();
            animation.Dispose();
        }
        base.Dispose(disposing);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Instrument Instrument
    {
        get => instrument;
        set
        {
            if (value == instrument)
                return;
            StopBow();
            instrument = value;
            stopped.Clear();
            ringingSince.Clear();
            playedFret.Clear();
            UpdateTab();
            Invalidate();
        }
    }

    /// <summary>Learning mode: the notes to play now are lit blue, the other parts aren't shown sounding.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public LearningSession? Learning { get; set; }

    /// <summary>A note was played on the fingerboard. <c>sustained</c>: bowed, sounding until <see cref="NoteEnded"/>.</summary>
    public event Action<int, int, bool>? NoteStarted;

    /// <summary>A bowed note stopped, or a plucked one has rung out (for the note input).</summary>
    public event Action<int>? NoteEnded;

    public void SetScore(Score? newScore)
    {
        score = newScore;
        currentSeconds = 0;
        UpdateTab();
        Invalidate();
    }

    /// <summary>The guitar's song notes sit where the TAB says (the same tablature as under the sheet music).</summary>
    Dictionary<ScoreNote, StringSpot> tab = new(ReferenceEqualityComparer.Instance);

    void UpdateTab() =>
        tab = score != null && IsGuitar
            ? Tablature.Assign(score.Parts.SelectMany(p => p.Notes), instrument.Strings)
            : new(ReferenceEqualityComparer.Instance);

    /// <summary>Where a song note is played: its TAB place on the guitar, the beginner's place otherwise.</summary>
    StringSpot? SpotOf(ScoreNote note) => tab.TryGetValue(note, out var spot) ? spot : Fingering.For(instrument, note.Pitch);

    public void SetCurrentTime(double seconds)
    {
        if (seconds == currentSeconds)
            return;
        double previous = currentSeconds;
        currentSeconds = seconds;
        // Playing on: the guitar's strings are plucked as the song's notes start (not on a jump or while learning).
        if (IsGuitar && Learning == null && seconds > previous && seconds - previous < 0.5)
            PluckSongNotes(previous, seconds);
        Invalidate();
    }

    /// <summary>The song's notes that started between two moments make their strings vibrate.</summary>
    void PluckSongNotes(double after, double upTo)
    {
        if (score == null)
            return;
        long now = clock.ElapsedMilliseconds;
        bool any = false;
        foreach (var part in score.Parts)
        {
            if (part.Muted)
                continue;
            foreach (var note in part.Notes)
            {
                if (note.StartSeconds <= after || note.StartSeconds > upTo)
                    continue;
                if (SpotOf(note) is not { } spot || stopped.ContainsKey(spot.String))
                    continue;
                ringingSince[spot.String] = now;
                playedFret[spot.String] = spot.Semitones;
                any = true;
            }
        }
        if (any)
            animation.Start();
    }

    public void SetKeyHeld(int pitch, bool down)
    {
        if (down ? held.Add(pitch) : held.Remove(pitch))
            Invalidate();
    }

    public void ClearHeldKeys()
    {
        held.Clear();
        Invalidate();
    }

    /// <summary>Lights a note for a moment (a wrong one: red).</summary>
    public void FlashKey(int pitch, int durationMs = 400, Color? color = null)
    {
        flash = (pitch, color ?? HintColor);
        flashTimer.Stop();
        flashTimer.Interval = Math.Max(1, durationMs);
        flashTimer.Start();
        Invalidate();
    }

    /// <summary>
    /// The left hand takes a chord shape (frets per string from the low one, -1 = not played) and the right hand strums
    /// it, low to high - heard through <see cref="NoteStarted"/> like a strum with the mouse.
    /// </summary>
    public void PlayChord(int[] frets)
    {
        if (!IsGuitar || frets.Length != instrument.Strings.Length)
            return;
        StopBow();
        stopped.Clear();
        playedFret.Clear();
        for (int s = 0; s < frets.Length; s++)
            if (frets[s] > 0)
                stopped[s] = frets[s];
        var strings = Enumerable.Range(0, frets.Length).Where(s => frets[s] >= 0).ToList();
        if (strings.Count == 0)
            return;
        int next = 0;
        Pluck(strings[next++], 85);
        var strum = new System.Windows.Forms.Timer { Interval = 25 };
        strum.Tick += (_, _) =>
        {
            if (next < strings.Count)
                Pluck(strings[next++], 85);
            if (next >= strings.Count)
            {
                strum.Stop();
                strum.Dispose();
            }
        };
        strum.Start();
        Invalidate();
    }

    /// <summary>Lets go of every pressed string (and stops the bow).</summary>
    public void ReleaseAll()
    {
        StopBow();
        stopped.Clear();
        Invalidate();
    }

    // --- Geometry ------------------------------------------------------------------------------------

    /// <summary>Places shown along the board: the guitar's 12 frets, an octave on the violin family.</summary>
    int Places => Fingering.GuitarFrets;

    bool IsGuitar => instrument.Id == "guitar";

    /// <summary>The frets / finger places (left part).</summary>
    RectangleF BoardRect
    {
        get
        {
            float labels = 34, openRoom = 30;
            float left = labels + openRoom;
            float width = Math.Max(40, (Width - left - 14) * 0.74f);
            return new RectangleF(left, 14, width, Math.Max(30, Height - 40));
        }
    }

    /// <summary>The right hand's zone: the sound hole (guitar) or where the bow goes (violin family).</summary>
    RectangleF PlayZone
    {
        get
        {
            var board = BoardRect;
            return new RectangleF(board.Right + 10, board.Top, Math.Max(30, Width - board.Right - 24), board.Height);
        }
    }

    /// <summary>The x of fret (or semitone place) <paramref name="n"/>: real spacing, each a twelfth root of two closer.</summary>
    float WireX(int n)
    {
        var board = BoardRect;
        float full = 1 - MathF.Pow(2, -Places / 12f);
        return board.Left + board.Width * (1 - MathF.Pow(2, -n / 12f)) / full;
    }

    /// <summary>Where a note's dot sits: an open string left of the nut, otherwise between the wires.</summary>
    float SpotX(int semitones)
    {
        if (semitones == 0)
            return BoardRect.Left - 16;
        int n = Math.Min(semitones, Places);
        return (WireX(n - 1) + WireX(n)) / 2;
    }

    float StringY(int s)
    {
        var board = BoardRect;
        int count = instrument.Strings.Length;
        float gap = board.Height / count;
        // The lowest string at the bottom, as the player looks down at it.
        return board.Bottom - gap * (s + 0.5f);
    }

    float[] StringYs() => Enumerable.Range(0, instrument.Strings.Length).Select(StringY).ToArray();

    int NearestString(float y) => Enumerable.Range(0, instrument.Strings.Length).MinBy(s => Math.Abs(StringY(s) - y));

    /// <summary>The little "×" that lets go of all pressed strings.</summary>
    RectangleF ReleaseButton => new(BoardRect.Left - 30, 0, 16, 14);

    // --- Playing it with the mouse ------------------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || instrument.Strings.Length == 0)
            return;
        if (ReleaseButton.Contains(e.Location) && stopped.Count > 0)
        {
            ReleaseAll();
            return;
        }
        var zone = PlayZone;
        if (e.X >= zone.Left - 4)
        {
            // The right hand.
            Capture = true;
            playing = true;
            lastY = e.Y;
            downAt = e.Location;
            downMs = lastMoveMs = clock.ElapsedMilliseconds;
            if (!IsGuitar)
            {
                // Not sounding yet: moving (or holding) makes it a bow stroke, letting go at once a pizzicato.
                bowPending = NearestString(e.Y);
                bowMoved = false;
                animation.Start();
            }
            return;
        }
        // The left hand: press a string at a fret / finger place; the same place again lets it go.
        if (PlaceAt(e.Location) is not { } place)
            return;
        if (place.Semitones == 0 || stopped.GetValueOrDefault(place.String, -1) == place.Semitones)
            stopped.Remove(place.String);
        else
            stopped[place.String] = place.Semitones;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!playing)
        {
            Cursor = e.X >= PlayZone.Left - 4 || PlaceAt(e.Location) != null || (ReleaseButton.Contains(e.Location) && stopped.Count > 0)
                ? Cursors.Hand : Cursors.Default;
            return;
        }
        long now = clock.ElapsedMilliseconds;
        if (IsGuitar)
        {
            // Every string crossed since the last move sounds, in the order the hand crosses them.
            double speed = Math.Abs(e.Y - lastY) / Math.Max(1.0, now - lastMoveMs);
            foreach (int s in Strumming.CrossedStrings(lastY, e.Y, StringYs()))
                Pluck(s, Strumming.Velocity(speed));
        }
        else if (Math.Abs(e.X - downAt.X) + Math.Abs(e.Y - downAt.Y) > 3)
        {
            bowMoved = true;
            StartBow();
            if (bowedString is { } s)
                ringingSince[s] = now;
        }
        lastY = e.Y;
        lastMoveMs = now;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!playing)
            return;
        playing = false;
        Capture = false;
        long held = clock.ElapsedMilliseconds - downMs;
        bool click = Math.Abs(e.X - downAt.X) + Math.Abs(e.Y - downAt.Y) <= 3;
        if (IsGuitar)
        {
            // A click in the sound hole plucks the nearest string.
            if (click)
                Pluck(NearestString(e.Y), 85);
            return;
        }
        // Let go before the bow started: a pizzicato (plucked) note.
        if (bowPending is { } plucked && click && held < PizzicatoMs)
        {
            bowPending = null;
            Pluck(plucked, 90);
            return;
        }
        bowPending = null;
        StopBow();
    }

    void Pluck(int stringIndex, int velocity)
    {
        int pitch = Strumming.Pitch(instrument.Strings, stringIndex, stopped);
        ringingSince[stringIndex] = clock.ElapsedMilliseconds;
        playedFret.Remove(stringIndex);
        NoteStarted?.Invoke(pitch, velocity, false);
        animation.Start();
        // The note input hears it as a short press.
        var ends = new System.Windows.Forms.Timer { Interval = 300 };
        ends.Tick += (_, _) =>
        {
            ends.Dispose();
            NoteEnded?.Invoke(pitch);
        };
        ends.Start();
    }

    /// <summary>The pending bow stroke starts sounding (the mouse moved, or stayed down long enough).</summary>
    void StartBow()
    {
        if (bowPending is not { } s)
            return;
        bowPending = null;
        bowedString = s;
        bowedPitch = Strumming.Pitch(instrument.Strings, s, stopped);
        ringingSince[s] = clock.ElapsedMilliseconds;
        NoteStarted?.Invoke(bowedPitch.Value, 90, true);
    }

    void StopBow()
    {
        if (bowedPitch is { } pitch)
            NoteEnded?.Invoke(pitch);
        bowedString = null;
        bowedPitch = null;
    }

    void OnAnimationTick()
    {
        long now = clock.ElapsedMilliseconds;
        // Held without moving past the pizzicato time: it's a bow stroke after all.
        if (playing && bowPending != null && now - downMs >= PizzicatoMs)
            StartBow();
        // The bow resting too long: it stopped sounding.
        if (playing && bowedString != null && now - lastMoveMs > BowRestMs && bowMoved)
            StopBow();
        bool any = bowPending != null || ringingSince.Any(r => now - r.Value < RingMs || r.Key == bowedString);
        Invalidate();
        if (!any)
            animation.Stop();
    }

    /// <summary>A place on the board: the nearest string, the fret (or place) under the pointer, open left of the nut.</summary>
    (int String, int Semitones)? PlaceAt(Point p)
    {
        if (instrument.Strings.Length == 0)
            return null;
        var board = BoardRect;
        if (p.Y < board.Top - 6 || p.Y > board.Bottom + 6 || p.X > board.Right || p.X < board.Left - 30)
            return null;
        int place = 0;
        for (int n = 1; n <= Places; n++)
            if (p.X >= WireX(n - 1) && p.X < WireX(n))
                place = n;
        return (NearestString(p.Y), place);
    }

    // --- Drawing -------------------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(BackColor);
        var strings = instrument.Strings;
        if (strings.Length == 0 || Width < 160 || Height < 60)
            return;
        var board = BoardRect;
        var zone = PlayZone;

        using var labelFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 8f);
        using var label = new SolidBrush(LabelColor);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        using (var wood = new SolidBrush(IsGuitar ? Board : Ebony))
            g.FillRectangle(wood, board);
        DrawPlayZone(g, zone, smallFont, center);

        using (var nut = new Pen(Color.FromArgb(230, 222, 200), 5))
            g.DrawLine(nut, board.Left, board.Top, board.Left, board.Bottom);
        if (IsGuitar)
        {
            using var fret = new Pen(FretColor, 2);
            using var inlay = new SolidBrush(Color.FromArgb(120, 230, 222, 200));
            for (int n = 1; n <= Places; n++)
            {
                g.DrawLine(fret, WireX(n), board.Top, WireX(n), board.Bottom);
                g.DrawString(n.ToString(), smallFont, label, new RectangleF(WireX(n - 1), board.Bottom + 2, WireX(n) - WireX(n - 1), 16), center);
            }
            foreach (int n in new[] { 3, 5, 7, 9 })
                g.FillEllipse(inlay, SpotX(n) - 5, board.Top + board.Height / 2 - 5, 10, 10);
            g.FillEllipse(inlay, SpotX(12) - 5, board.Top + board.Height / 3 - 5, 10, 10);
            g.FillEllipse(inlay, SpotX(12) - 5, board.Top + board.Height * 2 / 3 - 5, 10, 10);
        }
        else
        {
            using var place = new Pen(Color.FromArgb(40, 255, 255, 255), 1);
            for (int n = 1; n <= Places; n++)
                g.DrawLine(place, WireX(n), board.Top, WireX(n), board.Bottom);
            int[] tapes = instrument.Id == "cello" ? [2, 3, 4, 5] : [2, 4, 5, 7];
            using var tape = new Pen(Color.FromArgb(150, 245, 240, 225), 3);
            for (int i = 0; i < tapes.Length; i++)
            {
                float x = SpotX(tapes[i]);
                g.DrawLine(tape, x, board.Top, x, board.Bottom);
                g.DrawString((i + 1).ToString(), smallFont, label, new RectangleF(x - 10, board.Bottom + 2, 20, 16), center);
            }
        }

        // The strings - through the board and the right hand's zone - wavy while they ring.
        long now = clock.ElapsedMilliseconds;
        for (int s = 0; s < strings.Length; s++)
        {
            float y = StringY(s);
            using var pen = new Pen(StringColor, Math.Max(1f, 3.2f - s * 0.45f));
            float from = stopped.TryGetValue(s, out int at) ? SpotX(at)
                : playedFret.TryGetValue(s, out int fret) && fret > 0 ? SpotX(fret) : board.Left;
            if (from > board.Left)
                g.DrawLine(pen, board.Left - 30, y, from, y);
            float amplitude = 0;
            if (ringingSince.TryGetValue(s, out long since))
            {
                bool bowed = bowedString == s && playing;
                double age = now - since;
                amplitude = bowed ? 2.2f : age < RingMs ? (float)(3.5 * (1 - age / RingMs)) : 0;
            }
            if (amplitude <= 0.05f)
                g.DrawLine(pen, from > board.Left ? from : board.Left - 30, y, zone.Right, y);
            else
                DrawVibrating(g, pen, from > board.Left ? from : board.Left - 30, zone.Right, y, amplitude, now);
            g.DrawString(LetterOf(strings[s]), labelFont, label, new RectangleF(0, y - 10, 30, 20), center);
        }

        if (stopped.Count > 0)
            g.DrawString("×", labelFont, label, ReleaseButton, center);
        DrawStopped(g);
        DrawDots(g, strings);
    }

    void DrawPlayZone(Graphics g, RectangleF zone, Font font, StringFormat center)
    {
        if (IsGuitar)
        {
            // The sound hole: a dark circle with a rosette ring.
            float d = Math.Min(zone.Width, zone.Height) * 0.92f;
            var hole = new RectangleF(zone.Left + (zone.Width - d) / 2, zone.Top + (zone.Height - d) / 2, d, d);
            using (var top = new SolidBrush(Color.FromArgb(150, 110, 70)))
                g.FillRectangle(top, zone);
            using (var rosette = new Pen(Color.FromArgb(90, 60, 35), 6))
                g.DrawEllipse(rosette, hole);
            using (var dark = new SolidBrush(Color.FromArgb(18, 14, 12)))
                g.FillEllipse(dark, hole);
        }
        else
        {
            using var band = new SolidBrush(Color.FromArgb(48, 44, 42));
            g.FillRectangle(band, zone);
        }
        if (!IsGuitar)
        {
            using var hint = new SolidBrush(Color.FromArgb(150, 230, 225, 215));
            g.DrawString(T("táhni smyčcem", "draw the bow"), font, hint, new RectangleF(zone.Left, zone.Bottom + 2, zone.Width, 16), center);
        }
    }

    static void DrawVibrating(Graphics g, Pen pen, float from, float to, float y, float amplitude, long now)
    {
        var points = new List<PointF>();
        float phase = now / 18f;
        for (float x = from; x <= to; x += 4)
        {
            // Still at the ends, widest in the middle.
            float t = (x - from) / Math.Max(1, to - from);
            points.Add(new PointF(x, y + amplitude * MathF.Sin(t * MathF.PI) * MathF.Sin(phase + t * 40)));
        }
        if (points.Count > 1)
            g.DrawLines(pen, points.ToArray());
    }

    /// <summary>The left hand: a light dot where each string is pressed, with the fret or the finger.</summary>
    void DrawStopped(Graphics g)
    {
        if (stopped.Count == 0)
            return;
        float radius = Math.Clamp(BoardRect.Height / instrument.Strings.Length * 0.34f, 6, 12);
        using var font = new Font("Segoe UI", Math.Max(7f, radius * 0.8f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var fill = new SolidBrush(StoppedColor);
        using var ink = new SolidBrush(Color.FromArgb(40, 34, 30));
        foreach (var (s, semitones) in stopped)
        {
            var rect = new RectangleF(SpotX(semitones) - radius, StringY(s) - radius, 2 * radius, 2 * radius);
            g.FillEllipse(fill, rect);
            var spot = Fingering.For(instrument, instrument.Strings[s] + semitones);
            string text = IsGuitar ? semitones.ToString() : spot is { String: var fs, Label: var l } && fs == s ? l : "";
            g.DrawString(text, font, ink, rect, center);
        }
    }

    /// <summary>A string's name without the octave: E A D G H E (Czech) / B (English).</summary>
    static string LetterOf(int pitch) => new string(Tuning.Name(pitch).TakeWhile(c => !char.IsDigit(c)).ToArray());

    void DrawDots(Graphics g, int[] strings)
    {
        var dots = new Dictionary<int, (Color Color, bool Faint)>();
        // Song notes know their place (the TAB's); played / expected keys use the hand's or the beginner's.
        var songSpots = new Dictionary<int, StringSpot>();
        void Add(int pitch, Color color, bool faint = false, ScoreNote? note = null)
        {
            if (dots.TryAdd(pitch, (color, faint)) && note != null && SpotOf(note) is { } spot)
                songSpots[pitch] = spot;
        }

        if (flash is { } f)
            Add(f.Pitch, f.Color);
        foreach (int pitch in held)
            Add(pitch, HeldColor);
        if (Learning is { } learning)
            foreach (int pitch in learning.ExpectedKeys())
                Add(pitch, HintColor);
        if (score != null)
        {
            for (int part = 0; part < score.Parts.Count; part++)
            {
                var p = score.Parts[part];
                bool practiced = Learning?.PracticedParts.Contains(part) ?? true;
                if (p.Muted && !practiced)
                    continue;
                var color = PianoRollControl.PartColor(part);
                foreach (var note in p.Notes)
                {
                    double end = note.StartSeconds + note.DurationSeconds;
                    if (Learning == null && note.StartSeconds <= currentSeconds && end > currentSeconds)
                        Add(note.Pitch, color, note: note);
                    else if (practiced && note.StartSeconds > currentSeconds && note.StartSeconds <= currentSeconds + LookAheadSeconds)
                        Add(note.Pitch, color, faint: true, note: note);
                }
            }
        }

        float radius = Math.Clamp(BoardRect.Height / strings.Length * 0.38f, 7, 14);
        using var font = new Font("Segoe UI", Math.Max(7f, radius * 0.75f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        foreach (var (pitch, (color, faint)) in dots.OrderByDescending(d => d.Value.Faint))
        {
            // A note the left hand is pressing shows there; otherwise the beginner's place.
            var spot = PressedSpotFor(pitch) ?? songSpots.GetValueOrDefault(pitch) ?? Fingering.For(instrument, pitch);
            if (spot == null)
                continue;
            float x = SpotX(spot.Semitones), y = StringY(spot.String);
            var rect = new RectangleF(x - radius, y - radius, 2 * radius, 2 * radius);
            if (faint)
            {
                using var ring = new Pen(Color.FromArgb(150, color), 2);
                g.DrawEllipse(ring, rect);
                continue;
            }
            using (var fill = new SolidBrush(color))
                g.FillEllipse(fill, rect);
            using var outline = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f);
            g.DrawEllipse(outline, rect);
            string text = IsGuitar ? spot.Semitones.ToString() : spot.Label;
            using var ink = new SolidBrush(color.GetBrightness() > 0.6f ? Color.FromArgb(30, 30, 30) : Color.White);
            g.DrawString(text, font, ink, rect, center);
        }
    }

    /// <summary>Where the left hand plays <paramref name="pitch"/> right now (a pressed string, or an open one), if anywhere.</summary>
    StringSpot? PressedSpotFor(int pitch)
    {
        for (int s = 0; s < instrument.Strings.Length; s++)
        {
            int at = stopped.GetValueOrDefault(s);
            bool ringing = bowedString == s || (ringingSince.TryGetValue(s, out long since) && clock.ElapsedMilliseconds - since < 2 * RingMs);
            if (instrument.Strings[s] + at == pitch && (stopped.ContainsKey(s) || ringing))
                return new StringSpot(s, at, null, IsGuitar ? at.ToString() : Fingering.For(instrument, pitch) is { } f && f.String == s ? f.Label : "");
        }
        return null;
    }
}
