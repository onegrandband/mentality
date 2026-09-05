using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace Mentality
{
    public partial class Form1 : Form
    {
        // Controls
        private Panel mainPanel;
        private Label lblStatus;
        private ProgressBar progressBar;

        private Label lblInterval;
        private NumericUpDown nudIntervalMs;
        private TrackBar trackInterval;

        private Label lblButton;
        private RadioButton rdoLeftButton;
        private RadioButton rdoRightButton;

        private Label lblStartDelay;
        private NumericUpDown nudStartDelay;

        private CheckBox chkAutoMinimize;
        private CheckBox chkRunWhenMinimized;
        private CheckBox chkUseHotkey;
        private TextBox txtHotkey;
        private Label lblHotkeyHint;
        private Keys selectedHotkey = Keys.None;
        private uint selectedHotkeyModifiers = 0;

        private CheckBox chkLimitToSpecificWindow;
        private TextBox txtTargetGameWindows;

        private Button btnStartClicker;
        private Button btnStopClicker;

        // new controls: profiles, scheduling, jitter, pause detection
        private ComboBox cmbProfiles;
        private Button btnSaveProfile;
        private Button btnLoadProfile;
        private Button btnDeleteProfile;
        private CheckBox chkSchedule;
        private DateTimePicker dtpStartTime;
        private NumericUpDown nudRunMinutes;
        private NumericUpDown nudJitterPercent;
        private CheckBox chkPauseOnInput;
        private NumericUpDown nudPauseIdleSeconds;

        // runtime fields
        private Random rng = new Random();
        private System.Collections.Generic.List<Profile> profiles = new System.Collections.Generic.List<Profile>();

        // fields
        private CancellationTokenSource autoClickerCts;
        private Task autoClickerTask;
        private volatile bool isClicking;

        // target monitoring
        private Task targetMonitorTask;
        private CancellationTokenSource targetMonitorCts;
        private volatile bool isTargetAvailable = true;

        // native
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0x9000;

        // RegisterHotKey modifier flags
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public Form1()
        {
            InitializeComponent();
            BuildUi();
            LoadState();
            this.FormClosing += Form1_FormClosing;
        }

        // Minimal InitializeComponent to satisfy WinForms designer expectations
        private void InitializeComponent()
        {
            // No designer-generated controls; leave minimal setup
            this.SuspendLayout();
            this.ClientSize = new System.Drawing.Size(800, 600);
            this.Name = "Form1";
            this.ResumeLayout(false);
        }

        private void BuildUi()
        {
            this.Text = "Mentality v1.2.1";
            this.Size = new Size(900, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;

            // Try load icon if present
            TrySetAppIcon();

            // main panel with scrolling
            mainPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            lblStatus = new Label { Text = "Ready", Location = new Point(8, 8), Size = new Size(820, 24), Font = new Font("Segoe UI", 10F) };
            progressBar = new ProgressBar { Location = new Point(8, 36), Size = new Size(820, 16), Visible = false };

            int y = 64;

            lblInterval = new Label { Text = "Interval (ms):", Location = new Point(8, y), Size = new Size(100, 22) };
            nudIntervalMs = new NumericUpDown { Location = new Point(112, y - 2), Size = new Size(100, 24), Minimum = 0, Maximum = 10000, Value = 10 };
            trackInterval = new TrackBar { Location = new Point(220, y - 8), Size = new Size(360, 45), Minimum = 0, Maximum = 1000, Value = 10, TickFrequency = 50 };
            trackInterval.Scroll += (s, e) => { try { nudIntervalMs.Value = Math.Min(nudIntervalMs.Maximum, Math.Max(nudIntervalMs.Minimum, trackInterval.Value)); } catch { } };
            nudIntervalMs.ValueChanged += (s, e) => { try { trackInterval.Value = (int)Math.Min(trackInterval.Maximum, Math.Max(trackInterval.Minimum, (int)nudIntervalMs.Value)); } catch { } };

            y += 60;
            lblButton = new Label { Text = "Mouse Button:", Location = new Point(8, y), Size = new Size(100, 22) };
            rdoLeftButton = new RadioButton { Text = "Left", Location = new Point(120, y), Checked = true };
            rdoRightButton = new RadioButton { Text = "Right", Location = new Point(180, y) };

            y += 34;
            // passphrase removed for security and to avoid malware-like behavior

            y += 34;
            lblStartDelay = new Label { Text = "Start delay (s):", Location = new Point(8, y), Size = new Size(100, 22) };
            nudStartDelay = new NumericUpDown { Location = new Point(120, y - 2), Size = new Size(80, 24), Minimum = 0, Maximum = 3600, Value = 0 };

            y += 36;
            chkAutoMinimize = new CheckBox { Text = "Auto-minimize when running", Location = new Point(8, y), Size = new Size(200, 22), Checked = true };
            chkRunWhenMinimized = new CheckBox { Text = "Keep clicking when minimized", Location = new Point(220, y), Size = new Size(200, 22), Checked = true };

            y += 30;
            chkUseHotkey = new CheckBox { Text = "Use global hotkey", Location = new Point(8, y), Size = new Size(140, 22) };
            txtHotkey = new TextBox { Location = new Point(152, y - 2), Size = new Size(200, 24), ReadOnly = true, TabStop = true };
            txtHotkey.BackColor = SystemColors.Window;
            // Click to begin capture; then press a key combination (e.g., Ctrl+Shift+A)
            txtHotkey.Click += (s, e) => { try { txtHotkey.ReadOnly = false; txtHotkey.Text = "Press combination..."; txtHotkey.Focus(); } catch { } };
            txtHotkey.KeyDown += (s, e) =>
            {
                try
                {
                    e.SuppressKeyPress = true;
                    // ignore pure modifier keys as the main key
                    var key = e.KeyCode;
                    if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                    {
                        // just update the display to reflect modifiers pressed
                        var parts = new System.Collections.Generic.List<string>();
                        if (e.Control) parts.Add("Ctrl");
                        if (e.Shift) parts.Add("Shift");
                        if (e.Alt) parts.Add("Alt");
                        txtHotkey.Text = string.Join("+", parts);
                        return;
                    }

                    // compute modifiers
                    uint mods = 0;
                    if (e.Control) mods |= MOD_CONTROL;
                    if (e.Shift) mods |= MOD_SHIFT;
                    if (e.Alt) mods |= MOD_ALT;
                    if (IsWinKeyDown()) mods |= MOD_WIN;

                    selectedHotkeyModifiers = mods;
                    selectedHotkey = key;

                    var names = new System.Collections.Generic.List<string>();
                    if ((mods & MOD_CONTROL) != 0) names.Add("Ctrl");
                    if ((mods & MOD_SHIFT) != 0) names.Add("Shift");
                    if ((mods & MOD_ALT) != 0) names.Add("Alt");
                    names.Add(selectedHotkey.ToString());
                    txtHotkey.Text = string.Join("+", names);

                    txtHotkey.ReadOnly = true;
                    // persist
                                        try
                                        {
                                            Properties.Settings.Default.HotkeyKey = selectedHotkey == Keys.None ? string.Empty : selectedHotkey.ToString();
                                            Properties.Settings.Default.HotkeyModifiers = (int)selectedHotkeyModifiers;
                                            Properties.Settings.Default.Save();
                                            // attempt to register immediately
                                            TryRegisterHotkey();
                                        }
                                        catch { }
                                    }
                                    catch { }
                                };

            y += 34;
            chkLimitToSpecificWindow = new CheckBox { Text = "Only when target window is active", Location = new Point(8, y), Size = new Size(260, 22) };
            txtTargetGameWindows = new TextBox { Location = new Point(280, y - 2), Size = new Size(300, 24) };
            var lblAllowedHint = new Label { Text = "Comma-separated window titles or process names (no .exe)", Location = new Point(280, y + 26), Size = new Size(500, 18), ForeColor = Color.Gray };

            y += 60;
            // Profiles UI
            cmbProfiles = new ComboBox { Location = new Point(8, y), Size = new Size(220, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            btnLoadProfile = new Button { Text = "Load", Location = new Point(236, y), Size = new Size(60, 24) };
            btnLoadProfile.Click += (s, e) => { try { if (cmbProfiles.SelectedItem != null) { var profileName = cmbProfiles.SelectedItem.ToString(); var p = profiles.Find(x => x.Name == profileName); if (p != null) ApplyProfile(p); lblStatus.Text = $"Profile '{profileName}' applied."; Audit($"Profile '{profileName}' loaded"); } } catch { } };
            btnSaveProfile = new Button { Text = "Save", Location = new Point(300, y), Size = new Size(60, 24) };
            btnSaveProfile.Click += (s, e) => { try { var profileName = Prompt("Save Profile","Profile name:","New Profile"); if (!string.IsNullOrWhiteSpace(profileName)) { var p = new Profile { Name = profileName, IntervalMs = (int)nudIntervalMs.Value, RightButton = rdoRightButton.Checked, StartDelay = (int)nudStartDelay.Value, AutoMinimize = chkAutoMinimize.Checked, RunWhenMinimized = chkRunWhenMinimized.Checked, UseHotkey = chkUseHotkey.Checked, HotkeyKey = selectedHotkey==Keys.None?string.Empty:selectedHotkey.ToString(), HotkeyModifiers = (int)selectedHotkeyModifiers, LimitToSpecificWindow = chkLimitToSpecificWindow.Checked, TargetWindows = txtTargetGameWindows.Text, JitterPercent = (int)nudJitterPercent.Value, PauseOnUserInput = chkPauseOnInput.Checked }; profiles.RemoveAll(x=>x.Name==profileName); profiles.Add(p); ProfilesManager.SaveProfiles(profiles); RefreshProfilesCombo(); lblStatus.Text = $"Saved profile '{profileName}'."; Audit($"Profile '{profileName}' saved"); } } catch { } };
            btnDeleteProfile = new Button { Text = "Delete", Location = new Point(364, y), Size = new Size(60, 24) };
            btnDeleteProfile.Click += (s, e) => { try { if (cmbProfiles.SelectedItem != null) { var profileName = cmbProfiles.SelectedItem.ToString(); if (MessageBox.Show($"Delete profile '{profileName}'?","Confirm",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes) { profiles.RemoveAll(x=>x.Name==profileName); ProfilesManager.SaveProfiles(profiles); RefreshProfilesCombo(); lblStatus.Text = $"Deleted profile '{profileName}'"; Audit($"Profile '{profileName}' deleted"); } } } catch { } };

            btnStartClicker = new Button { Text = "Start Clicking", Location = new Point(8, y+40), Size = new Size(140, 40) };
            btnStartClicker.Click += (s, e) => StartAutoClicker();
            btnStopClicker = new Button { Text = "Stop Clicking", Location = new Point(160, y+40), Size = new Size(140, 40), Enabled = false };
            btnStopClicker.Click += (s, e) => StopAutoClicker();

            // clear hotkey button
            var btnClearHotkey = new Button { Text = "Clear hotkey", Location = new Point(320, y+40 - 2), Size = new Size(100, 24) };
            btnClearHotkey.Click += (s, e) =>
            {
                try
                {
                    selectedHotkey = Keys.None;
                    selectedHotkeyModifiers = 0;
                    txtHotkey.Text = string.Empty;
                    Properties.Settings.Default.HotkeyKey = string.Empty;
                    Properties.Settings.Default.HotkeyModifiers = 0;
                    Properties.Settings.Default.Save();
                    try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { }
                    lblStatus.Text = "Hotkey cleared.";
                    Audit("Hotkey cleared by user");
                }
                catch { }
            };

            // Misc group
            var grpMisc = new GroupBox { Text = "Misc (advanced)", Location = new Point(8, y+92), Size = new Size(760, 180) };

            chkSchedule = new CheckBox { Text = "Schedule start", Location = new Point(12, 20), Size = new Size(120, 22) };
            dtpStartTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Location = new Point(140, 20), Size = new Size(200, 24) };
            nudRunMinutes = new NumericUpDown { Location = new Point(348, 20), Size = new Size(80, 24), Minimum = 0, Maximum = 10080, Value = 0 };
            var lblRunMinutes = new Label { Text = "Run minutes (0=indef):", Location = new Point(436, 24), Size = new Size(140, 18) };

            nudJitterPercent = new NumericUpDown { Location = new Point(12, 56), Size = new Size(80, 24), Minimum = 0, Maximum = 100, Value = 0 };
            var lblJitter = new Label { Text = "Jitter %:", Location = new Point(100, 60), Size = new Size(60, 18) };

            chkPauseOnInput = new CheckBox { Text = "Pause on user input", Location = new Point(164, 56), Size = new Size(140, 22) };
            nudPauseIdleSeconds = new NumericUpDown { Location = new Point(308, 56), Size = new Size(80, 24), Minimum = 1, Maximum = 3600, Value = 3 };
            var lblPauseHint = new Label { Text = "Idle seconds to consider user active:", Location = new Point(396, 60), Size = new Size(220, 18) };

            // target-app behavior note
            var lblTargetNote = new Label { Text = "When target app is closed or minimized the clicker pauses; it will resume automatically when the app is restored.", Location = new Point(12, 92), Size = new Size(720, 34), ForeColor = Color.DarkBlue };

            grpMisc.Controls.AddRange(new Control[] { chkSchedule, dtpStartTime, nudRunMinutes, lblRunMinutes, nudJitterPercent, lblJitter, chkPauseOnInput, nudPauseIdleSeconds, lblPauseHint, lblTargetNote });


            mainPanel.Controls.Add(lblStatus);
            mainPanel.Controls.Add(progressBar);
            mainPanel.Controls.Add(lblInterval);
            mainPanel.Controls.Add(nudIntervalMs);
            mainPanel.Controls.Add(trackInterval);
            mainPanel.Controls.Add(lblButton);
            mainPanel.Controls.Add(rdoLeftButton);
            mainPanel.Controls.Add(rdoRightButton);
            mainPanel.Controls.Add(lblStartDelay);
            mainPanel.Controls.Add(nudStartDelay);
            mainPanel.Controls.Add(chkAutoMinimize);
            mainPanel.Controls.Add(chkRunWhenMinimized);
            mainPanel.Controls.Add(chkUseHotkey);
            mainPanel.Controls.Add(txtHotkey);
            mainPanel.Controls.Add(lblHotkeyHint);
            mainPanel.Controls.Add(cmbProfiles);
            mainPanel.Controls.Add(btnLoadProfile);
            mainPanel.Controls.Add(btnSaveProfile);
            mainPanel.Controls.Add(btnDeleteProfile);
            mainPanel.Controls.Add(btnClearHotkey);
            mainPanel.Controls.Add(chkLimitToSpecificWindow);
            mainPanel.Controls.Add(txtTargetGameWindows);
            mainPanel.Controls.Add(lblAllowedHint);
            mainPanel.Controls.Add(btnStartClicker);
            mainPanel.Controls.Add(btnStopClicker);
            mainPanel.Controls.Add(grpMisc);

            // footer
            var sep = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.Gainsboro };
            var footerLabel = new Label { Text = "Status:", Location = new Point(8, 4), AutoSize = true };
            sep.Controls.Add(footerLabel);

            this.Controls.Add(sep);
            this.Controls.Add(mainPanel);
        }

        private void TryRegisterHotkey()
        {
            try
            {
                try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { }
                var vk = GetSelectedHotkeyVk();
                var mods = GetSelectedHotkeyModifiers();
                if (vk != 0 && chkUseHotkey != null && chkUseHotkey.Checked)
                {
                    if (!RegisterHotKey(this.Handle, HOTKEY_ID, mods, vk))
                    {
                        var err = Marshal.GetLastWin32Error();
                        var friendly = "Hotkey registration failed";
                        if (err == 1409) friendly += ": that combination is reserved by the system or another program.";
                        else friendly += $" (error {err}).";
                        LogError(new Exception($"RegisterHotKey failed ({err})"));
                        this.Invoke(new Action(() => lblStatus.Text = friendly));
                        Audit($"Hotkey registration failed (err={err})");
                        HotkeyLog($"RegisterHotKey failed err={err}, mods={mods}, vk={vk}");
                        try { this.Invoke(new Action(() => MessageBox.Show(this, friendly + " Try a different combination like Ctrl+Shift+F8.", "Hotkey failed", MessageBoxButtons.OK, MessageBoxIcon.Warning))); } catch { }
                    }
                    else
                    {
                        try { this.Invoke(new Action(() => lblStatus.Text = "Hotkey registered.")); } catch { }
                        Audit($"Hotkey registered: {mods}+{vk}");
                        HotkeyLog($"RegisterHotKey succeeded mods={mods}, vk={vk}");
                        // also write a lightweight Temp UI notification if possible
                        try { this.Invoke(new Action(() => { var t = new ToolTip(); t.Show("Hotkey active", txtHotkey, 2000); })); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private void StartTargetMonitor()
        {
            try
            {
                StopTargetMonitor();
                targetMonitorCts = new CancellationTokenSource();
                var token = targetMonitorCts.Token;
                targetMonitorTask = Task.Run(() =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            bool found = false;
                            var entries = txtTargetGameWindows.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < entries.Length && !found; i++)
                            {
                                var e = entries[i].Trim();
                                if (string.IsNullOrEmpty(e)) continue;
                                try
                                {
                                    foreach (var p in Process.GetProcesses())
                                    {
                                        try
                                        {
                                            if (!string.IsNullOrEmpty(p.ProcessName) && p.ProcessName.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                var h = p.MainWindowHandle;
                                                if (h != IntPtr.Zero && !IsIconic(h)) { found = true; break; }
                                            }
                                            if (!string.IsNullOrEmpty(p.MainWindowTitle) && p.MainWindowTitle.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                var h = p.MainWindowHandle;
                                                if (h != IntPtr.Zero && !IsIconic(h)) { found = true; break; }
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                catch { }
                            }

                            isTargetAvailable = found;
                            if (!found) this.Invoke(new Action(() => lblStatus.Text = "Waiting for target app..."));
                            Thread.Sleep(1000);
                        }
                    }
                    catch { }
                }, token);
            }
            catch { }
        }

        private void StopTargetMonitor()
        {
            try
            {
                if (targetMonitorCts != null)
                {
                    targetMonitorCts.Cancel();
                    targetMonitorCts = null;
                }
            }
            catch { }
        }

        private string Prompt(string title, string promptText, string defaultValue)
        {
            try
            {
                using (var f = new Form())
                {
                    f.Text = title;
                    f.FormBorderStyle = FormBorderStyle.FixedDialog;
                    f.StartPosition = FormStartPosition.CenterParent;
                    f.Width = 420;
                    f.Height = 140;

                    var lbl = new Label() { Left = 8, Top = 8, Text = promptText, AutoSize = true };
                    var txt = new TextBox() { Left = 8, Top = 28, Width = 384, Text = defaultValue };
                    var ok = new Button() { Text = "OK", Left = 236, Width = 75, Top = 60, DialogResult = DialogResult.OK };
                    var cancel = new Button() { Text = "Cancel", Left = 320, Width = 75, Top = 60, DialogResult = DialogResult.Cancel };
                    f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                    f.AcceptButton = ok;
                    f.CancelButton = cancel;

                    if (f.ShowDialog() == DialogResult.OK) return txt.Text;
                }
            }
            catch { }
            return null;
        }

        private void TrySetAppIcon()
        {
            try
            {
                var ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mentality.ico");
                if (File.Exists(ico))
                {
                    this.Icon = new Icon(ico);
                    return;
                }

                var png = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "img_5635.png");
                if (File.Exists(png))
                {
                    using (var bmp = new Bitmap(png))
                    {
                        IntPtr hIcon = bmp.GetHicon();
                        var icon = Icon.FromHandle(hIcon);
                        var cloned = (Icon)icon.Clone();
                        this.Icon = cloned;
                        DestroyIcon(hIcon);
                    }
                }
            }
            catch { }
        }

        // Load UI state and profiles from disk
        private void LoadState()
        {
            try
            {
                // load hotkey
                try
                {
                    var keyName = Properties.Settings.Default.HotkeyKey;
                    var mods = Properties.Settings.Default.HotkeyModifiers;
                    if (!string.IsNullOrEmpty(keyName))
                    {
                        if (Enum.TryParse<Keys>(keyName, out Keys kval))
                        {
                            selectedHotkey = kval;
                            selectedHotkeyModifiers = (uint)mods;
                            var parts = new System.Collections.Generic.List<string>();
                            if ((selectedHotkeyModifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
                            if ((selectedHotkeyModifiers & MOD_SHIFT) != 0) parts.Add("Shift");
                            if ((selectedHotkeyModifiers & MOD_ALT) != 0) parts.Add("Alt");
                            // Win not shown in capture name
                            parts.Add(selectedHotkey.ToString());
                            txtHotkey.Text = string.Join("+", parts);
                        }
                    }
                }
                catch { }

                // load profiles
                profiles = ProfilesManager.LoadProfiles();
                RefreshProfilesCombo();

                // register saved hotkey if present and enabled
                try
                {
                    if (!string.IsNullOrEmpty(Properties.Settings.Default.HotkeyKey))
                    {
                        chkUseHotkey.Checked = true;
                        TryRegisterHotkey();
                    }
                }
                catch { }
            }
            catch { }
        }

        private void RefreshProfilesCombo()
        {
            try
            {
                cmbProfiles.Items.Clear();
                foreach (var p in profiles) cmbProfiles.Items.Add(p.Name);
                if (cmbProfiles.Items.Count > 0) cmbProfiles.SelectedIndex = 0;
            }
            catch { }
        }

        private void ApplyProfile(Profile p)
        {
            try
            {
                if (p == null) return;
                nudIntervalMs.Value = Math.Min(nudIntervalMs.Maximum, Math.Max(nudIntervalMs.Minimum, p.IntervalMs));
                rdoRightButton.Checked = p.RightButton;
                rdoLeftButton.Checked = !p.RightButton;
                nudStartDelay.Value = Math.Min(nudStartDelay.Maximum, Math.Max(nudStartDelay.Minimum, p.StartDelay));
                chkAutoMinimize.Checked = p.AutoMinimize;
                chkRunWhenMinimized.Checked = p.RunWhenMinimized;
                chkUseHotkey.Checked = p.UseHotkey;
                if (!string.IsNullOrEmpty(p.HotkeyKey) && Enum.TryParse<Keys>(p.HotkeyKey, out Keys kv)) selectedHotkey = kv;
                selectedHotkeyModifiers = (uint)p.HotkeyModifiers;
                chkLimitToSpecificWindow.Checked = p.LimitToSpecificWindow;
                txtTargetGameWindows.Text = p.TargetWindows;
                nudJitterPercent.Value = Math.Min(nudJitterPercent.Maximum, Math.Max(nudJitterPercent.Minimum, p.JitterPercent));
                chkPauseOnInput.Checked = p.PauseOnUserInput;

                // update hotkey display
                try
                {
                    var parts = new System.Collections.Generic.List<string>();
                    if ((selectedHotkeyModifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
                    if ((selectedHotkeyModifiers & MOD_SHIFT) != 0) parts.Add("Shift");
                    if ((selectedHotkeyModifiers & MOD_ALT) != 0) parts.Add("Alt");
                    if ((selectedHotkeyModifiers & MOD_WIN) != 0) parts.Add("Win");
                    if (selectedHotkey != Keys.None) parts.Add(selectedHotkey.ToString());
                    txtHotkey.Text = string.Join("+", parts);
                }
                catch { }
            }
            catch { }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopAutoClicker();
            try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private uint GetIdleTimeMs()
        {
            try
            {
                var li = new LASTINPUTINFO();
                li.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (!GetLastInputInfo(ref li)) return 0;
                return (uint)Environment.TickCount - li.dwTime;
            }
            catch { return 0; }
        }

        private void Audit(string message)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "Mentality");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "audit.log");
                File.AppendAllText(file, $"[{DateTime.Now}] {message}\r\n");
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                // log reception for diagnostics
                try { Audit($"WM_HOTKEY received (id={HOTKEY_ID})"); } catch { }
                // toggle
                if (isClicking) StopAutoClicker(); else StartAutoClicker();
                return;
            }
            base.WndProc(ref m);
        }

        private uint GetSelectedHotkeyVk()
        {
            if (selectedHotkey == Keys.None) return 0;
            return (uint)selectedHotkey;
        }

        private uint GetSelectedHotkeyModifiers()
        {
            return selectedHotkeyModifiers;
        }

        private bool IsWinKeyDown()
        {
            try
            {
                return ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0) || ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0);
            }
            catch { return false; }
        }

        // Lightweight diagnostic log helper for hotkey events
        private void HotkeyLog(string message)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "Mentality");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "hotkey_diag.log");
                File.AppendAllText(file, $"[{DateTime.Now}] {message}\r\n");
            }
            catch { }
        }

        private string GetForegroundProcessName()
        {
            try
            {
                var h = GetForegroundWindow();
                if (h == IntPtr.Zero) return null;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                var p = Process.GetProcessById((int)pid);
                return p?.ProcessName ?? null;
            }
            catch { return null; }
        }

        private void StartAutoClicker()
        {
            if (isClicking) return;
            isClicking = true;
            btnStartClicker.Enabled = false;
            btnStopClicker.Enabled = true;
            progressBar.Visible = true;
            lblStatus.Text = "Starting...";

            // register hotkey if requested
            if (chkUseHotkey.Checked)
            {
                try
                {
                    var vk = GetSelectedHotkeyVk();
                    var mods = GetSelectedHotkeyModifiers();
                    if (vk != 0)
                    {
                        UnregisterHotKey(this.Handle, HOTKEY_ID);
                        if (!RegisterHotKey(this.Handle, HOTKEY_ID, mods, vk))
                        {
                            var err = Marshal.GetLastWin32Error();
                            var friendly = "Hotkey registration failed";
                            if (err == 1409) friendly += ": that combination is reserved by the system or another program.";
                            else friendly += $" (error {err}).";
                            LogError(new Exception($"RegisterHotKey failed ({err})"));
                            this.Invoke(new Action(() => lblStatus.Text = friendly));
                            Audit($"Hotkey registration failed (err={err})");
                            // show balloon tip suggestion if running with an interactive UI
                            try { this.Invoke(new Action(() => MessageBox.Show(this, friendly + " Try a different combination like Ctrl+Shift+F8.", "Hotkey failed", MessageBoxButtons.OK, MessageBoxIcon.Warning))); } catch { }
                        }
                        else
                        {
                            Audit($"Hotkey registered: {mods}+{vk}");
                            try { this.Invoke(new Action(() => lblStatus.Text = "Hotkey registered.")); } catch { }
                        }
                    }
                }
                catch (Exception ex) { LogError(ex); }
            }

            autoClickerCts = new CancellationTokenSource();
            var token = autoClickerCts.Token;

            // capture schedule settings
            DateTime? scheduledStart = null;
            DateTime? scheduledEnd = null;
            if (chkSchedule != null && chkSchedule.Checked)
            {
                scheduledStart = dtpStartTime.Value;
                if (nudRunMinutes != null && nudRunMinutes.Value > 0)
                {
                    scheduledEnd = scheduledStart.Value.AddMinutes((double)nudRunMinutes.Value);
                }
            }

            // start target monitor if targeting specific apps
            try { if (chkLimitToSpecificWindow != null && chkLimitToSpecificWindow.Checked) StartTargetMonitor(); } catch { }

            autoClickerTask = Task.Run(() =>
            {
                try
                {
                    // wait until scheduled start if needed
                    if (scheduledStart.HasValue && scheduledStart.Value > DateTime.Now)
                    {
                        var waitMs = (int)(scheduledStart.Value - DateTime.Now).TotalMilliseconds;
                        if (waitMs > 0)
                        {
                            this.Invoke(new Action(() => lblStatus.Text = $"Waiting to start at {scheduledStart.Value}"));
                            var waited = 0;
                            while (waited < waitMs && !token.IsCancellationRequested)
                            {
                                Thread.Sleep(Math.Min(1000, waitMs - waited));
                                waited += Math.Min(1000, waitMs - waited);
                            }
                        }
                    }

                    // optional start delay
                    int delay = 0;
                    try { this.Invoke(new Action(() => delay = (int)nudStartDelay.Value)); } catch { }
                    for (int i = 0; i < delay && !token.IsCancellationRequested; i++)
                    {
                        this.Invoke(new Action(() => lblStatus.Text = $"Starting in {delay - i}...") );
                        Thread.Sleep(1000);
                    }

                    // auto-minimize
                    if (chkAutoMinimize.Checked)
                    {
                        try { this.Invoke(new Action(() => this.WindowState = FormWindowState.Minimized)); } catch { }
                    }

                    while (!token.IsCancellationRequested)
                    {
                        // scheduled end check
                        if (scheduledEnd.HasValue && DateTime.Now > scheduledEnd.Value)
                        {
                            this.Invoke(new Action(() => lblStatus.Text = "Scheduled run complete."));
                            break;
                        }

                        bool doClick = true;

                        if (!chkRunWhenMinimized.Checked && this.WindowState == FormWindowState.Minimized)
                        {
                            doClick = false;
                        }

                        // pause on user input
                        try
                        {
                            if (chkPauseOnInput != null && chkPauseOnInput.Checked)
                            {
                                var idleMs = GetIdleTimeMs();
                                var threshold = (int)(nudPauseIdleSeconds?.Value ?? 3) * 1000;
                                if (idleMs < (uint)threshold) doClick = false;
                            }
                        }
                        catch { }

                        if (doClick && chkLimitToSpecificWindow.Checked)
                        {
                            var allowed = txtTargetGameWindows.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < allowed.Length; i++) allowed[i] = allowed[i].Trim().ToLowerInvariant();
                            if (allowed.Length > 0)
                            {
                                var fg = GetForegroundProcessName();
                                if (string.IsNullOrEmpty(fg) || Array.IndexOf(allowed, fg.ToLowerInvariant()) < 0)
                                {
                                    // also try window title match
                                    var h = GetForegroundWindow();
                                    string title = null;
                                    try
                                    {
                                        var sb = new System.Text.StringBuilder(256);
                                        GetWindowText(h, sb, sb.Capacity);
                                        title = sb.ToString().ToLowerInvariant();
                                    }
                                    catch { }
                                    bool match = false;
                                    for (int i = 0; i < allowed.Length && !match; i++)
                                    {
                                        if (!string.IsNullOrEmpty(allowed[i]) && (fg.ToLowerInvariant().Contains(allowed[i]) || (!string.IsNullOrEmpty(title) && title.Contains(allowed[i]))))
                                            match = true;
                                    }
                                    if (!match) doClick = false;
                                }
                            }
                        }

                        if (doClick)
                        {
                            if (rdoRightButton.Checked)
                            {
                                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                            }
                            else
                            {
                                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                            }
                        }

                        int interval = 10;
                        try { this.Invoke(new Action(() => interval = (int)nudIntervalMs.Value)); } catch { }

                        // apply jitter
                        try
                        {
                            var jitter = (int)(nudJitterPercent?.Value ?? 0);
                            if (jitter > 0 && interval > 0)
                            {
                                var maxOffset = Math.Max(0, (int)(interval * jitter / 100.0));
                                var offset = rng.Next(-maxOffset, maxOffset + 1);
                                interval = Math.Max(1, interval + offset);
                            }
                        }
                        catch { }

                        if (interval > 0) Thread.Sleep(interval); else Thread.Sleep(0);
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    this.Invoke(new Action(() => lblStatus.Text = "Error during autoclick."));
                }
                finally
                {
                    // ensure UI state reset
                    this.Invoke(new Action(() =>
                    {
                        isClicking = false;
                        btnStartClicker.Enabled = true;
                        btnStopClicker.Enabled = false;
                        progressBar.Visible = false;
                    }));
                    try { StopTargetMonitor(); } catch { }
                }
            }, token);
        }

        private void StopAutoClicker()
        {
            if (!isClicking) return;
            try
            {
                autoClickerCts?.Cancel();
                autoClickerTask?.Wait(500);
            }
            catch { }
            finally
            {
                isClicking = false;
                btnStartClicker.Enabled = true;
                btnStopClicker.Enabled = false;
                progressBar.Visible = false;
                lblStatus.Text = "Stopped.";
                try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { }
                try { StopTargetMonitor(); } catch { }
            }
        }

        private void LogError(Exception ex)
        {
            try
            {
                // Prefer Windows Event Log for system integration; fall back to AppData in case of permissions
                try
                {
                    const string source = "Mentality";
                    const string logName = "Application";
                    if (!EventLog.SourceExists(source))
                    {
                        // Creating a source requires admin; attempt, but if it fails we'll fall back
                        try { EventLog.CreateEventSource(source, logName); } catch { }
                    }

                    if (EventLog.SourceExists(source))
                    {
                        EventLog.WriteEntry(source, ex.ToString(), EventLogEntryType.Error);
                        return;
                    }
                }
                catch { /* fall through to AppData fallback */ }

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "Mentality");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "error_log.txt");
                File.AppendAllText(file, $"[{DateTime.Now}] {ex}\r\n");
            }
            catch { /* nothing more we can do safely */ }
        }
    }
}
