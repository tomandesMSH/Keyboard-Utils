namespace KeyboardUtils;

/// <summary>Opening songs from outside: <c>keyboardutils://</c> links from Muzio and files given on the command line.</summary>
sealed partial class MainForm
{
    readonly CancellationTokenSource openListener = new();

    /// <summary>What the app was started with (a link or a file), opened once the window is up.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? StartupRequest { get; init; }

    /// <summary>This start finished an update (<see cref="SelfUpdater"/>): say so.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool JustUpdated { get; init; }

    /// <summary>Called from the constructor: listen for later starts handing over links, open the startup one.</summary>
    void InitIntegration()
    {
        Shown += (_, _) =>
        {
            ComeToFront();
            if (JustUpdated)
            {
                // A link the update was started with wins; otherwise the song from before the update.
                if (ResumeState.Take() is { } resume && string.IsNullOrWhiteSpace(StartupRequest))
                    Resume(resume);
                SetStatus(T($"Aktualizováno na verzi {UpdateChecker.CurrentVersion.ToString(3)}.", $"Updated to version {UpdateChecker.CurrentVersion.ToString(3)}."));
                // What the update brought (quietly skipped when offline).
                _ = WhatsNewForm.ShowAsync(this, onlyWithNotes: true);
            }
            _ = ProtocolHandler.ListenAsync(argument => Post(() => _ = OpenRequestAsync(argument)), openListener.Token);
            if (!string.IsNullOrWhiteSpace(StartupRequest))
                _ = OpenRequestAsync(StartupRequest);
        };
        FormClosed += (_, _) => openListener.Cancel();
    }

    /// <summary>A link from Muzio, a song file, or "" (the app was started again: just come to the front).</summary>
    async Task OpenRequestAsync(string argument)
    {
        RestoreWindow();
        argument = argument.Trim();
        if (argument.Length == 0)
            return;
        if (RemoteScore.IsLink(argument))
        {
            await OpenRemoteScoreAsync(argument);
            return;
        }
        if (File.Exists(argument) && SongExtensions.Contains(Path.GetExtension(argument), StringComparer.OrdinalIgnoreCase))
            OpenSongPath(argument);
    }

    async Task OpenRemoteScoreAsync(string argument)
    {
        if (RemoteScore.Parse(argument, out string? error) is not { } link)
        {
            ShowModal(this, Text, error!, AntdUI.TType.Error);
            return;
        }
        SetStatus(T($"Stahuji noty z Muzia: {link.FileName}...", $"Downloading the score from Muzio: {link.FileName}..."));
        string path;
        try
        {
            path = await RemoteScore.DownloadAsync(link);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            if (IsDisposed)
                return;
            SetStatus("");
            ShowModal(this, Text,
                T("Noty se nepodařilo stáhnout. Odkaz platí jen pár minut - v Muziu klikni na „Otevřít v Keyboard Utils“ znovu, nebo zkontroluj připojení k internetu.",
                    "Couldn't download the score. The link only works for a few minutes - click \"Open in Keyboard Utils\" in Muzio again, or check your internet connection.")
                + (ex is HttpRequestException { StatusCode: null } && ex.Message.Length < 80 ? "\n\n" + ex.Message : ""),
                AntdUI.TType.Error);
            return;
        }
        if (!IsDisposed)
            OpenSongPath(path);
    }

    /// <summary>For the update's restart: the open song, where it is in it and the loop (<see cref="ResumeState"/>).</summary>
    void SaveResumeState()
    {
        if (CurrentSongPath is not { } path)
            return;
        int index = playlist.IndexOf(path);
        new ResumeState
        {
            Playlist = index >= 0 ? [.. playlist] : [path],
            PlaylistIndex = Math.Max(0, index),
            Seconds = PlaybackSeconds,
            LoopStart = loopStart,
            LoopEnd = loopEnd,
            LoopOn = loopOn,
        }.Save();
    }

    void Resume(ResumeState state)
    {
        playlist = [.. state.Playlist];
        playlistIndex = state.PlaylistIndex;
        LoadCurrentSong();
        if (CurrentSongPath != state.SongPath || currentScore == null)
            return;
        double at = Math.Clamp(state.Seconds, 0, currentScore.DurationSeconds);
        PlaybackSeek(at);
        SetViewTime(at);
        loopStart = state.LoopStart;
        loopEnd = state.LoopEnd;
        loopOn = state.LoopOn;
        ApplyLoop();
    }

    /// <summary>
    /// Started from Muzio (or after the language was picked on the first start), the window can open behind the
    /// browser: Windows doesn't let a window that comes up a moment later take the front. Put it on top once - the
    /// TopMost flip raises it even when Windows refuses to activate it - and flash the taskbar button if it still
    /// isn't the active one.
    /// </summary>
    void ComeToFront()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        TopMost = true;
        TopMost = false;
        Activate();
        if (Handle != WindowFront.Foreground)
            WindowFront.Flash(Handle);
    }

    void OpenSongPath(string path)
    {
        // Like opening it from the menu: a recording in progress is finished first (offers saving).
        FinishCreateMode();
        playlist = [path];
        playlistIndex = 0;
        LoadCurrentSong();
    }
}
