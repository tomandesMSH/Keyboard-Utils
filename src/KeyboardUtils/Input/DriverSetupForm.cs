using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace KeyboardUtils;

/// <summary>Shown when the Interception driver can't be opened: installs it (elevated) and offers a restart.</summary>
sealed class DriverSetupForm : AntdUI.Window
{
    const string KeyboardClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E96B-E325-11CE-BFC1-08002BE10318}";

    readonly AntdUI.Label message = new() { Dock = DockStyle.Fill, Padding = new Padding(16), Font = new Font("Segoe UI", 10f) };
    readonly AntdUI.Button installButton = new() { Text = T("Nainstalovat ovladač", "Install driver"), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button restartButton = new() { Text = T("Restartovat počítač", "Restart computer"), AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button closeButton = new() { Text = T("Zavřít", "Close"), AutoSize = true, DialogResult = DialogResult.Cancel, Radius = 8 };

    static string InstallerPath => Path.Combine(AppContext.BaseDirectory, "install-interception.exe");

    /// <summary>The driver is registered as a keyboard class filter. It only starts working after a reboot.</summary>
    public static bool IsDriverInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyboardClassKey);
        return key?.GetValue("UpperFilters") is string[] filters
            && filters.Contains("keyboard", StringComparer.OrdinalIgnoreCase);
    }

    public DriverSetupForm()
    {
        Text = T("Keyboard Utils - ovladač", "Keyboard Utils - driver");
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(520, 280);
        Resizable = false;
        MaximizeBox = MinimizeBox = false;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.AddRange([closeButton, restartButton, installButton]);
        Controls.Add(message);
        Controls.Add(buttons);
        CancelButton = closeButton;
        Controls.Add(new TitleBar(this));

        installButton.Click += (_, _) => Install();
        restartButton.Click += (_, _) => Restart();
        UpdateState();
    }

    void UpdateState()
    {
        bool installed = IsDriverInstalled();
        installButton.Visible = !installed;
        restartButton.Visible = installed;
        message.Text = installed
            ? T("Ovladač Interception je nainstalovaný, ale začne fungovat až po restartu počítače.\n\nPo restartu spusť Keyboard Utils znovu.",
                "The Interception driver is installed, but it only starts working after a restart.\n\nRun Keyboard Utils again after restarting.")
            : T("Aby šlo druhou klávesnici použít jako makro klávesnici k ovládání appky, potřebuje aplikace ovladač Interception. " +
                "Ten umí rozlišit, ze které klávesnice stisk přišel.\n\n" +
                "Instalace si vyžádá oprávnění správce a potom restart počítače.",
                "To use a second keyboard as a macro keyboard to control the app, it needs the Interception driver. " +
                "It can tell which keyboard a key press came from.\n\n" +
                "Installing it asks for administrator rights and then a restart.");
    }

    void Install()
    {
        if (!File.Exists(InstallerPath))
        {
            AntdUI.Modal.open(this, Text, T("Instalátor nebyl nalezen:", "Installer not found:") + $"\n{InstallerPath}", AntdUI.TType.Error);
            return;
        }
        try
        {
            using var process = Process.Start(new ProcessStartInfo(InstallerPath, "/install")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            process?.WaitForExit();
        }
        catch (Win32Exception)
        {
            // UAC prompt declined.
            return;
        }

        if (!IsDriverInstalled())
            AntdUI.Modal.open(this, Text, T("Instalace ovladače se nepovedla.", "Driver installation failed."), AntdUI.TType.Error);
        UpdateState();
    }

    void Restart()
    {
        if (MessageBox.Show(this, T("Restartovat počítač teď? Ulož si rozdělanou práci.", "Restart the computer now? Save your work first."), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true });
        Close();
    }
}
