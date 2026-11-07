using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Theme Toggle")]
[assembly: AssemblyDescription("Tray icon that toggles Windows light/dark theme")]
[assembly: AssemblyProduct("Theme Toggle")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

// Tray icon: left-click toggles Windows light/dark. Right-click: autostart / exit.
// "ThemeToggle.exe --toggle" toggles once and exits (for scripts/shortcuts).
static class ThemeToggle
{
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessageTimeout(IntPtr h, uint m, UIntPtr w, string l, uint f, uint t, out UIntPtr r);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    static bool IsLight()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Personalize))
            return Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 1;
    }

    static void Toggle() { SetTheme(!IsLight()); }

    static void SetTheme(bool light)
    {
        int v = light ? 1 : 0;
        using (var k = Registry.CurrentUser.OpenSubKey(Personalize, true))
        {
            k.SetValue("AppsUseLightTheme", v, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", v, RegistryValueKind.DWord);
        }
        // shortcut: fixed 300 ms gap before the 2nd broadcast, raise if some shells still lag.
        Broadcast();
        Thread.Sleep(300);
        Broadcast();
    }

    // Tell every window, and each monitor's taskbar directly, to re-read the theme.
    // The plain broadcast alone left secondary-monitor taskbars unchanged on some PCs.
    static void Broadcast()
    {
        UIntPtr r;
        SendMessageTimeout((IntPtr)0xffff, 0x1A, UIntPtr.Zero, "ImmersiveColorSet", 2, 5000, out r);
        foreach (string cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, cls, null)) != IntPtr.Zero)
                SendMessageTimeout(h, 0x1A, UIntPtr.Zero, "ImmersiveColorSet", 2, 2000, out r);
        }
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

    // ---- Schedule: light from LightAt until DarkAt, dark otherwise (may span midnight). ----
    const string AppKey = @"Software\ThemeToggle";
    static bool SchedOn;
    static TimeSpan LightAt = new TimeSpan(7, 0, 0), DarkAt = new TimeSpan(19, 0, 0);

    static TimeSpan ReadTime(RegistryKey k, string name, TimeSpan fallback)
    {
        TimeSpan t;
        return TimeSpan.TryParse(k.GetValue(name) as string, CultureInfo.InvariantCulture, out t) ? t : fallback;
    }

    static void LoadSchedule()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(AppKey))
        {
            SchedOn = Convert.ToInt32(k.GetValue("ScheduleOn", 0)) == 1;
            LightAt = ReadTime(k, "LightAt", LightAt);
            DarkAt = ReadTime(k, "DarkAt", DarkAt);
        }
    }

    static void SaveSchedule()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(AppKey))
        {
            k.SetValue("ScheduleOn", SchedOn ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("LightAt", LightAt.ToString(@"hh\:mm"));
            k.SetValue("DarkAt", DarkAt.ToString(@"hh\:mm"));
        }
    }

    static bool ScheduledLight(TimeSpan t)
    {
        return LightAt < DarkAt ? (t >= LightAt && t < DarkAt) : (t >= LightAt || t < DarkAt);
    }

    // Returns true if the user saved changes.
    static bool ShowScheduleDialog()
    {
        var f = new Form
        {
            Text = "Theme Toggle schedule", FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false,
            ClientSize = new Size(300, 170), ShowInTaskbar = true
        };
        var on = new CheckBox { Text = "Switch automatically at these times", Left = 16, Top = 16, Width = 270, Checked = SchedOn };
        Func<string, TimeSpan, int, DateTimePicker> row = delegate(string label, TimeSpan t, int top)
        {
            f.Controls.Add(new Label { Text = label, Left = 16, Top = top + 4, Width = 120 });
            var p = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true,
                Left = 150, Top = top, Width = 80, Value = DateTime.Today + t
            };
            f.Controls.Add(p);
            return p;
        };
        var light = row("Light mode at", LightAt, 52);
        var dark = row("Dark mode at", DarkAt, 88);
        var ok = new Button { Text = "Save", Left = 130, Top = 130, Width = 75 };
        var cancel = new Button { Text = "Cancel", Left = 212, Top = 130, Width = 75, DialogResult = DialogResult.Cancel };
        ok.Click += delegate
        {
            if (light.Value.TimeOfDay == dark.Value.TimeOfDay)
            {
                MessageBox.Show("Light and dark times must be different.", "Theme Toggle");
                return;
            }
            SchedOn = on.Checked; LightAt = light.Value.TimeOfDay; DarkAt = dark.Value.TimeOfDay;
            SaveSchedule();
            f.DialogResult = DialogResult.OK;
        };
        f.AcceptButton = ok; f.CancelButton = cancel;
        f.Controls.AddRange(new Control[] { on, ok, cancel });
        using (f) return f.ShowDialog() == DialogResult.OK;
    }

    // ---- Updater: checks the latest GitHub release and, if newer and the user agrees, swaps the exe. ----
    const string Repo = "mo3shma/windows-theme-toggle";

    static bool AutoUpdate
    {
        get { using (var k = Registry.CurrentUser.CreateSubKey(AppKey)) return Convert.ToInt32(k.GetValue("AutoUpdate", 1)) == 1; }
        set { using (var k = Registry.CurrentUser.CreateSubKey(AppKey)) k.SetValue("AutoUpdate", value ? 1 : 0, RegistryValueKind.DWord); }
    }

    static WebClient NewClient()
    {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2; .NET 4.0 has no enum name for it
        var c = new WebClient();
        c.Headers[HttpRequestHeader.UserAgent] = "ThemeToggle/" + Application.ProductVersion;
        return c;
    }

    // Returns { tag, downloadUrl } of the latest release, or null if it can't be read.
    static string[] FetchLatest()
    {
        using (var c = NewClient())
        {
            string j = c.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
            var tag = Regex.Match(j, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            var url = Regex.Match(j, "\"browser_download_url\"\\s*:\\s*\"(https://github\\.com/" + Regex.Escape(Repo) + "/[^\"]*ThemeToggle\\.exe)\"");
            return tag.Success && url.Success ? new[] { tag.Groups[1].Value, url.Groups[1].Value } : null;
        }
    }

    // Downloads the new exe and swaps it in for `exe` (the running file is renamed to .old, which Windows allows).
    // shortcut: blocks the UI thread for the ~20 KB download, move to a worker if the exe grows large.
    static void ReplaceExe(string url, string exe)
    {
        string tmp = exe + ".new", old = exe + ".old";
        try
        {
            using (var c = NewClient()) c.DownloadFile(url, tmp);
            var head = new byte[2];
            using (var s = File.OpenRead(tmp))
                if (s.Length < 4096 || s.Read(head, 0, 2) != 2 || head[0] != 'M' || head[1] != 'Z')
                    throw new IOException("Downloaded file is not a valid program.");
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(tmp, exe); }
            catch { File.Move(old, exe); throw; }   // never leave the user without an exe
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    static void CheckForUpdates(bool manual)
    {
        var w = new BackgroundWorker();
        w.DoWork += (s, e) => { e.Result = FetchLatest(); };
        w.RunWorkerCompleted += (s, e) =>
        {
            var r = e.Error == null ? e.Result as string[] : null;
            Version latest, cur = new Version(Application.ProductVersion);
            if (r == null || !Version.TryParse(r[0].TrimStart('v'), out latest))
            {
                if (manual) MessageBox.Show("Couldn't check for updates. Check your internet connection and try again.", "Theme Toggle");
                return;
            }
            if (latest <= cur)
            {
                if (manual) MessageBox.Show("You're up to date (version " + cur.ToString(3) + ").", "Theme Toggle");
                return;
            }
            string skipped;
            using (var k = Registry.CurrentUser.CreateSubKey(AppKey)) skipped = k.GetValue("SkipVersion") as string;
            if (!manual && skipped == r[0]) return;

            var answer = MessageBox.Show("Theme Toggle " + r[0] + " is available (you have " + cur.ToString(3) + ").\n\nDownload and install it now?",
                "Theme Toggle update", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes)
            {
                if (!manual) using (var k = Registry.CurrentUser.CreateSubKey(AppKey)) k.SetValue("SkipVersion", r[0]);
                return;
            }
            try
            {
                ReplaceExe(r[1], Application.ExecutablePath);
                Process.Start(Application.ExecutablePath, "--restarted");
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't update automatically (" + ex.Message + "). Opening the download page instead.", "Theme Toggle");
                Process.Start("https://github.com/" + Repo + "/releases/latest");
            }
        };
        w.RunWorkerAsync();
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--toggle") { Toggle(); return; }

        bool first = false;
        Mutex mutex = null;
        // After an update the old instance is still exiting: keep retrying for ~10 s until it releases the mutex.
        for (int i = args.Length > 0 && args[0] == "--restarted" ? 40 : 1; i > 0 && !first; i--)
        {
            mutex = new Mutex(true, "ThemeToggleTray_v2", out first);
            if (!first) { mutex.Dispose(); if (i > 1) Thread.Sleep(250); }
        }
        if (!first) return;
        using (mutex)
        {
            try { File.Delete(Application.ExecutablePath + ".old"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Application.EnableVisualStyles();
            FirstRunSetup();

            var tray = new NotifyIcon { Icon = MakeIcon(), Text = "Click: toggle light/dark", Visible = true };
            Action refresh = delegate { var old = tray.Icon; tray.Icon = MakeIcon(); old.Dispose(); };

            // Apply the schedule when its desired state changes, so a manual click stays until the next switch time.
            LoadSchedule();
            bool? last = null;
            Action tick = delegate
            {
                if (!SchedOn) { last = null; return; }
                bool want = ScheduledLight(DateTime.Now.TimeOfDay);
                if (last == want) return;
                last = want;
                if (IsLight() != want) { SetTheme(want); refresh(); }
            };

            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { Toggle(); refresh(); }
            };

            var menu = new ContextMenuStrip();
            var auto = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
            menu.Opening += delegate { auto.Checked = Autostart; };
            auto.Click += delegate { SetAutostart(auto.Checked); };
            menu.Items.Add("Schedule...", null, delegate
            {
                if (ShowScheduleDialog()) { last = null; tick(); }
            });
            var autoUpd = new ToolStripMenuItem("Check for updates automatically") { CheckOnClick = true };
            autoUpd.Click += delegate { AutoUpdate = autoUpd.Checked; };
            menu.Opening += delegate { autoUpd.Checked = AutoUpdate; };
            menu.Items.Add("Check for updates now", null, delegate { CheckForUpdates(true); });
            menu.Items.Add(autoUpd);
            menu.Items.Add(auto);
            menu.Items.Add("Exit", null, delegate { tray.Visible = false; Application.Exit(); });
            tray.ContextMenuStrip = menu;

            // Keep the icon right when the theme is changed elsewhere (Settings, hotkeys).
            SystemEvents.UserPreferenceChanged += delegate { refresh(); };

            var timer = new System.Windows.Forms.Timer { Interval = 30000 };
            timer.Tick += delegate { tick(); };
            timer.Start();
            tick();

            // First check shortly after start, then daily.
            var upd = new System.Windows.Forms.Timer { Interval = 15000 };
            upd.Tick += delegate { upd.Interval = 24 * 3600 * 1000; if (AutoUpdate) CheckForUpdates(false); };
            upd.Start();

            Application.Run();
            upd.Dispose();
            timer.Dispose();
            tray.Dispose();
        }
    }
}
