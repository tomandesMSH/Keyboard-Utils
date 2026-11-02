using System.Text.RegularExpressions;

namespace KeyboardUtils;

/// <summary>"Press a key on the keyboard you want as the macro keyboard" - identifies it via the interceptor.</summary>
sealed partial class KeyboardPickerForm : AntdUI.Window
{
    static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["046D"] = "Logitech", ["1532"] = "Razer", ["1B1C"] = "Corsair", ["1038"] = "SteelSeries",
        ["045E"] = "Microsoft", ["0951"] = "HyperX", ["03F0"] = "HP / HyperX", ["413C"] = "Dell",
        ["17EF"] = "Lenovo", ["0B05"] = "ASUS", ["258A"] = "Sinowealth", ["04D9"] = "Holtek",
        ["1A2C"] = "China Resource Semico", ["3434"] = "Keychron", ["320F"] = "Glorious", ["0C45"] = "Microdia",
    };

    readonly KeyboardInterceptor interceptor;
    readonly AntdUI.Label prompt = new()
    {
        Dock = DockStyle.Top,
        Height = 110,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 14f, FontStyle.Bold),
    };
    readonly AntdUI.Label detail = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.TopCenter,
        Font = new Font("Segoe UI", 10f),
        Padding = new Padding(12, 0, 12, 0),
    };
    readonly AntdUI.Button useButton = new() { Text = T("Použít tuto klávesnici", "Use this keyboard"), AutoSize = true, Enabled = false, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button retryButton = new() { Text = T("Zkusit znovu", "Try again"), AutoSize = true, Enabled = false, Radius = 8 };
    readonly AntdUI.Button cancelButton = new() { Text = T("Zrušit", "Cancel"), AutoSize = true, Radius = 8 };

    public MacroKeyboardTarget? Result { get; private set; }

    public KeyboardPickerForm(KeyboardInterceptor interceptor, MacroKeyboardTarget? current)
    {
        this.interceptor = interceptor;
        Text = T("Výběr makro klávesnice", "Choose macro keyboard");
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 300);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.AddRange([cancelButton, useButton, retryButton]);
        Controls.Add(detail);
        Controls.Add(prompt);
        Controls.Add(buttons);
        var titleBar = new TitleBar(this);
        Controls.Add(titleBar);
        titleBar.BringToFront();

        useButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        retryButton.Click += (_, _) => Listen();
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        interceptor.DeviceLearned += OnLearned;
        FormClosed += (_, _) =>
        {
            interceptor.DeviceLearned -= OnLearned;
            interceptor.CancelLearning();
        };

        if (current != null)
            detail.Tag = T("Aktuálně: ", "Current: ") + Describe(current.HardwareId);
        Listen();
    }

    void Listen()
    {
        Result = null;
        useButton.Enabled = retryButton.Enabled = false;
        prompt.Text = T("Stiskni libovolnou klávesu na klávesnici,\nkterou chceš používat jako MAKRO KLÁVESNICI (ovládání appky)",
            "Press any key on the keyboard\nyou want to use as the MACRO KEYBOARD (to control the app)");
        detail.Text = T("Na hlavní klávesnici teď nic nemačkej.", "Don't press anything on your main keyboard now.") + "\n\n" + (detail.Tag as string ?? "");
        interceptor.StartLearning();
    }

    /// <summary>Runs on the interception thread.</summary>
    void OnLearned(MacroKeyboardTarget target) => BeginInvoke(() =>
    {
        Result = target;
        prompt.Text = T("Klávesnice nalezena", "Keyboard found");
        detail.Text = $"{Describe(target.HardwareId)}\n" + T($"(zařízení č. {target.Device})", $"(device #{target.Device})") + "\n\n" +
                      T("Klávesy z ní půjdou jen do Keyboard Utils, do Windows se nedostanou.",
                        "Its keys will only go to Keyboard Utils, Windows won't see them.");
        useButton.Enabled = retryButton.Enabled = true;
        useButton.Focus();
    });

    public static string Describe(string hardwareId)
    {
        var match = VidPid().Match(hardwareId);
        if (!match.Success)
            return hardwareId;
        var vid = match.Groups[1].Value;
        var pid = match.Groups[2].Value;
        var vendor = Vendors.TryGetValue(vid, out var name) ? name : T("Klávesnice", "Keyboard");
        return $"{vendor}  (VID {vid.ToUpperInvariant()}, PID {pid.ToUpperInvariant()})";
    }

    [GeneratedRegex(@"VID[_&](?:000[12](?=[0-9A-F]{4}_))?([0-9A-F]{4}).*?PID[_&]([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPid();
}
