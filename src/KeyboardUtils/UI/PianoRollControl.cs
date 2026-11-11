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

    /// <summary>
    /// Create mode, after recording: clicking a note selects it (<see cref="NoteSelected"/>), shift-click toggles
    /// one in/out of the selection (<see cref="NoteToggleSelectRequested"/>), dragging empty space marquee-selects
    /// a region (<see cref="MarqueeSelectRequested"/>), dragging a selected note moves the whole selection
    /// (<see cref="NoteDragged"/>), arrow keys nudge it, Delete removes it, double-click opens the properties
    /// dialog. The control only reports intent - the owner applies edits and calls <see cref="SetSelectedNotes"/>
    /// to keep the highlight in sync, the same way <see cref="Seek"/> works for scrubbing.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Editable { get; set; }

    HashSet<ScoreNote> selectedNotes = [];

    /// <summary>Seconds a Left/Right key press nudges the selection by.</summary>
    const double KeyNudgeSeconds = 0.02;

    /// <summary>A plain click on a note not already in the selection - replaces the selection with just it.</summary>
    public event Action<ScoreNote?>? NoteSelected;

    /// <summary>A shift-click on a note - add/remove it from the selection without touching the rest.</summary>
    public event Action<ScoreNote>? NoteToggleSelectRequested;

    /// <summary>A drag over empty space finished: the notes inside the rectangle, and whether to add to (shift
    /// was held) or replace the current selection. Empty + not additive is "clicked empty space" - clears it.</summary>
    public event Action<IReadOnlyList<ScoreNote>, bool>? MarqueeSelectRequested;

    /// <summary>A note drag began/ended - a good place to group every <see cref="NoteDragged"/> call in between
    /// into one undo step, since a drag can call it once per pixel of movement.</summary>
    public event Action? NoteDragStarted;
    public event Action? NoteDragEnded;

    /// <summary>A drag, or a Left/Right key press, moved the selection by this many seconds (+ = later).</summary>
    public event Action<double>? NoteDragged;

    /// <summary>An Up/Down key press - the selection should move by this many semitones.</summary>
    public event Action<int>? NotePitchChangeRequested;

    public event Action? NoteDeleteRequested;

    /// <summary>Double-click on a selected note: open its pitch/time/duration dialog (the owner decides whether
    /// that's valid for the current selection size).</summary>
    public event Action? NoteEditRequested;

    int? dragNoteStartY;

    // Marquee (rubber-band) drag-select over empty space: the rectangle's corners on screen, and whether it adds
    // to (Shift was held at drag-start) or replaces the selection.
    Point? marqueeStart, marqueeCurrent;
    bool marqueeAdditive;

    /// <summary>Resyncs the highlighted notes after the owner applies an edit (old references no longer match).</summary>
    public void SetSelectedNotes(IReadOnlySet<ScoreNote> notes)
    {
        selectedNotes = new HashSet<ScoreNote>(notes);
        Invalidate();
    }

    /// <summary>When true, every white key shows its note name (C, D, E...), not just the Cs.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllKeyLabels
    {
        get => allKeyLabels;
        set
        {
            allKeyLabels = value;
            Invalidate();
        }
    }
    bool allKeyLabels;

    /// <summary>A loop's start and end, drawn as lines across the falling notes; null = no loop.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public (double Start, double End)? Loop
    {
        get => loop;
        set
        {
            loop = value;
            Invalidate();
        }
    }
    (double Start, double End)? loop;

    public static Color LoopColor { get; } = Color.FromArgb(255, 179, 0);

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
        selectedNotes.Clear();
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
            DrawLoop(g, rollHeight);
            DrawMarquee(g);
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

    bool CanSeekAt(Point p) => Seekable && !Editable && score != null && !RecordingView && p.Y < RollHeight;

    bool CanEditAt(Point p) => Editable && score != null && !RecordingView && p.Y < RollHeight;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        if (CanEditAt(e.Location))
        {
            Focus();
            var hit = HitTestNote(e.Location);
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            if (hit != null)
            {
                if (shift)
                {
                    NoteToggleSelectRequested?.Invoke(hit);
                    return;
                }
                // Clicking a note already in the selection drags the whole group; otherwise it becomes the sole selection.
                if (!selectedNotes.Contains(hit))
                    NoteSelected?.Invoke(hit);
                dragNoteStartY = e.Y;
                Capture = true;
                NoteDragStarted?.Invoke();
                return;
            }
            // Empty space: start a marquee (rubber-band) drag-select; a plain click with no drag selects nothing.
            marqueeStart = marqueeCurrent = e.Location;
            marqueeAdditive = shift;
            Capture = true;
            return;
        }
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

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (CanEditAt(e.Location) && selectedNotes.Count > 0)
            NoteEditRequested?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragNoteStartY is { } noteStartY)
        {
            double pixelsPerSecond = RollHeight / SecondsVisible;
            double deltaSeconds = (e.Y - noteStartY) / pixelsPerSecond;
            dragNoteStartY = e.Y;
            if (deltaSeconds != 0)
                NoteDragged?.Invoke(deltaSeconds);
            return;
        }
        if (marqueeStart != null)
        {
            marqueeCurrent = e.Location;
            Invalidate();
            return;
        }
        if (dragStartY is { } startY)
        {
            // Grab-and-drag: pulling the notes down brings later ones to the keyboard, i.e. moves forward.
            double pixelsPerSecond = RollHeight / SecondsVisible;
            double seconds = dragStartSeconds + (e.Y - startY) / pixelsPerSecond;
            Seek?.Invoke(Math.Clamp(seconds, 0, score?.DurationSeconds ?? 0));
            return;
        }
        Cursor = CanSeekAt(e.Location) ? Cursors.SizeNS
            : CanEditAt(e.Location) && HitTestNote(e.Location) != null ? Cursors.SizeAll
            : Clickable && e.Y >= RollHeight ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
        EndNoteDrag();
        EndMarquee();
        ReleaseKey();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        EndDrag();
        EndNoteDrag();
        EndMarquee();
        ReleaseKey();
    }

    void EndNoteDrag()
    {
        if (dragNoteStartY == null)
            return;
        dragNoteStartY = null;
        Capture = false;
        NoteDragEnded?.Invoke();
    }

    void EndMarquee()
    {
        if (marqueeStart is not { } start || marqueeCurrent is not { } current)
            return;
        marqueeStart = marqueeCurrent = null;
        Capture = false;
        var rect = Rectangle.FromLTRB(Math.Min(start.X, current.X), Math.Min(start.Y, current.Y),
            Math.Max(start.X, current.X), Math.Max(start.Y, current.Y));
        MarqueeSelectRequested?.Invoke(NotesInRect(rect), marqueeAdditive);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        Editable && keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Delete or Keys.Back
            ? true
            : base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Editable || selectedNotes.Count == 0)
            return;
        switch (e.KeyCode)
        {
            case Keys.Delete or Keys.Back:
                NoteDeleteRequested?.Invoke();
                e.Handled = true;
                break;
            case Keys.Left:
                NoteDragged?.Invoke(-KeyNudgeSeconds);
                e.Handled = true;
                break;
            case Keys.Right:
                NoteDragged?.Invoke(KeyNudgeSeconds);
                e.Handled = true;
                break;
            case Keys.Up:
                NotePitchChangeRequested?.Invoke(1);
                e.Handled = true;
                break;
            case Keys.Down:
                NotePitchChangeRequested?.Invoke(-1);
                e.Handled = true;
                break;
        }
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

    /// <summary>
    /// Where a note's falling (or, in <see cref="RecordingView"/>, rising) rectangle is vertically right now;
    /// null when it's outside the visible time window. Shared by drawing and <see cref="HitTestNote"/> so the
    /// two can never drift apart.
    /// </summary>
    (float Top, float Bottom)? NoteYRange(ScoreNote note, float rollHeight, float pixelsPerSecond)
    {
        double noteEnd = note.StartSeconds + note.DurationSeconds;
        if (RecordingView)
        {
            // Played notes rise away from the keyboard: the newest at the line, older ones higher up.
            if (noteEnd < currentSeconds - SecondsVisible || note.StartSeconds > currentSeconds)
                return null;
            float yTop = rollHeight - (float)((currentSeconds - note.StartSeconds) * pixelsPerSecond);
            float yBottom = rollHeight - (float)((currentSeconds - noteEnd) * pixelsPerSecond);
            return (yTop, yBottom);
        }
        if (noteEnd < currentSeconds || note.StartSeconds > currentSeconds + SecondsVisible)
            return null;
        float bottom = rollHeight - (float)((note.StartSeconds - currentSeconds) * pixelsPerSecond);
        float top = rollHeight - (float)((noteEnd - currentSeconds) * pixelsPerSecond);
        return (top, bottom);
    }

    /// <summary>The topmost note under <paramref name="p"/> in the roll area, or null. Only used while <see cref="Editable"/>.</summary>
    public ScoreNote? HitTestNote(Point p)
    {
        if (score == null)
            return null;
        float rollHeight = RollHeight;
        float pixelsPerSecond = (float)(rollHeight / SecondsVisible);
        for (int partIndex = score.Parts.Count - 1; partIndex >= 0; partIndex--)
        {
            var part = score.Parts[partIndex];
            foreach (var note in part.Notes)
            {
                if (!keyLayout.TryGetValue(note.Pitch, out var layout) || p.X < layout.X || p.X >= layout.X + layout.Width)
                    continue;
                if (NoteYRange(note, rollHeight, pixelsPerSecond) is not { } range)
                    continue;
                float height = Math.Max(3, range.Bottom - range.Top);
                if (p.Y >= range.Top && p.Y <= range.Top + height)
                    return note;
            }
        }
        return null;
    }

    /// <summary>Every note (any part) whose rectangle intersects <paramref name="rect"/> - a marquee drag-select.</summary>
    List<ScoreNote> NotesInRect(Rectangle rect)
    {
        var result = new List<ScoreNote>();
        if (score == null)
            return result;
        float rollHeight = RollHeight;
        float pixelsPerSecond = (float)(rollHeight / SecondsVisible);
        foreach (var part in score.Parts)
            foreach (var note in part.Notes)
            {
                if (!keyLayout.TryGetValue(note.Pitch, out var layout) || layout.X + layout.Width < rect.Left || layout.X > rect.Right)
                    continue;
                if (NoteYRange(note, rollHeight, pixelsPerSecond) is not { } range)
                    continue;
                float height = Math.Max(3, range.Bottom - range.Top);
                if (range.Top + height >= rect.Top && range.Top <= rect.Bottom)
                    result.Add(note);
            }
        return result;
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
        using var selectionPen = new Pen(Color.White, 2);
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
                if (!keyLayout.TryGetValue(note.Pitch, out var layout))
                    continue;
                if (NoteYRange(note, rollHeight, pixelsPerSecond) is not { } range)
                    continue;

                float yTop = range.Top, yBottom = range.Bottom;
                double noteEnd = note.StartSeconds + note.DurationSeconds;
                float height = Math.Max(3, yBottom - yTop);
                var mark = practiced ? Learning!.MarkOf(note) : NoteMark.Pending;
                var noteBrush = mark switch
                {
                    NoteMark.Correct => correctBrush,
                    NoteMark.Wrong => wrongBrush,
                    _ => brush,
                };
                g.FillRectangle(noteBrush, layout.X + 1, yTop, Math.Max(2, layout.Width - 2), height);
                if (Editable && selectedNotes.Contains(note))
                    g.DrawRectangle(selectionPen, layout.X + 1, yTop, Math.Max(2, layout.Width - 2), height);

                // Recording shows what's held via the held keys instead. In learning mode only practiced notes
                // that were already judged light up - a pending one is the "play this" hint, the rest is dimmed context.
                bool lights = !RecordingView && note.StartSeconds <= currentSeconds && currentSeconds < noteEnd
                    && (Learning == null || mark != NoteMark.Pending);
                if (lights)
                    soundingKeys[note.Pitch] = ((SolidBrush)noteBrush).Color;
            }
        }
    }

    /// <summary>The loop's start and end as lines where those moments are in the falling notes, with a faint band between.</summary>
    void DrawLoop(Graphics g, float rollHeight)
    {
        if (loop is not { } range || RecordingView || score == null)
            return;
        float pixelsPerSecond = (float)(rollHeight / SecondsVisible);
        float Y(double seconds) => rollHeight - (float)((seconds - currentSeconds) * pixelsPerSecond);
        float top = Math.Max(0, Y(range.End)), bottom = Math.Min(rollHeight, Y(range.Start));
        if (top >= rollHeight || bottom <= 0)
            return;
        using var band = new SolidBrush(Color.FromArgb(12, LoopColor));
        g.FillRectangle(band, 0, top, ClientSize.Width, bottom - top);
        using var pen = new Pen(LoopColor, 2) { DashStyle = DashStyle.Dash };
        foreach (double edge in new[] { range.Start, range.End })
        {
            float y = Y(edge);
            if (y >= 0 && y <= rollHeight)
                g.DrawLine(pen, 0, y, ClientSize.Width, y);
        }
    }

    /// <summary>The marquee (rubber-band) rectangle while drag-selecting over empty space.</summary>
    void DrawMarquee(Graphics g)
    {
        if (marqueeStart is not { } start || marqueeCurrent is not { } current)
            return;
        var rect = Rectangle.FromLTRB(Math.Min(start.X, current.X), Math.Min(start.Y, current.Y),
            Math.Max(start.X, current.X), Math.Max(start.Y, current.Y));
        using var band = new SolidBrush(Color.FromArgb(40, SelectedColor));
        g.FillRectangle(band, rect);
        using var pen = new Pen(SelectedColor, 1);
        g.DrawRectangle(pen, rect);
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

        // "C4" etc. on every C, so you can find your way on a wide keyboard - or, with AllKeyLabels, the letter on
        // every white key too. Skipped when keys are too narrow to fit them (a letter fits narrower keys than "C4").
        using var labelFont = new Font("Segoe UI", Math.Clamp(whiteKeyWidth * 0.32f, 6.5f, 9f));
        using var labelBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
        using var labelFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far };
        bool showOctaves = whiteKeyWidth >= 16 && height >= 40;
        bool showLetters = allKeyLabels && whiteKeyWidth >= 10 && height >= 40;
        string? LabelOf(int note) => note % 12 == 0 && showOctaves ? NoteNames.Describe(note)
            : showLetters ? NoteNames.Describe(note)[..1]
            : null;

        foreach (var (note, layout) in keyLayout.Where(k => !k.Value.Black))
        {
            var rect = new RectangleF(layout.X, top, layout.Width, height);
            var fill = BrushFor(note, whiteBrush);
            g.FillRectangle(fill, rect);
            g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);
            if (LabelOf(note) is { } label)
                g.DrawString(label, labelFont, fill == whiteBrush ? labelBrush : Brushes.White,
                    new RectangleF(rect.X, rect.Y, rect.Width, rect.Height - 3), labelFormat);
        }
        foreach (var (note, layout) in keyLayout.Where(k => k.Value.Black))
            g.FillRectangle(BrushFor(note, blackBrush), layout.X, top, layout.Width, height * 0.6f);

        foreach (var brush in soundingBrushes.Values)
            brush.Dispose();
    }
}
