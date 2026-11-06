using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Theme Toggle")]
[assembly: AssemblyDescription("Tray icon that toggles Windows light/dark theme")]
[assembly: AssemblyProduct("Theme Toggle")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// Tray icon: left-click toggles Windows light/dark. Right-click: autostart / exit.
// "ThemeToggle.exe --toggle" toggles once and exits (for scripts/shortcuts).
static class ThemeToggle
{
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessageTimeout(IntPtr h, uint m, UIntPtr w, string l, uint f, uint t, out UIntPtr r);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    static bool IsLight()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Personalize))
            return Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 1;
    }

    static void Toggle()
    {
        int v = IsLight() ? 0 : 1;
        using (var k = Registry.CurrentUser.OpenSubKey(Personalize, true))
        {
            k.SetValue("AppsUseLightTheme", v, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", v, RegistryValueKind.DWord);
        }
        UIntPtr r;
        SendMessageTimeout((IntPtr)0xffff, 0x1A, UIntPtr.Zero, "ImmersiveColorSet", 2, 5000, out r);
    }

    static Icon MakeIcon()
    {
        using (var bmp = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (IsLight()) g.FillEllipse(Brushes.Gold, 4, 4, 24, 24);          // light now: sun
            else
            {                                                                    // dark now: moon
                g.FillEllipse(Brushes.LightSteelBlue, 4, 4, 24, 24);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.FillEllipse(Brushes.Transparent, 11, 1, 22, 22);
            }
            IntPtr h = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            return icon;
        }
    }

    static bool Autostart
    {
        get { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k.GetValue("ThemeToggle") != null; }
    }

    // Autostart runs a private copy so moving/deleting the downloaded file can't break it.
    static string StableCopy()
    {
        string dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThemeToggle", "ThemeToggle.exe");
        if (string.Equals(dest, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return dest;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(Application.ExecutablePath, dest, true);
            return dest;
        }
        catch (IOException) { return Application.ExecutablePath; }
        catch (UnauthorizedAccessException) { return Application.ExecutablePath; }
    }

    static void SetAutostart(bool on)
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (on) k.SetValue("ThemeToggle", "\"" + StableCopy() + "\"");
            else k.DeleteValue("ThemeToggle", false);
        }
    }

    // First launch only: ask once whether to start with Windows.
    static void FirstRunSetup()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\ThemeToggle"))
        {
            if (k.GetValue("SetupDone") != null) return;
            var answer = MessageBox.Show(
                "Theme Toggle adds a tray icon: click it to switch Windows between light and dark.\n\n" +
                "Start Theme Toggle automatically when you sign in to Windows?\n" +
                "(You can change this later by right-clicking the tray icon.)",
                "Theme Toggle setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            SetAutostart(answer == DialogResult.Yes);
            k.SetValue("SetupDone", 1, RegistryValueKind.DWord);
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--toggle") { Toggle(); return; }

        bool first;
        using (var mutex = new Mutex(true, "ThemeToggleTray_v2", out first))
        {
            if (!first) return;
            Application.EnableVisualStyles();
            FirstRunSetup();

            var tray = new NotifyIcon { Icon = MakeIcon(), Text = "Click: toggle light/dark", Visible = true };
            Action refresh = delegate { var old = tray.Icon; tray.Icon = MakeIcon(); old.Dispose(); };

            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { Toggle(); refresh(); }
            };

            var menu = new ContextMenuStrip();
            var auto = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
            menu.Opening += delegate { auto.Checked = Autostart; };
            auto.Click += delegate { SetAutostart(auto.Checked); };
            menu.Items.Add(auto);
            menu.Items.Add("Exit", null, delegate { tray.Visible = false; Application.Exit(); });
            tray.ContextMenuStrip = menu;

            // Keep the icon right when the theme is changed elsewhere (Settings, hotkeys).
            SystemEvents.UserPreferenceChanged += delegate { refresh(); };

            Application.Run();
            tray.Dispose();
        }
    }
}
