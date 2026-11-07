using System.Drawing.Drawing2D;

namespace KeyboardUtils;

/// <summary>Vertical "falling notes" piano roll (Synthesia-style): notes scroll down toward a keyboard strip at the bottom.</summary>
sealed class PianoRollControl : Control, IThemed
{
    static readonly Color[] PartColors =
    [
        Color.FromArgb(66, 133, 244), Color.FromArgb(219, 68, 55), Color.FromArgb(244, 180, 0),
        Color.FromArgb(15, 157, 88), Color.FromArgb(171, 71, 188), Color.FromArgb(255, 112, 67),
    ];

    /// <summary>The colour a part's notes are drawn in - the same in the piano roll and the sheet music.</summary>
    public static Color PartColor(int partIndex) => PartColors[partIndex % PartColors.Length];

    const double SecondsVisible = 4.0;

    /// <summary>A real piano: A0 to C8.</summary>
    const int PianoLowest = 21, PianoHighest = 108;

    public const int MinKeyWidth = 14, MaxKeyWidth = 60, DefaultKeyWidth = 26;

    /// <summary>Keyboard height as a multiple of the white key width - roughly a real key's proportions.</summary>
    const float KeyHeightRatio = 4.2f;
    const float MinKeyboardHeight = 60;

    readonly record struct KeyLayout(float X, float Width, bool Black);

    Score? score;
    double currentSeconds;

    // The keys that must be visible (the song's range, held keys) vs. the keys shown, which RebuildLayout
    // widens around the required range to fill the width.
    int requiredLowest = 48; // C3
    int requiredHighest = 84; // C6
    int lowestKey = 48;
    int highestKey = 84;
    float whiteKeyWidth = DefaultKeyWidth;
    int keyWidth = DefaultKeyWidth;

    /// <summary>
    /// White key width the keyboard aims for (the "keyboard size" slider); a wider window shows more octaves
    /// rather than wider keys. Keys get narrower than this only when the song's range wouldn't fit otherwise.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int KeyWidth
    {
        get => keyWidth;
        set
        {
            keyWidth = Math.Clamp(value, MinKeyWidth, MaxKeyWidth);
            RebuildLayout();
            Invalidate();
        }
    }
    Dictionary<int, KeyLayout> keyLayout = [];
    readonly HashSet<int> heldKeys = [];

    /// <summary>When true, the roll area is hidden and the keyboard fills the whole control (note-picker use).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FillKeyboard { get; set; }

    /// <summary>When true, clicking a key raises <see cref="KeySelected"/>.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Clickable { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int? SelectedKey { get; set; }

    /// <summary>
    /// While set, practiced notes are coloured by how they were played (pending in their part colour, then green or
    /// red), the other parts are dimmed, and the keys to press now are lit as a hint.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public LearningSession? Learning { get; set; }

    /// <summary>Create mode: time runs upward - notes appear at the keyboard as they're played and rise away.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool RecordingView { get; set; }

    public static Color CorrectColor { get; } = Color.FromArgb(52, 199, 89);
    public static Color WrongColor { get; } = Color.FromArgb(234, 67, 53);
    static Color SelectedColor { get; } = Color.FromArgb(66, 133, 244);
    static Color HintColor { get; } = Color.FromArgb(150, 190, 255);

    public event Action<int>? KeySelected;

    /// <summary>The mouse button went up after a <see cref="KeySelected"/> - the on-screen key is let go.</summary>
    public event Action<int>? KeyReleased;
    int? pressedKey;

    /// <summary>
    /// When true, dragging the note area up/down moves through the song: <see cref="SeekStarted"/>, then
    /// <see cref="Seek"/> with the new time as the mouse moves, then <see cref="SeekEnded"/>. The owner moves the
    /// playback and calls <see cref="SetCurrentTime"/> - the control itself doesn't change its time.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Seekable { get; set; }

    public event Action? SeekStarted;
    public event Action<double>? Seek;
    public event Action? SeekEnded;

    readonly System.Windows.Forms.Timer flashTimer = new() { Interval = 400 };
    Color flashColor = SelectedColor;

    // Drag-to-seek: where the drag began, on screen and in the song.
    int? dragStartY;
    double dragStartSeconds;

    public PianoRollControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 24, 28);
        flashTimer.Tick += (_, _) =>
        {
            flashTimer.Stop();
            SelectedKey = null;
            Invalidate();
        };
        RebuildLayout();
    }

    /// <summary>Briefly highlights a key, e.g. when a macro key plays it. Clears itself after <paramref name="durationMs"/>.</summary>
    public void FlashKey(int key, int durationMs = 400, Color? color = null)
    {
        flashColor = color ?? SelectedColor;
        SelectedKey = key;
        Invalidate();
        flashTimer.Interval = durationMs;
        flashTimer.Stop();
        flashTimer.Start();
    }

    /// <summary>Lights a key for as long as it's held (a note heard from the microphone/MIDI input), unlike <see cref="FlashKey"/>.</summary>
    public void SetKeyHeld(int key, bool held)
    {
        if (held ? !heldKeys.Add(key) : !heldKeys.Remove(key))
            return;
        // A heard note outside the visible range would otherwise light up nothing.
        if (held)
            Require(key);
        Invalidate();
    }

    /// <summary>Makes sure <paramref name="key"/> (with a little margin) stays on the keyboard.</summary>
    void Require(int key)
    {
        if (key - 2 >= requiredLowest && key + 2 <= requiredHighest)
            return;
        requiredLowest = Math.Max(0, Math.Min(requiredLowest, key - 2));
        requiredHighest = Math.Min(127, Math.Max(requiredHighest, key + 2));
        RebuildLayout();
    }

    public void ClearHeldKeys()
    {
        heldKeys.Clear();
        Invalidate();
    }

    public void SetScore(Score? newScore)
    {
        score = newScore;
        currentSeconds = 0;
        var pitches = score?.Parts.SelectMany(p => p.Notes).Select(n => n.Pitch).ToList() ?? [];
        if (pitches.Count > 0)
        {
            requiredLowest = Math.Max(0, pitches.Min() - 2);
            requiredHighest = Math.Min(127, pitches.Max() + 2);
        }
        RebuildLayout();
        Invalidate();
    }

    /// <summary>
    /// Swaps in an updated version of a score that's still being written (create mode) without resetting the
    /// time or shrinking the keyboard - it only widens if a note falls outside, so it doesn't jump while playing.
    /// </summary>
    public void UpdateLiveScore(Score liveScore)
    {
        score = liveScore;
        foreach (int pitch in liveScore.Parts.SelectMany(p => p.Notes).Select(n => n.Pitch))
            Require(pitch);
        Invalidate();
    }

    public void SetCurrentTime(double seconds)
    {
        currentSeconds = seconds;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildLayout();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            flashTimer.Dispose();
        base.Dispose(disposing);
    }

    static bool IsBlackKey(int note) => (note % 12) is 1 or 3 or 6 or 8 or 10;

    static int WhiteKeysBetween(int low, int high) => Enumerable.Range(low, high - low + 1).Count(n => !IsBlackKey(n));

    /// <summary>
    /// Widens the required range alternately down and up, one white key at a time, until the keys are about
    /// <see cref="KeyWidth"/> wide or the whole piano is shown. Never shrinks below the required range,
    /// so in a narrow window the keys just get narrower.
    /// </summary>
    void FitRangeToWidth()
    {
        int low = requiredLowest, high = requiredHighest;
        // Start and end on white keys: a black key at the edge would be cut in half.
        while (IsBlackKey(low) && low > 0) low--;
        while (IsBlackKey(high) && high < 127) high++;

        int wanted = Math.Max(1, ClientSize.Width) / keyWidth;
        bool down = true;
        while (WhiteKeysBetween(low, high) < wanted && (low > PianoLowest || high < PianoHighest))
        {
            if (down && low > PianoLowest)
                do low--; while (IsBlackKey(low) && low > PianoLowest);
            else if (high < PianoHighest)
                do high++; while (IsBlackKey(high) && high < PianoHighest);
            down = !down;
        }
        lowestKey = low;
        highestKey = high;
    }

    void RebuildLayout()
    {
        FitRangeToWidth();
        var layout = new Dictionary<int, KeyLayout>();
        int whiteCount = WhiteKeysBetween(lowestKey, highestKey);
        float whiteWidth = whiteCount > 0 ? (float)Math.Max(1, ClientSize.Width) / whiteCount : 20f;
        whiteKeyWidth = whiteWidth;
        float blackWidth = whiteWidth * 0.6f;

        var whiteX = new Dictionary<int, float>();
        float x = 0;
        for (int n = lowestKey; n <= highestKey; n++)
        {
            if (IsBlackKey(n))
                continue;
            whiteX[n] = x;
            layout[n] = new KeyLayout(x, whiteWidth, false);
            x += whiteWidth;
        }
        for (int n = lowestKey; n <= highestKey; n++)
        {
            if (!IsBlackKey(n))
                continue;
            int prevWhite = n - 1;
            while (prevWhite >= lowestKey && IsBlackKey(prevWhite))
                prevWhite--;
            float baseX = whiteX.TryGetValue(prevWhite, out var wx) ? wx : 0;
            layout[n] = new KeyLayout(baseX + whiteWidth - blackWidth / 2, blackWidth, true);
        }

        keyLayout = layout;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        float keyboardHeight = KeyboardHeight;
        float rollHeight = ClientSize.Height - keyboardHeight;
        if (rollHeight < 0)
            return;

        var soundingKeys = new Dictionary<int, Color>();
        if (!FillKeyboard)
        {
            DrawNotes(g, rollHeight, soundingKeys);
            using var nowPen = new Pen(Color.White, 2);
            g.DrawLine(nowPen, 0, rollHeight, ClientSize.Width, rollHeight);
        }
        DrawKeyboard(g, rollHeight, keyboardHeight, soundingKeys);
    }

    /// <summary>Proportional to the key width (like a real keyboard), but never under 60 px or over a quarter of the control.</summary>
    float KeyboardHeight => FillKeyboard
        ? ClientSize.Height
        : Math.Clamp(whiteKeyWidth * KeyHeightRatio, MinKeyboardHeight, Math.Max(MinKeyboardHeight, ClientSize.Height * 0.25f));

    float RollHeight => FillKeyboard ? 0 : ClientSize.Height - KeyboardHeight;

    bool CanSeekAt(Point p) => Seekable && score != null && !RecordingView && p.Y < RollHeight;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        if (CanSeekAt(e.Location))
        {
            dragStartY = e.Y;
            dragStartSeconds = currentSeconds;
            Capture = true;
            SeekStarted?.Invoke();
            return;
        }
        // Clicks above the keyboard used to play the key under them too; only the keys themselves do now.
        if (!Clickable || e.Y < RollHeight || HitTestKey(e.Location) is not { } key)
            return;
        pressedKey = key;
        KeySelected?.Invoke(key);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragStartY is { } startY)
        {
            // Grab-and-drag: pulling the notes down brings later ones to the keyboard, i.e. moves forward.
            double pixelsPerSecond = RollHeight / SecondsVisible;
            double seconds = dragStartSeconds + (e.Y - startY) / pixelsPerSecond;
            Seek?.Invoke(Math.Clamp(seconds, 0, score?.DurationSeconds ?? 0));
            return;
        }
        Cursor = CanSeekAt(e.Location) ? Cursors.SizeNS : Clickable && e.Y >= RollHeight ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
        ReleaseKey();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        EndDrag();
        ReleaseKey();
    }

    void ReleaseKey()
    {
        if (pressedKey is not { } key)
            return;
        pressedKey = null;
        KeyReleased?.Invoke(key);
    }

    void EndDrag()
    {
        if (dragStartY == null)
            return;
        dragStartY = null;
        Capture = false;
        SeekEnded?.Invoke();
    }

    int? HitTestKey(Point p)
    {
        // Black keys are drawn on top, so test them first - but they only reach 60 % down the keyboard; below
        // that, the same column belongs to the white key.
        if (p.Y < RollHeight + KeyboardHeight * 0.6f)
            foreach (var (note, layout) in keyLayout.Where(k => k.Value.Black))
                if (p.X >= layout.X && p.X < layout.X + layout.Width)
                    return note;
        foreach (var (note, layout) in keyLayout.Where(k => !k.Value.Black))
            if (p.X >= layout.X && p.X < layout.X + layout.Width)
                return note;
        return null;
    }

    /// <param name="soundingKeys">Filled with the keys whose note has reached the keyboard and is still sounding,
    /// in that note's colour - so the key lights up in the colour of what hits it.</param>
    void DrawNotes(Graphics g, float rollHeight, Dictionary<int, Color> soundingKeys)
    {
        if (score == null)
            return;

        float pixelsPerSecond = (float)(rollHeight / SecondsVisible);
        using var correctBrush = new SolidBrush(CorrectColor);
        using var wrongBrush = new SolidBrush(WrongColor);
        for (int partIndex = 0; partIndex < score.Parts.Count; partIndex++)
        {
            var part = score.Parts[partIndex];
            bool practiced = Learning?.PracticedParts.Contains(partIndex) ?? false;
            // In learning mode the practiced parts are what matters, muted or not; the rest is just context.
            if (part.Muted && !practiced)
                continue;

            var partColor = PartColors[partIndex % PartColors.Length];
            using var brush = new SolidBrush(Learning != null && !practiced ? Color.FromArgb(60, partColor) : partColor);
            foreach (var note in part.Notes)
            {
                double noteEnd = note.StartSeconds + note.DurationSeconds;
                if (!keyLayout.TryGetValue(note.Pitch, out var layout))
                    continue;

                float yBottom, yTop;
                if (RecordingView)
                {
                    // Played notes rise away from the keyboard: the newest at the line, older ones higher up.
                    if (noteEnd < currentSeconds - SecondsVisible || note.StartSeconds > currentSeconds)
                        continue;
                    yTop = rollHeight - (float)((currentSeconds - note.StartSeconds) * pixelsPerSecond);
                    yBottom = rollHeight - (float)((currentSeconds - noteEnd) * pixelsPerSecond);
                }
                else
                {
                    if (noteEnd < currentSeconds || note.StartSeconds > currentSeconds + SecondsVisible)
                        continue;
                    yBottom = rollHeight - (float)((note.StartSeconds - currentSeconds) * pixelsPerSecond);
                    yTop = rollHeight - (float)((noteEnd - currentSeconds) * pixelsPerSecond);
                }
                float height = Math.Max(3, yBottom - yTop);
                var mark = practiced ? Learning!.MarkOf(note) : NoteMark.Pending;
                var noteBrush = mark switch
                {
                    NoteMark.Correct => correctBrush,
                    NoteMark.Wrong => wrongBrush,
                    _ => brush,
                };
                g.FillRectangle(noteBrush, layout.X + 1, yTop, Math.Max(2, layout.Width - 2), height);

                // Recording shows what's held via the held keys instead. In learning mode only practiced notes
                // that were already judged light up - a pending one is the "play this" hint, the rest is dimmed context.
                bool lights = !RecordingView && note.StartSeconds <= currentSeconds && currentSeconds < noteEnd
                    && (Learning == null || mark != NoteMark.Pending);
                if (lights)
                    soundingKeys[note.Pitch] = ((SolidBrush)noteBrush).Color;
            }
        }
    }

    void DrawKeyboard(Graphics g, float top, float height, IReadOnlyDictionary<int, Color> soundingKeys)
    {
        using var whiteBrush = new SolidBrush(Color.White);
        using var blackBrush = new SolidBrush(Color.Black);
        using var selectedBrush = new SolidBrush(flashColor);
        using var heldBrush = new SolidBrush(Color.FromArgb(255, 152, 0));
        using var hintBrush = new SolidBrush(HintColor);
        using var borderPen = new Pen(Color.Gray);
        var hintKeys = Learning?.ExpectedKeys().ToHashSet() ?? [];

        var soundingBrushes = soundingKeys.Values.Distinct().ToDictionary(c => c, c => new SolidBrush(c));

        // A flash (e.g. a red wrong key) wins over a held key, then the "play this" hint, then a sounding note.
        Brush BrushFor(int note, Brush rest) => note == SelectedKey ? selectedBrush
            : heldKeys.Contains(note) ? heldBrush
            : hintKeys.Contains(note) ? hintBrush
            : soundingKeys.TryGetValue(note, out var color) ? soundingBrushes[color]
            : rest;

        // "C4" etc. on every C, so you can find your way on a wide keyboard. Skipped when keys are too narrow to fit it.
        using var labelFont = new Font("Segoe UI", Math.Clamp(whiteKeyWidth * 0.32f, 6.5f, 9f));
        using var labelBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
        using var labelFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far };
        bool showLabels = whiteKeyWidth >= 16 && height >= 40;

        foreach (var (note, layout) in keyLayout.Where(k => !k.Value.Black))
        {
            var rect = new RectangleF(layout.X, top, layout.Width, height);
            var fill = BrushFor(note, whiteBrush);
            g.FillRectangle(fill, rect);
            g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);
            if (showLabels && note % 12 == 0)
                g.DrawString(NoteNames.Describe(note), labelFont, fill == whiteBrush ? labelBrush : Brushes.White,
                    new RectangleF(rect.X, rect.Y, rect.Width, rect.Height - 3), labelFormat);
        }
        foreach (var (note, layout) in keyLayout.Where(k => k.Value.Black))
            g.FillRectangle(BrushFor(note, blackBrush), layout.X, top, layout.Width, height * 0.6f);

        foreach (var brush in soundingBrushes.Values)
            brush.Dispose();
    }
}
