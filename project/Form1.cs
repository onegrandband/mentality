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
using Microsoft.Win32;
using System.ComponentModel;
using System.Security.Principal;

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
        private ComboBox cmbClickType;

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
        private Button btnCheckUpdates;
        private Button btnInstallUpdate;
        private Label lblUpdateStatus;
        private Button btnMiscToggle;
        private Panel pnlMiscContent;
        private Button btnExportData;
        private Button btnImportData;
        private Button btnOtherStuffToggle;
        private Panel pnlOtherStuff;
        private bool otherStuffExpanded;
        private CheckBox chkAlwaysActive;
        private TextBox txtShowAppHotkey;
        private CheckBox chkKeepOnTop;
        private Button btnBackgroundColor;
        private CheckBox chkRainbowTap;
        private Label lblClickCount;
        private Button btnResetClickCount;
        private Button btnDesktopShortcut;
        private Button btnStartMenuShortcut;
        private Button btnRemoveDesktopShortcut;
        private Button btnRemoveStartMenuShortcut;
        private Button btnTaskbarShortcut;
        private CheckBox chkAutoCreateDesktopShortcut;
        private CheckBox chkAutoCreateStartMenuShortcut;
        private Button btnOpenDataFolder;
        private Label lblAdminStatus;
        private Button btnRestartAsAdmin;
        private bool miscExpanded;
        private UpdateInfo availableUpdate;

        // runtime fields
        private Random rng = new Random();
        private System.Collections.Generic.List<Profile> profiles = new System.Collections.Generic.List<Profile>();

        // fields
        private CancellationTokenSource autoClickerCts;
        private Task autoClickerTask;
        private volatile bool isClicking;
        private bool hotkeyRegistered;
        private IntPtr printScreenHookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc printScreenHookCallback;
        private bool printScreenKeyDown;
        private int autoClickerGeneration;
        private bool startInTray;
        private bool exitRequested;
        private NotifyIcon trayIcon;
        private NotifyIcon updateNotifyIcon;
        private bool showAppHotkeyRegistered;
        private Keys showAppHotkey = Keys.None;
        private uint showAppHotkeyModifiers;
        private System.Windows.Forms.Timer rainbowTimer;
        private System.Windows.Forms.Timer clickCountTimer;
        private System.Windows.Forms.Timer updateReminderTimer;
        private int rainbowHue;
        private int clickCount;
        private Color originalStatusForeColor;
        private bool updatingAlwaysActive;
        private bool printScreenCaptureMode;
        private bool updateCheckInProgress;
        private uint pendingPrintScreenModifiers;

        // target monitoring
        private Task targetMonitorTask;
        private CancellationTokenSource targetMonitorCts;
        private volatile bool isTargetAvailable = true;

        // native
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0x9000;
        private const int SHOW_APP_HOTKEY_ID = 0x9001;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_SNAPSHOT = 0x2C;

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

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr moduleHandle, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public Form1()
        public Form1(bool startInTray = false)
        {
            this.startInTray = startInTray;
            InitializeComponent();
            BuildUi();
            LoadState();
            this.FormClosing += Form1_FormClosing;
            this.Shown += Form1_Shown;
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
            this.Text = "Mentality v1.3.5.1";
            this.Size = new Size(900, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;

            // Try load icon if present
            TrySetAppIcon();

            // main panel with scrolling
            mainPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            lblStatus = new Label { Text = "Ready", Location = new Point(8, 8), Size = new Size(820, 24), Font = new Font("Segoe UI", 10F) };
            originalStatusForeColor = lblStatus.ForeColor;
            progressBar = new ProgressBar { Location = new Point(8, 36), Size = new Size(820, 16), Visible = false };

            int y = 64;

            lblInterval = new Label { Text = "Interval (ms):", Location = new Point(8, y), Size = new Size(100, 22) };
            nudIntervalMs = new NumericUpDown { Location = new Point(112, y - 2), Size = new Size(100, 24), Minimum = 0, Maximum = 10000, Value = 10 };
            trackInterval = new TrackBar { Location = new Point(220, y - 8), Size = new Size(360, 45), Minimum = 0, Maximum = 1000, Value = 10, TickFrequency = 50 };
            trackInterval = new TrackBar { Location = new Point(220, y - 8), Size = new Size(360, 45), Minimum = 0, Maximum = 10000, Value = 10, TickFrequency = 500 };
            trackInterval.Scroll += (s, e) => { try { nudIntervalMs.Value = Math.Min(nudIntervalMs.Maximum, Math.Max(nudIntervalMs.Minimum, trackInterval.Value)); } catch { } };
            nudIntervalMs.ValueChanged += (s, e) => { try { trackInterval.Value = (int)Math.Min(trackInterval.Maximum, Math.Max(trackInterval.Minimum, (int)nudIntervalMs.Value)); } catch { } };

            y += 60;
            lblButton = new Label { Text = "Mouse Button:", Location = new Point(8, y), Size = new Size(100, 22) };
            rdoLeftButton = new RadioButton { Text = "Left", Location = new Point(120, y), Checked = true };
            rdoRightButton = new RadioButton { Text = "Right", Location = new Point(180, y) };
            cmbClickType = new ComboBox { Location = new Point(120, y - 2), Size = new Size(150, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbClickType.Items.AddRange(new object[] { "Left click", "Double click", "Right click", "Middle click" });
            cmbClickType.SelectedIndex = 0;

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
            chkUseHotkey.CheckedChanged += (s, e) => TryRegisterHotkey();
            txtHotkey = new TextBox { Location = new Point(152, y - 2), Size = new Size(200, 24), ReadOnly = true, TabStop = true };
            txtHotkey.BackColor = SystemColors.Window;
            lblHotkeyHint = new Label { Text = "Click to set a global hotkey (e.g., Win+F5).", Location = new Point(360, y), Size = new Size(400, 22), ForeColor = Color.Gray };
            // Click to begin capture; then press a key combination (e.g., Ctrl+Shift+A)
            txtHotkey.Click += (s, e) => { try { txtHotkey.ReadOnly = false; txtHotkey.Text = "Press combination..."; txtHotkey.Focus(); } catch { } };
            txtHotkey.Click += (s, e) => BeginHotkeyCapture();
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
                        if (IsWinKeyDown()) parts.Add("Win");
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
                    printScreenCaptureMode = false;

                    var names = new System.Collections.Generic.List<string>();
                    if ((mods & MOD_CONTROL) != 0) names.Add("Ctrl");
                    if ((mods & MOD_SHIFT) != 0) names.Add("Shift");
                    if ((mods & MOD_ALT) != 0) names.Add("Alt");
                    if ((mods & MOD_WIN) != 0) names.Add("Win");
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
            btnSaveProfile.Click += SaveProfileClicked;
            btnDeleteProfile = new Button { Text = "Delete", Location = new Point(364, y), Size = new Size(60, 24) };
            btnDeleteProfile.Click += (s, e) => { try { if (cmbProfiles.SelectedItem != null) { var profileName = cmbProfiles.SelectedItem.ToString(); if (MessageBox.Show($"Delete profile '{profileName}'?","Confirm",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes) { profiles.RemoveAll(x=>x.Name==profileName); ProfilesManager.SaveProfiles(profiles); RefreshProfilesCombo(); lblStatus.Text = $"Deleted profile '{profileName}'"; Audit($"Profile '{profileName}' deleted"); } } } catch { } };
            btnDeleteProfile.Click += (s, e) => { try { if (cmbProfiles.SelectedItem != null) { var profileName = cmbProfiles.SelectedItem.ToString(); if (MessageBox.Show($"Delete profile '{profileName}'?","Confirm",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes) { profiles.RemoveAll(x=>x.Name==profileName); if (!ProfilesManager.SaveProfiles(profiles)) { profiles = ProfilesManager.LoadProfiles(); RefreshProfilesCombo(); lblStatus.Text = "Could not delete the profile."; return; } RefreshProfilesCombo(); lblStatus.Text = $"Deleted profile '{profileName}'"; Audit($"Profile '{profileName}' deleted"); } } } catch (Exception ex) { LogError(ex); lblStatus.Text = "Could not delete the profile."; } };

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
                    TryRegisterHotkey();
                    lblStatus.Text = "Hotkey cleared.";
                    Audit("Hotkey cleared by user");
                }
                catch { }
            };

            // Misc group
            var grpMisc = new GroupBox { Text = "Misc (advanced)", Location = new Point(8, y+92), Size = new Size(760, 180) };
            // Misc accordion
            btnMiscToggle = new Button { Text = "Misc (advanced)   ▶", Location = new Point(8, y+92), Size = new Size(760, 30), TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat };
            pnlMiscContent = new Panel { Location = new Point(8, y+122), Size = new Size(760, 252), BorderStyle = BorderStyle.FixedSingle, Visible = false, AutoScroll = true };
            mainPanel.AutoScrollMinSize = new Size(0, btnMiscToggle.Bottom + 8);
            btnMiscToggle.Click += (s, e) =>
            {
                if (miscExpanded && otherStuffExpanded)
                {
                    otherStuffExpanded = false;
                    pnlOtherStuff.Visible = false;
                    btnOtherStuffToggle.Text = "Other stuff   ▶";
                    pnlMiscContent.Height = btnOtherStuffToggle.Bottom + 8;
                }
                miscExpanded = !miscExpanded;
                pnlMiscContent.Visible = miscExpanded;
                btnMiscToggle.Text = miscExpanded ? "Misc (advanced)   ▼" : "Misc (advanced)   ▶";
                mainPanel.AutoScrollMinSize = new Size(0, (miscExpanded ? pnlMiscContent.Bottom + 48 : btnMiscToggle.Bottom + 8));
                mainPanel.PerformLayout();
                if (miscExpanded)
                {
                    var scrollBottom = Math.Max(0, mainPanel.AutoScrollMinSize.Height - mainPanel.ClientSize.Height);
                    mainPanel.AutoScrollPosition = new Point(0, scrollBottom);
                }
                else
                {
                    mainPanel.ScrollControlIntoView(btnMiscToggle);
                }
            };

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
            btnCheckUpdates = new Button { Text = "Check for updates", Location = new Point(12, 140), Size = new Size(140, 28) };
            btnCheckUpdates.Click += CheckForUpdatesClicked;
            btnInstallUpdate = new Button { Text = "Install update", Location = new Point(160, 140), Size = new Size(120, 28), Enabled = false };
            btnInstallUpdate.Click += InstallUpdateClicked;
            lblUpdateStatus = new Label { Text = "Check online for a newer version.", Location = new Point(292, 144), Size = new Size(440, 24), ForeColor = Color.Gray };
            btnExportData = new Button { Text = "Export data...", Location = new Point(12, 178), Size = new Size(120, 28) };
            btnExportData.Click += ExportDataClicked;
            btnImportData = new Button { Text = "Import data...", Location = new Point(144, 178), Size = new Size(120, 28) };
            btnImportData.Click += ImportDataClicked;

            btnOtherStuffToggle = new Button { Text = "Other stuff   ▶", Location = new Point(12, 216), Size = new Size(720, 28), TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat };
            pnlOtherStuff = new Panel { Location = new Point(12, 250), Size = new Size(720, 460), BorderStyle = BorderStyle.FixedSingle, Visible = false, AutoScroll = true };
            chkAlwaysActive = new CheckBox { Text = "Always Active (start with Windows; keep in tray)", Location = new Point(12, 10), Size = new Size(330, 24), Checked = Properties.Settings.Default.AlwaysActive };
            chkAlwaysActive.CheckedChanged += AlwaysActiveChanged;
            txtShowAppHotkey = new TextBox { Location = new Point(12, 46), Size = new Size(220, 24), ReadOnly = true, Text = FormatShowAppHotkey() };
            txtShowAppHotkey.Click += (s, e) => { txtShowAppHotkey.ReadOnly = false; txtShowAppHotkey.Text = "Press combination..."; txtShowAppHotkey.Focus(); };
            txtShowAppHotkey.KeyDown += ShowAppHotkeyKeyDown;
            var lblShowAppHotkey = new Label { Text = "Hotkey to show app:", Location = new Point(244, 49), Size = new Size(180, 22) };
            chkKeepOnTop = new CheckBox { Text = "Keep window on top", Location = new Point(12, 82), Size = new Size(200, 24), Checked = Properties.Settings.Default.KeepOnTop };
            chkKeepOnTop.CheckedChanged += KeepOnTopChanged;
            this.TopMost = chkKeepOnTop.Checked;
            btnBackgroundColor = new Button { Text = "Choose background color...", Location = new Point(12, 116), Size = new Size(200, 28) };
            btnBackgroundColor.Click += ChooseBackgroundColorClicked;
            chkRainbowTap = new CheckBox { Text = "Rainbow tap effect", Location = new Point(236, 118), Size = new Size(200, 24), Checked = Properties.Settings.Default.RainbowTap };
            chkRainbowTap.CheckedChanged += RainbowTapChanged;
            rainbowTimer = new System.Windows.Forms.Timer { Interval = 80 };
            rainbowTimer.Tick += RainbowTimerTick;
            lblClickCount = new Label { Text = "Clicks this session: 0", Location = new Point(12, 154), Size = new Size(190, 22) };
            btnResetClickCount = new Button { Text = "Reset count", Location = new Point(208, 150), Size = new Size(110, 28) };
            btnResetClickCount.Click += ResetClickCountClicked;
            clickCountTimer = new System.Windows.Forms.Timer { Interval = 250 };
            clickCountTimer.Tick += (s, e) => lblClickCount.Text = "Clicks this session: " + Interlocked.CompareExchange(ref clickCount, 0, 0);
            clickCountTimer.Start();
            btnDesktopShortcut = new Button { Text = "Create Desktop shortcut", Location = new Point(12, 188), Size = new Size(180, 28) };
            btnDesktopShortcut.Click += (s, e) => CreateShortcutClicked(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), true);
            btnStartMenuShortcut = new Button { Text = "Create Start Menu shortcut", Location = new Point(200, 188), Size = new Size(200, 28) };
            btnStartMenuShortcut.Click += (s, e) => CreateShortcutClicked(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Mentality"), true);
            btnRemoveDesktopShortcut = new Button { Text = "Remove Desktop shortcut", Location = new Point(12, 222), Size = new Size(180, 28) };
            btnRemoveDesktopShortcut.Click += (s, e) => RemoveShortcutClicked(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Desktop");
            btnRemoveStartMenuShortcut = new Button { Text = "Remove Start Menu shortcut", Location = new Point(200, 222), Size = new Size(200, 28) };
            btnRemoveStartMenuShortcut.Click += (s, e) => RemoveShortcutClicked(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Mentality"), "Start Menu");
            btnTaskbarShortcut = new Button { Text = "Pin / unpin taskbar", Location = new Point(408, 222), Size = new Size(180, 28) };
            btnTaskbarShortcut.Click += TaskbarShortcutClicked;
            btnOpenDataFolder = new Button { Text = "Open app data folder", Location = new Point(12, 258), Size = new Size(180, 28) };
            btnOpenDataFolder.Click += OpenDataFolderClicked;
            lblAdminStatus = new Label { Text = IsRunningAsAdministrator() ? "Running as Administrator" : "Running as a standard user", Location = new Point(12, 296), Size = new Size(260, 22) };
            btnRestartAsAdmin = new Button { Text = "Restart as Administrator...", Location = new Point(12, 324), Size = new Size(200, 28) };
            btnRestartAsAdmin.Click += RestartAsAdministratorClicked;
            chkAutoCreateDesktopShortcut = new CheckBox { Text = "Automatically create Desktop shortcut if missing", Location = new Point(236, 328), Size = new Size(340, 22), Checked = Properties.Settings.Default.AutoCreateDesktopShortcut };
            chkAutoCreateDesktopShortcut.CheckedChanged += (s, e) => { Properties.Settings.Default.AutoCreateDesktopShortcut = chkAutoCreateDesktopShortcut.Checked; Properties.Settings.Default.Save(); };
            chkAutoCreateStartMenuShortcut = new CheckBox { Text = "Automatically create Start Menu shortcut if missing", Location = new Point(236, 354), Size = new Size(370, 22), Checked = Properties.Settings.Default.AutoCreateStartMenuShortcut };
            chkAutoCreateStartMenuShortcut.CheckedChanged += (s, e) => { Properties.Settings.Default.AutoCreateStartMenuShortcut = chkAutoCreateStartMenuShortcut.Checked; Properties.Settings.Default.Save(); };
            pnlOtherStuff.Controls.AddRange(new Control[] { chkAlwaysActive, txtShowAppHotkey, lblShowAppHotkey, chkKeepOnTop, btnBackgroundColor, chkRainbowTap, lblClickCount, btnResetClickCount, btnDesktopShortcut, btnStartMenuShortcut, btnRemoveDesktopShortcut, btnRemoveStartMenuShortcut, btnTaskbarShortcut, btnOpenDataFolder, lblAdminStatus, btnRestartAsAdmin, chkAutoCreateDesktopShortcut, chkAutoCreateStartMenuShortcut });
            btnOtherStuffToggle.Click += OtherStuffToggleClicked;

            pnlMiscContent.Controls.AddRange(new Control[] { chkSchedule, dtpStartTime, nudRunMinutes, lblRunMinutes, nudJitterPercent, lblJitter, chkPauseOnInput, nudPauseIdleSeconds, lblPauseHint, lblTargetNote, btnCheckUpdates, btnInstallUpdate, lblUpdateStatus, btnExportData, btnImportData, btnOtherStuffToggle, pnlOtherStuff });


            mainPanel.Controls.Add(lblStatus);
            mainPanel.Controls.Add(progressBar);
            mainPanel.Controls.Add(lblInterval);
            mainPanel.Controls.Add(nudIntervalMs);
            mainPanel.Controls.Add(trackInterval);
            mainPanel.Controls.Add(lblButton);
            mainPanel.Controls.Add(rdoLeftButton);
            mainPanel.Controls.Add(rdoRightButton);
            mainPanel.Controls.Add(cmbClickType);
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
            mainPanel.Controls.Add(btnMiscToggle);
            mainPanel.Controls.Add(pnlMiscContent);

            // footer
            var sep = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.Gainsboro };
            var footerLabel = new Label { Text = "Status:", Location = new Point(8, 4), AutoSize = true };
            sep.Controls.Add(footerLabel);

            this.Controls.Add(sep);
            this.Controls.Add(mainPanel);
        }

        private void OtherStuffToggleClicked(object sender, EventArgs e)
        {
            otherStuffExpanded = !otherStuffExpanded;
            pnlOtherStuff.Visible = otherStuffExpanded;
            btnOtherStuffToggle.Text = otherStuffExpanded ? "Other stuff   ▼" : "Other stuff   ▶";
            pnlMiscContent.Height = otherStuffExpanded ? pnlOtherStuff.Bottom + 8 : btnOtherStuffToggle.Bottom + 8;

            if (!miscExpanded) return;

            mainPanel.AutoScrollMinSize = new Size(0, pnlMiscContent.Bottom + 48);
            mainPanel.PerformLayout();
            var scrollBottom = Math.Max(0, mainPanel.AutoScrollMinSize.Height - mainPanel.ClientSize.Height);
            mainPanel.AutoScrollPosition = new Point(0, scrollBottom);
        }

        private void ShowAppHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            {
                showAppHotkey = Keys.None;
                showAppHotkeyModifiers = 0;
                txtShowAppHotkey.Text = string.Empty;
                txtShowAppHotkey.ReadOnly = true;
                Properties.Settings.Default.ShowAppHotkeyKey = string.Empty;
                Properties.Settings.Default.ShowAppHotkeyModifiers = 0;
                Properties.Settings.Default.Save();
                TryRegisterShowAppHotkey();
                return;
            }

            var key = e.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
            {
                var modifiersOnly = new System.Collections.Generic.List<string>();
                if (e.Control) modifiersOnly.Add("Ctrl");
                if (e.Shift) modifiersOnly.Add("Shift");
                if (e.Alt) modifiersOnly.Add("Alt");
                if (IsWinKeyDown()) modifiersOnly.Add("Win");
                txtShowAppHotkey.Text = string.Join("+", modifiersOnly);
                return;
            }

            uint modifiers = 0;
            if (e.Control) modifiers |= MOD_CONTROL;
            if (e.Shift) modifiers |= MOD_SHIFT;
            if (e.Alt) modifiers |= MOD_ALT;
            if (IsWinKeyDown()) modifiers |= MOD_WIN;
            if (key == Keys.PrintScreen || (chkUseHotkey.Checked && key == selectedHotkey && modifiers == selectedHotkeyModifiers))
            {
                lblStatus.Text = "Choose a different key combination for the show-app hotkey.";
                txtShowAppHotkey.Text = FormatShowAppHotkey();
                txtShowAppHotkey.ReadOnly = true;
                return;
            }

            showAppHotkey = key;
            showAppHotkeyModifiers = modifiers;
            txtShowAppHotkey.Text = FormatShowAppHotkey();
            txtShowAppHotkey.ReadOnly = true;
            Properties.Settings.Default.ShowAppHotkeyKey = showAppHotkey.ToString();
            Properties.Settings.Default.ShowAppHotkeyModifiers = (int)showAppHotkeyModifiers;
            Properties.Settings.Default.Save();
            TryRegisterShowAppHotkey();
        }

        private string FormatShowAppHotkey()
        {
            var keyName = Properties.Settings.Default.ShowAppHotkeyKey;
            Keys key;
            if (string.IsNullOrWhiteSpace(keyName) || !Enum.TryParse(keyName, out key) || key == Keys.None) return string.Empty;

            var modifiers = (uint)Properties.Settings.Default.ShowAppHotkeyModifiers;
            var parts = new System.Collections.Generic.List<string>();
            if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & MOD_WIN) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        private void TryRegisterShowAppHotkey()
        {
            if (!IsHandleCreated) return;
            UnregisterShowAppHotkey();
            if (showAppHotkey == Keys.None) return;

            if (chkUseHotkey.Checked && showAppHotkey == selectedHotkey && showAppHotkeyModifiers == selectedHotkeyModifiers)
            {
                lblStatus.Text = "The show-app hotkey conflicts with the clicker hotkey.";
                return;
            }

            if (RegisterHotKey(this.Handle, SHOW_APP_HOTKEY_ID, showAppHotkeyModifiers, (uint)showAppHotkey))
            {
                showAppHotkeyRegistered = true;
                return;
            }

            var error = Marshal.GetLastWin32Error();
            lblStatus.Text = "Could not register the show-app hotkey (error " + error + ").";
            HotkeyLog("Show-app RegisterHotKey failed err=" + error);
        }

        private void UnregisterShowAppHotkey()
        {
            if (!showAppHotkeyRegistered || !IsHandleCreated)
            {
                showAppHotkeyRegistered = false;
                return;
            }

            try { UnregisterHotKey(this.Handle, SHOW_APP_HOTKEY_ID); }
            catch { }
            finally { showAppHotkeyRegistered = false; }
        }

        private void KeepOnTopChanged(object sender, EventArgs e)
        {
            this.TopMost = chkKeepOnTop.Checked;
            Properties.Settings.Default.KeepOnTop = chkKeepOnTop.Checked;
            Properties.Settings.Default.Save();
        }

        private void ChooseBackgroundColorClicked(object sender, EventArgs e)
        {
            using (var dialog = new ColorDialog { Color = mainPanel.BackColor, FullOpen = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Properties.Settings.Default.BackgroundColor = ColorTranslator.ToHtml(dialog.Color);
                Properties.Settings.Default.Save();
                ApplyBackgroundColor(dialog.Color);
            }
        }

        private void ApplyBackgroundColor(Color color)
        {
            mainPanel.BackColor = color;
            pnlMiscContent.BackColor = color;
            pnlOtherStuff.BackColor = color;
            this.BackColor = color;
        }

        private void RainbowTapChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.RainbowTap = chkRainbowTap.Checked;
            Properties.Settings.Default.Save();
            if (chkRainbowTap.Checked) rainbowTimer.Start();
            else
            {
                rainbowTimer.Stop();
                lblStatus.ForeColor = originalStatusForeColor;
            }
        }

        private void RainbowTimerTick(object sender, EventArgs e)
        {
            if (!chkRainbowTap.Checked || !isClicking)
            {
                lblStatus.ForeColor = originalStatusForeColor;
                return;
            }

            rainbowHue = (rainbowHue + 6) % 360;
            lblStatus.ForeColor = ColorFromHue(rainbowHue);
        }

        private static Color ColorFromHue(int hue)
        {
            var chroma = 1.0;
            var x = 1.0 - Math.Abs((hue / 60.0 % 2.0) - 1.0);
            double red, green, blue;
            if (hue < 60) { red = chroma; green = x; blue = 0; }
            else if (hue < 120) { red = x; green = chroma; blue = 0; }
            else if (hue < 180) { red = 0; green = chroma; blue = x; }
            else if (hue < 240) { red = 0; green = x; blue = chroma; }
            else if (hue < 300) { red = x; green = 0; blue = chroma; }
            else { red = chroma; green = 0; blue = x; }
            return Color.FromArgb((int)(red * 255), (int)(green * 255), (int)(blue * 255));
        }

        private void ResetClickCountClicked(object sender, EventArgs e)
        {
            Interlocked.Exchange(ref clickCount, 0);
            lblClickCount.Text = "Clicks this session: 0";
        }

        private bool IsRunningAsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private string DesktopShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Mentality.lnk"); }
        }

        private string StartMenuShortcutFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Mentality"); }
        }

        private void CreateShortcutClicked(string destinationFolder, bool manual)
        {
            var shortcutPath = Path.Combine(destinationFolder, "Mentality.lnk");
            var isDesktop = string.Equals(destinationFolder, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StringComparison.OrdinalIgnoreCase);
            var promptSuppressed = isDesktop ? Properties.Settings.Default.DesktopShortcutPromptSuppressed : Properties.Settings.Default.StartMenuShortcutPromptSuppressed;
            var replaceApproved = isDesktop ? Properties.Settings.Default.DesktopShortcutReplaceApproved : Properties.Settings.Default.StartMenuShortcutReplaceApproved;
            try
            {
                Directory.CreateDirectory(destinationFolder);
                if (File.Exists(shortcutPath))
                {
                    Version existingVersion;
                    if (IsShortcutForExactCurrentVersion(shortcutPath, out existingVersion))
                    {
                        lblStatus.Text = "A shortcut for this exact Mentality version already exists.";
                        return;
                    }
                    if (!manual) return;

                    if (!promptSuppressed)
                    {
                        using (var prompt = new Form())
                        {
                            prompt.Text = "Replace shortcut?";
                            prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                            prompt.StartPosition = FormStartPosition.CenterParent;
                            prompt.ClientSize = new Size(430, 142);
                            prompt.MinimizeBox = false;
                            prompt.MaximizeBox = false;
                            var promptLabel = new Label { Text = "A shortcut already exists here:\r\n" + shortcutPath + "\r\nReplace it with this version of Mentality?", Location = new Point(12, 10), Size = new Size(406, 56) };
                            var suppress = new CheckBox { Text = "Do not show again for this location", Location = new Point(12, 72), Size = new Size(250, 22) };
                            var yes = new Button { Text = "Replace", DialogResult = DialogResult.Yes, Location = new Point(244, 104), Size = new Size(80, 28) };
                            var no = new Button { Text = "Cancel", DialogResult = DialogResult.No, Location = new Point(334, 104), Size = new Size(80, 28) };
                            prompt.Controls.AddRange(new Control[] { promptLabel, suppress, yes, no });
                            prompt.AcceptButton = yes;
                            prompt.CancelButton = no;
                            var result = prompt.ShowDialog(this);
                            if (suppress.Checked)
                            {
                                if (isDesktop)
                                {
                                    Properties.Settings.Default.DesktopShortcutPromptSuppressed = true;
                                    Properties.Settings.Default.DesktopShortcutReplaceApproved = result == DialogResult.Yes;
                                }
                                else
                                {
                                    Properties.Settings.Default.StartMenuShortcutPromptSuppressed = true;
                                    Properties.Settings.Default.StartMenuShortcutReplaceApproved = result == DialogResult.Yes;
                                }
                                Properties.Settings.Default.Save();
                            }
                            if (result != DialogResult.Yes) return;
                        }
                    }
                    else if (!replaceApproved)
                    {
                        lblStatus.Text = "Shortcut replacement was previously declined for this location.";
                        return;
                    }
                }

                WriteShortcut(shortcutPath);
                lblStatus.Text = "Shortcut created in " + destinationFolder;
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show(this, ex.Message, "Shortcut creation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsShortcutForExactCurrentVersion(string shortcutPath, out Version version)
        {
            object shellObject = null;
            object shortcutObject = null;
            version = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return false;
                shellObject = Activator.CreateInstance(shellType);
                dynamic shell = shellObject;
                shortcutObject = shell.CreateShortcut(shortcutPath);
                dynamic shortcut = shortcutObject;
                var target = Convert.ToString(shortcut.TargetPath);
                if (string.IsNullOrWhiteSpace(target) || !File.Exists(target)) return false;
                version = System.Reflection.AssemblyName.GetAssemblyName(target).Version;
                return string.Equals(System.Reflection.AssemblyName.GetAssemblyName(target).Name, "Mentality", StringComparison.OrdinalIgnoreCase)
                    && version == typeof(Form1).Assembly.GetName().Version;
            }
            catch { return false; }
            finally
            {
                if (shortcutObject != null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
                if (shellObject != null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
            }
        }

        private static void WriteShortcut(string shortcutPath)
        {
            object shellObject = null;
            object shortcutObject = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new InvalidOperationException("Windows Script Host is unavailable; a shortcut could not be created.");
                shellObject = Activator.CreateInstance(shellType);
                dynamic shell = shellObject;
                shortcutObject = shell.CreateShortcut(shortcutPath);
                dynamic shortcut = shortcutObject;
                shortcut.TargetPath = Application.ExecutablePath;
                shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                shortcut.Description = "Mentality AutoClicker";
                shortcut.IconLocation = Application.ExecutablePath + ",0";
                shortcut.Save();
            }
            finally
            {
                if (shortcutObject != null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
                if (shellObject != null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
            }
        }

        private void RemoveShortcutClicked(string folder, string location)
        {
            try
            {
                var shortcutPath = Path.Combine(folder, "Mentality.lnk");
                if (!File.Exists(shortcutPath))
                {
                    lblStatus.Text = "No Mentality shortcut was found in " + location + ".";
                    return;
                }
                if (MessageBox.Show(this, "Remove the Mentality shortcut from " + location + "?", "Remove shortcut", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                File.Delete(shortcutPath);
                lblStatus.Text = "Removed the Mentality shortcut from " + location + ".";
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show(this, ex.Message, "Shortcut removal failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateAutomaticShortcuts()
        {
            if (Properties.Settings.Default.AutoCreateDesktopShortcut)
                CreateAutomaticShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            if (Properties.Settings.Default.AutoCreateStartMenuShortcut)
                CreateAutomaticShortcut(StartMenuShortcutFolder);
        }

        private void CreateAutomaticShortcut(string folder)
        {
            try
            {
                var shortcutPath = Path.Combine(folder, "Mentality.lnk");
                if (File.Exists(shortcutPath))
                {
                    Version existingVersion;
                    if (IsShortcutForExactCurrentVersion(shortcutPath, out existingVersion)) return;
                    return;
                }
                Directory.CreateDirectory(folder);
                WriteShortcut(shortcutPath);
            }
            catch (Exception ex) { LogError(ex); }
        }

        private void TaskbarShortcutClicked(object sender, EventArgs e)
        {
            MessageBox.Show(this,
                "Windows does not provide a supported API for reliably pinning or unpinning applications from the taskbar. Use the Mentality shortcut: right-click it and choose 'Pin to taskbar' or 'Unpin from taskbar'.",
                "Taskbar pinning", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenDataFolderClicked(object sender, EventArgs e)
        {
            try
            {
                var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mentality");
                Directory.CreateDirectory(dataFolder);
                Process.Start(new ProcessStartInfo(dataFolder) { UseShellExecute = true });
                lblStatus.Text = "Opened the Mentality data folder.";
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show(this, ex.Message, "Could not open data folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RestartAsAdministratorClicked(object sender, EventArgs e)
        {
            if (IsRunningAsAdministrator())
            {
                lblStatus.Text = "Mentality is already running as Administrator.";
                return;
            }

            UnregisterCurrentHotkey();
            UnregisterShowAppHotkey();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                exitRequested = true;
                this.Close();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                TryRegisterHotkey();
                TryRegisterShowAppHotkey();
                lblStatus.Text = "Administrator restart was cancelled.";
            }
            catch (Exception ex)
            {
                TryRegisterHotkey();
                TryRegisterShowAppHotkey();
                LogError(ex);
                MessageBox.Show(this, ex.Message, "Could not restart as Administrator", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TryRegisterHotkey()
        {
            if (chkUseHotkey == null || !IsHandleCreated) return;

            try
            {
                try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { }
                UnregisterCurrentHotkey();

                if (!chkUseHotkey.Checked) return;
                var vk = GetSelectedHotkeyVk();
                var mods = GetSelectedHotkeyModifiers();
                if (vk != 0 && chkUseHotkey != null && chkUseHotkey.Checked)
                if (vk == VK_SNAPSHOT)
                {
                    printScreenHookCallback = PrintScreenKeyboardProc;
                    printScreenHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, printScreenHookCallback, GetModuleHandle(null), 0);
                    if (printScreenHookHandle == IntPtr.Zero)
                    {
                        var err = Marshal.GetLastWin32Error();
                        var friendly = $"Print Screen hotkey registration failed (error {err}).";
                        LogError(new Exception($"SetWindowsHookEx failed ({err})"));
                        lblStatus.Text = friendly;
                        Audit($"Print Screen hotkey registration failed (err={err})");
                        HotkeyLog($"SetWindowsHookEx failed err={err}, mods={mods}");
                        MessageBox.Show(this, friendly, "Hotkey failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        Audit($"Print Screen hook registered (mods={mods})");
                        HotkeyLog($"Print Screen hook registered mods={mods}");
                        lblStatus.Text = "Print Screen hotkey registered.";
                    }
                    return;
                }

                if (vk != 0)
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
                        hotkeyRegistered = true;
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
        private Profile CaptureCurrentSettings(string name)
        {
            return new Profile
            {
                Name = name,
                IntervalMs = (int)nudIntervalMs.Value,
                RightButton = cmbClickType.SelectedIndex == 2,
                MouseClickType = GetMouseClickTypeName(),
                StartDelay = (int)nudStartDelay.Value,
                AutoMinimize = chkAutoMinimize.Checked,
                RunWhenMinimized = chkRunWhenMinimized.Checked,
                UseHotkey = chkUseHotkey.Checked,
                HotkeyKey = selectedHotkey == Keys.None ? string.Empty : selectedHotkey.ToString(),
                HotkeyModifiers = (int)selectedHotkeyModifiers,
                LimitToSpecificWindow = chkLimitToSpecificWindow.Checked,
                TargetWindows = txtTargetGameWindows.Text,
                JitterPercent = (int)nudJitterPercent.Value,
                PauseOnUserInput = chkPauseOnInput.Checked,
                ScheduleEnabled = chkSchedule.Checked,
                ScheduledStartTime = dtpStartTime.Value,
                RunMinutes = (int)nudRunMinutes.Value,
                PauseIdleSeconds = (int)nudPauseIdleSeconds.Value
            };
        }

        private void SaveProfileClicked(object sender, EventArgs e)
        {
            try
            {
                var name = Prompt("Save Profile", "Profile name:", "New Profile");
                if (string.IsNullOrWhiteSpace(name)) return;

                name = name.Trim();
                profiles.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                profiles.Add(CaptureCurrentSettings(name));
                if (!ProfilesManager.SaveProfiles(profiles))
                {
                    lblStatus.Text = "Could not save the profile.";
                    return;
                }

                RefreshProfilesCombo();
                cmbProfiles.SelectedItem = name;
                lblStatus.Text = $"Saved profile '{name}'.";
                Audit($"Profile '{name}' saved");
            }
            catch (Exception ex)
            {
                LogError(ex);
                lblStatus.Text = "Could not save the profile.";
            }
        }
        private void UnregisterCurrentHotkey()
        {
            UnregisterPrintScreenHook();
            if (!hotkeyRegistered || !IsHandleCreated)
            {
                hotkeyRegistered = false;
                return;
            }

            try { UnregisterHotKey(this.Handle, HOTKEY_ID); }
            catch { }
            finally { hotkeyRegistered = false; }
        }

        private void UnregisterPrintScreenHook()
        {
            if (printScreenHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(printScreenHookHandle);
                printScreenHookHandle = IntPtr.Zero;
            }
            printScreenKeyDown = false;
            printScreenHookCallback = null;
        }

        private void BeginHotkeyCapture()
        {
            try
            {
                printScreenCaptureMode = true;
                UnregisterCurrentHotkey();
                txtHotkey.ReadOnly = false;
                txtHotkey.Text = "Press combination... (PrtScr supported)";
                txtHotkey.Focus();
                printScreenHookCallback = PrintScreenKeyboardProc;
                printScreenHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, printScreenHookCallback, GetModuleHandle(null), 0);
                if (printScreenHookHandle == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    printScreenCaptureMode = false;
                    lblStatus.Text = "Could not start hotkey capture (error " + error + ").";
                    LogError(new InvalidOperationException("Print Screen capture hook failed: " + error));
                }
            }
            catch (Exception ex)
            {
                printScreenCaptureMode = false;
                LogError(ex);
            }
        }

        private void CompletePrintScreenCapture()
        {
            var modifiers = pendingPrintScreenModifiers;
            printScreenCaptureMode = false;
            selectedHotkey = Keys.PrintScreen;
            selectedHotkeyModifiers = modifiers;
            var parts = new System.Collections.Generic.List<string>();
            if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & MOD_WIN) != 0) parts.Add("Win");
            parts.Add("PrtScr");
            txtHotkey.Text = string.Join("+", parts);
            txtHotkey.ReadOnly = true;
            Properties.Settings.Default.HotkeyKey = selectedHotkey.ToString();
            Properties.Settings.Default.HotkeyModifiers = (int)selectedHotkeyModifiers;
            Properties.Settings.Default.Save();
            chkUseHotkey.Checked = true;
            TryRegisterHotkey();
            lblStatus.Text = "Print Screen hotkey captured and enabled.";
        }

        private IntPtr PrintScreenKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Marshal.ReadInt32(lParam) == VK_SNAPSHOT)
            {
                var message = wParam.ToInt32();
                if (printScreenCaptureMode && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN))
                {
                    printScreenKeyDown = true;
                    pendingPrintScreenModifiers = 0;
                    if (IsKeyDown(0x11)) pendingPrintScreenModifiers |= MOD_CONTROL;
                    if (IsKeyDown(0x10)) pendingPrintScreenModifiers |= MOD_SHIFT;
                    if (IsKeyDown(0x12)) pendingPrintScreenModifiers |= MOD_ALT;
                    if (IsWinKeyDown()) pendingPrintScreenModifiers |= MOD_WIN;
                    try { BeginInvoke(new Action(CompletePrintScreenCapture)); } catch { }
                    return new IntPtr(1);
                }
                if (printScreenCaptureMode && (message == WM_KEYUP || message == WM_SYSKEYUP))
                    return new IntPtr(1);
                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                {
                    if (printScreenKeyDown) return new IntPtr(1);

                    if (AreHotkeyModifiersDown(selectedHotkeyModifiers))
                    {
                        printScreenKeyDown = true;
                        try
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (isClicking) StopAutoClicker(); else StartAutoClicker();
                            }));
                        }
                        catch { }
                        return new IntPtr(1);
                    }
                }
                else if ((message == WM_KEYUP || message == WM_SYSKEYUP) && printScreenKeyDown)
                {
                    printScreenKeyDown = false;
                    return new IntPtr(1);
                }
            }

            return CallNextHookEx(printScreenHookHandle, nCode, wParam, lParam);
        }

        private bool AreHotkeyModifiersDown(uint modifiers)
        {
            return IsKeyDown(0x11) == ((modifiers & MOD_CONTROL) != 0)
                && IsKeyDown(0x10) == ((modifiers & MOD_SHIFT) != 0)
                && IsKeyDown(0x12) == ((modifiers & MOD_ALT) != 0)
                && IsWinKeyDown() == ((modifiers & MOD_WIN) != 0);
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryRegisterHotkey();
            TryRegisterShowAppHotkey();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterCurrentHotkey();
            UnregisterShowAppHotkey();
            base.OnHandleDestroyed(e);
        }

        private void StartUpdateReminderChecks()
        {
            if (updateReminderTimer != null) return;
            updateReminderTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
            updateReminderTimer.Tick += async (s, e) => await CheckForUpdateReminderAsync();
            updateReminderTimer.Start();
            BeginInvoke(new Action(async () => await CheckForUpdateReminderAsync()));
        }

        private async Task CheckForUpdateReminderAsync()
        {
            if (updateCheckInProgress || IsDisposed) return;
            updateCheckInProgress = true;
            try
            {
                Properties.Settings.Default.LastUpdateCheckUtcTicks = DateTime.UtcNow.Ticks;
                Properties.Settings.Default.Save();
                var update = await Migrate.CheckForUpdateAsync(GetCurrentVersion());
                if (update == null)
                {
                    Properties.Settings.Default.LastUpdateNotifiedVersion = string.Empty;
                    Properties.Settings.Default.LastUpdateNotificationUtcTicks = 0;
                    Properties.Settings.Default.Save();
                    return;
                }

                var settings = Properties.Settings.Default;
                var versionText = update.Version.ToString();
                var sameVersion = string.Equals(settings.LastUpdateNotifiedVersion, versionText, StringComparison.OrdinalIgnoreCase);
                var lastTicks = settings.LastUpdateNotificationUtcTicks;
                var elapsed = lastTicks > 0 && lastTicks <= DateTime.MaxValue.Ticks
                    ? DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc)
                    : TimeSpan.MaxValue;
                if (!sameVersion || elapsed >= TimeSpan.FromHours(6))
                {
                    ShowUpdateNotification(update);
                    settings.LastUpdateNotifiedVersion = versionText;
                    settings.LastUpdateNotificationUtcTicks = DateTime.UtcNow.Ticks;
                    settings.Save();
                }
            }
            catch (Exception ex) { LogError(ex); }
            finally { updateCheckInProgress = false; }
        }

        private void ShowUpdateNotification(UpdateInfo update)
        {
            if (trayIcon != null && trayIcon.Visible)
            {
                trayIcon.BalloonTipTitle = "Mentality update available";
                trayIcon.BalloonTipText = "Version " + update.Version + " is available. Open Mentality to install it.";
                trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                trayIcon.ShowBalloonTip(10000);
                return;
            }

            if (updateNotifyIcon != null) updateNotifyIcon.Dispose();
            updateNotifyIcon = new NotifyIcon
            {
                Icon = this.Icon ?? SystemIcons.Application,
                Text = "Mentality update available",
                Visible = true,
                BalloonTipTitle = "Mentality update available",
                BalloonTipText = "Version " + update.Version + " is available. Open Mentality to install it.",
                BalloonTipIcon = ToolTipIcon.Info
            };
            updateNotifyIcon.BalloonTipClicked += (s, e) => ShowFromTray();
            updateNotifyIcon.ShowBalloonTip(10000);
            var disposeTimer = new System.Windows.Forms.Timer { Interval = 15000 };
            disposeTimer.Tick += (s, e) =>
            {
                disposeTimer.Stop();
                disposeTimer.Dispose();
                if (updateNotifyIcon != null)
                {
                    updateNotifyIcon.Visible = false;
                    updateNotifyIcon.Dispose();
                    updateNotifyIcon = null;
                }
            };
            disposeTimer.Start();
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            CreateAutomaticShortcuts();
            StartUpdateReminderChecks();
            if (chkAlwaysActive.Checked)
            {
                try { SetAlwaysActiveStartup(true); }
                catch (Exception ex) { LogError(ex); }
                EnsureTrayIcon();
                if (startInTray) HideToTray();
            }
            else if (startInTray)
            {
                EnsureTrayIcon();
                HideToTray();
            }
        }

        private void AlwaysActiveChanged(object sender, EventArgs e)
        {
            if (updatingAlwaysActive) return;

            var enabled = chkAlwaysActive.Checked;
            try
            {
                Properties.Settings.Default.AlwaysActive = enabled;
                Properties.Settings.Default.Save();
                SetAlwaysActiveStartup(enabled);

                if (enabled)
                {
                    EnsureTrayIcon();
                    lblStatus.Text = "Always Active is on; closing the window keeps Mentality in the tray.";
                }
                else
                {
                    DestroyTrayIcon();
                    lblStatus.Text = "Always Active is off.";
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                updatingAlwaysActive = true;
                chkAlwaysActive.Checked = !enabled;
                updatingAlwaysActive = false;
                Properties.Settings.Default.AlwaysActive = !enabled;
                try { Properties.Settings.Default.Save(); } catch { }
                lblStatus.Text = "Could not change Always Active. See the error log.";
            }
        }

        private static void SetAlwaysActiveStartup(bool enabled)
        {
            const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string valueName = "Mentality";
            if (enabled)
            {
                using (var runKey = Registry.CurrentUser.CreateSubKey(runKeyPath))
                {
                    if (runKey == null) throw new InvalidOperationException("Could not access the current-user startup settings.");
                    var command = "\"" + Application.ExecutablePath + "\" --background";
                    runKey.SetValue(valueName, command, RegistryValueKind.String);
                }
            }
            else
            {
                using (var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, true))
                {
                    if (runKey != null && runKey.GetValue(valueName) != null)
                        runKey.DeleteValue(valueName, false);
                }
            }
        }

        private void EnsureTrayIcon()
        {
            if (trayIcon != null) return;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Mentality", null, (s, e) => ShowFromTray());
            menu.Items.Add("Exit", null, ExitFromTray);
            trayIcon = new NotifyIcon
            {
                Icon = this.Icon ?? SystemIcons.Application,
                Text = "Mentality - Always Active",
                ContextMenuStrip = menu,
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => ShowFromTray();
        }

        private void HideToTray()
        {
            EnsureTrayIcon();
            trayIcon.Visible = true;
            this.ShowInTaskbar = false;
            this.Hide();
        }

        private void ShowFromTray()
        {
            this.ShowInTaskbar = true;
            this.Show();
            if (this.WindowState == FormWindowState.Minimized) this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        private void ExitFromTray(object sender, EventArgs e)
        {
            exitRequested = true;
            this.Close();
        }

        private void DestroyTrayIcon()
        {
            if (trayIcon == null) return;
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        private CancellationTokenSource StartTargetMonitor(string[] entries)
        {
            try
            {
                StopTargetMonitor();
                targetMonitorCts = new CancellationTokenSource();
                var token = targetMonitorCts.Token;
                var monitorCts = new CancellationTokenSource();
                targetMonitorCts = monitorCts;
                isTargetAvailable = false;
                lblStatus.Text = "Waiting for target app...";
                var token = monitorCts.Token;
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
                                        using (p)
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
                                }
                                catch { }
                            }

                            if (found != isTargetAvailable)
                            {
                                isTargetAvailable = found;
                            if (!found) this.Invoke(new Action(() => lblStatus.Text = "Waiting for target app..."));
                            Thread.Sleep(1000);
                                try
                                {
                                    this.BeginInvoke(new Action(() =>
                                    {
                                        if (ReferenceEquals(targetMonitorCts, monitorCts))
                                            lblStatus.Text = found ? "Target app available." : "Waiting for target app...";
                                    }));
                                }
                                catch { }
                            }
                            if (token.WaitHandle.WaitOne(1000)) break;
                        }
                    }
                    catch { }
                }, token);
                    finally
                    {
                        if (ReferenceEquals(targetMonitorCts, monitorCts)) targetMonitorCts = null;
                        monitorCts.Dispose();
                    }
                });
                return monitorCts;
            }
            catch { }
            catch { return null; }
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
            StopTargetMonitor(targetMonitorCts);
        }
            catch { }

        private void StopTargetMonitor(CancellationTokenSource monitorCts)
        {
            if (monitorCts == null) return;
            try { monitorCts.Cancel(); } catch { }
            if (ReferenceEquals(targetMonitorCts, monitorCts)) targetMonitorCts = null;
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
                try { Migrate.UpgradeUserSettings(); }
                catch (Exception ex) { LogError(ex); }

                chkAlwaysActive.Checked = Properties.Settings.Default.AlwaysActive;
                showAppHotkeyModifiers = (uint)Properties.Settings.Default.ShowAppHotkeyModifiers;
                if (!Enum.TryParse(Properties.Settings.Default.ShowAppHotkeyKey, out showAppHotkey))
                    showAppHotkey = Keys.None;
                txtShowAppHotkey.Text = FormatShowAppHotkey();
                chkKeepOnTop.Checked = Properties.Settings.Default.KeepOnTop;
                this.TopMost = chkKeepOnTop.Checked;
                chkRainbowTap.Checked = Properties.Settings.Default.RainbowTap;
                if (chkRainbowTap.Checked) rainbowTimer.Start();
                if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.BackgroundColor))
                {
                    try { ApplyBackgroundColor(ColorTranslator.FromHtml(Properties.Settings.Default.BackgroundColor)); }
                    catch (Exception ex) { LogError(ex); }
                }

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
                            if ((selectedHotkeyModifiers & MOD_WIN) != 0) parts.Add("Win");
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

        private string GetMouseClickTypeName()
        {
            switch (cmbClickType.SelectedIndex)
            {
                case 1: return "DoubleLeft";
                case 2: return "Right";
                case 3: return "Middle";
                default: return "Left";
            }
        }

        private static Version GetCurrentVersion()
        {
            var version = typeof(Form1).Assembly.GetName().Version;
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        private async void CheckForUpdatesClicked(object sender, EventArgs e)
        {
            btnCheckUpdates.Enabled = false;
            btnInstallUpdate.Enabled = false;
            lblUpdateStatus.Text = "Checking for updates...";

            try
            {
                availableUpdate = await Migrate.CheckForUpdateAsync(GetCurrentVersion());
                if (availableUpdate == null)
                {
                    lblUpdateStatus.Text = "You are using the latest version.";
                }
                else
                {
                    lblUpdateStatus.Text = $"Version {availableUpdate.Version} is available.";
                    btnInstallUpdate.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                availableUpdate = null;
                if (ex is AggregateException)
                    lblUpdateStatus.Text = "Could not reach either update source. Check your internet connection or proxy settings.";
                else if (ex is InvalidDataException)
                    lblUpdateStatus.Text = "Update sources responded, but no valid Windows release was listed.";
                else
                    lblUpdateStatus.Text = "Update check failed. See the error log for details.";
                LogError(ex);
            }
            finally
            {
                btnCheckUpdates.Enabled = true;
            }
        }

        private async void InstallUpdateClicked(object sender, EventArgs e)
        {
            if (availableUpdate == null) return;
            if (MessageBox.Show(this, $"Download and install Mentality {availableUpdate.Version}? The app will restart.", "Install update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            btnCheckUpdates.Enabled = false;
            btnInstallUpdate.Enabled = false;
            try
            {
                var progress = new Progress<string>(message => lblUpdateStatus.Text = message);
                var packagePath = await Migrate.DownloadUpdateAsync(availableUpdate, progress);
                Migrate.StartInstall(packagePath, Application.ExecutablePath, Process.GetCurrentProcess().Id);
                exitRequested = true;
                Close();
            }
            catch (Exception ex)
            {
                lblUpdateStatus.Text = "Installation failed. See the error log for details.";
                btnCheckUpdates.Enabled = true;
                btnInstallUpdate.Enabled = true;
                LogError(ex);
                MessageBox.Show(this, ex.Message, "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportDataClicked(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = "mentalitydata",
                FileName = "Mentality_backup.mentalitydata",
                Filter = "Mentality data files (*.mentalitydata)|*.mentalitydata",
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var data = new MentalityData
                    {
                        HotkeyKey = selectedHotkey == Keys.None ? string.Empty : selectedHotkey.ToString(),
                        HotkeyModifiers = (int)selectedHotkeyModifiers,
                        CurrentSettings = CaptureCurrentSettings("Current settings"),
                        Profiles = new System.Collections.Generic.List<Profile>(profiles)
                    };
                    MentalityDataManager.Export(dialog.FileName, data);
                    lblStatus.Text = "Data exported successfully.";
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportDataClicked(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = "Mentality data files (*.mentalitydata)|*.mentalitydata",
                Multiselect = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var data = MentalityDataManager.Import(dialog.FileName);
                    if (MessageBox.Show(this, "Importing will replace your current profiles and settings. Continue?", "Import Mentality data", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

                    var previousProfiles = profiles;
                    var previousHotkey = Properties.Settings.Default.HotkeyKey;
                    var previousModifiers = Properties.Settings.Default.HotkeyModifiers;
                    var settingsChanged = false;
                    try
                    {
                        Properties.Settings.Default.HotkeyKey = data.HotkeyKey ?? string.Empty;
                        Properties.Settings.Default.HotkeyModifiers = data.HotkeyModifiers;
                        Properties.Settings.Default.Save();
                        settingsChanged = true;

                        if (!ProfilesManager.SaveProfiles(data.Profiles)) throw new IOException("Could not save the imported profiles.");

                        profiles = data.Profiles;
                        data.CurrentSettings.HotkeyKey = data.HotkeyKey ?? string.Empty;
                        data.CurrentSettings.HotkeyModifiers = data.HotkeyModifiers;
                        ApplyProfile(data.CurrentSettings);
                        RefreshProfilesCombo();
                        lblStatus.Text = "Data imported successfully.";
                    }
                    catch
                    {
                        if (settingsChanged)
                        {
                            Properties.Settings.Default.HotkeyKey = previousHotkey;
                            Properties.Settings.Default.HotkeyModifiers = previousModifiers;
                            Properties.Settings.Default.Save();
                        }
                        ProfilesManager.SaveProfiles(previousProfiles);
                        profiles = previousProfiles;
                        RefreshProfilesCombo();
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ApplyProfile(Profile p)
        {
            try
            {
                if (p == null) return;
                nudIntervalMs.Value = Math.Min(nudIntervalMs.Maximum, Math.Max(nudIntervalMs.Minimum, p.IntervalMs));
                rdoRightButton.Checked = p.RightButton;
                rdoLeftButton.Checked = !p.RightButton;
                switch (p.MouseClickType)
                {
                    case "DoubleLeft": cmbClickType.SelectedIndex = 1; break;
                    case "Right": cmbClickType.SelectedIndex = 2; break;
                    case "Middle": cmbClickType.SelectedIndex = 3; break;
                    default: cmbClickType.SelectedIndex = p.RightButton ? 2 : 0; break;
                }
                nudStartDelay.Value = Math.Min(nudStartDelay.Maximum, Math.Max(nudStartDelay.Minimum, p.StartDelay));
                chkAutoMinimize.Checked = p.AutoMinimize;
                chkRunWhenMinimized.Checked = p.RunWhenMinimized;
                chkUseHotkey.Checked = p.UseHotkey;
                if (!string.IsNullOrEmpty(p.HotkeyKey) && Enum.TryParse<Keys>(p.HotkeyKey, out Keys kv)) selectedHotkey = kv;
                else selectedHotkey = Keys.None;
                selectedHotkeyModifiers = (uint)p.HotkeyModifiers;
                try
                {
                    Properties.Settings.Default.HotkeyKey = selectedHotkey == Keys.None ? string.Empty : selectedHotkey.ToString();
                    Properties.Settings.Default.HotkeyModifiers = (int)selectedHotkeyModifiers;
                    Properties.Settings.Default.Save();
                }
                catch (Exception ex) { LogError(ex); }
                chkLimitToSpecificWindow.Checked = p.LimitToSpecificWindow;
                txtTargetGameWindows.Text = p.TargetWindows;
                nudJitterPercent.Value = Math.Min(nudJitterPercent.Maximum, Math.Max(nudJitterPercent.Minimum, p.JitterPercent));
                chkPauseOnInput.Checked = p.PauseOnUserInput;
                chkSchedule.Checked = p.ScheduleEnabled;
                if (p.ScheduledStartTime >= dtpStartTime.MinDate && p.ScheduledStartTime <= dtpStartTime.MaxDate)
                    dtpStartTime.Value = p.ScheduledStartTime;
                nudRunMinutes.Value = Math.Min(nudRunMinutes.Maximum, Math.Max(nudRunMinutes.Minimum, p.RunMinutes));
                if (p.PauseIdleSeconds > 0)
                    nudPauseIdleSeconds.Value = Math.Min(nudPauseIdleSeconds.Maximum, Math.Max(nudPauseIdleSeconds.Minimum, p.PauseIdleSeconds));

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
                TryRegisterHotkey();
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
            if (chkAlwaysActive.Checked && !exitRequested && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            StopAutoClicker();
            try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { }
            UnregisterCurrentHotkey();
            UnregisterShowAppHotkey();
            rainbowTimer.Stop();
            clickCountTimer.Stop();
            DestroyTrayIcon();
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

        private static void SendMouseClick(uint downFlag, uint upFlag)
        {
            mouse_event(downFlag, 0, 0, 0, UIntPtr.Zero);
            mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
        }

        private static void PerformClick(int clickType)
        {
            switch (clickType)
            {
                case 1:
                    SendMouseClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                    Thread.Sleep(50);
                    SendMouseClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                    break;
                case 2:
                    SendMouseClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                    break;
                case 3:
                    SendMouseClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                    break;
                default:
                    SendMouseClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                    break;
            }
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
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == SHOW_APP_HOTKEY_ID)
            {
                ShowFromTray();
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
                using (var p = Process.GetProcessById((int)pid))
                    return p.ProcessName;
            }
            catch { return null; }
        }

        private void StartAutoClicker()
        {
            if (isClicking) return;

            var delay = (int)nudStartDelay.Value;
            var autoMinimize = chkAutoMinimize.Checked;
            var runWhenMinimized = chkRunWhenMinimized.Checked;
            var pauseOnInput = chkPauseOnInput.Checked;
            var pauseIdleSeconds = (int)nudPauseIdleSeconds.Value;
            var limitToSpecificWindow = chkLimitToSpecificWindow.Checked;
            var targetWindows = txtTargetGameWindows.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < targetWindows.Length; i++) targetWindows[i] = targetWindows[i].Trim().ToLowerInvariant();
            var clickType = cmbClickType.SelectedIndex;
            var jitterPercent = (int)nudJitterPercent.Value;
            DateTime? scheduledStart = null;
            DateTime? scheduledEnd = null;
            if (chkSchedule.Checked)
            {
                scheduledStart = dtpStartTime.Value;
                if (nudRunMinutes.Value > 0) scheduledEnd = scheduledStart.Value.AddMinutes((double)nudRunMinutes.Value);
            }

            isClicking = true;
            if (chkRainbowTap.Checked) rainbowTimer.Start();
            var generation = ++autoClickerGeneration;
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
            var clickerCts = autoClickerCts;
            var token = clickerCts.Token;

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
            var monitorCts = limitToSpecificWindow ? StartTargetMonitor(targetWindows) : null;

            autoClickerTask = Task.Run(() =>
            {
                var jitterRandom = new Random();
                try
                {
                    // wait until scheduled start if needed
                    // wait until scheduled start if needed (cancellable; safe for long waits)
                    if (scheduledStart.HasValue && scheduledStart.Value > DateTime.Now)
                    {
                        var waitMs = (int)(scheduledStart.Value - DateTime.Now).TotalMilliseconds;
                        if (waitMs > 0)
                        var scheduledWait = scheduledStart.Value - DateTime.Now;
                        if (scheduledWait > TimeSpan.Zero)
                        {
                            this.Invoke(new Action(() => lblStatus.Text = $"Waiting to start at {scheduledStart.Value}"));
                            var waited = 0;
                            while (waited < waitMs && !token.IsCancellationRequested)
                            while (scheduledWait > TimeSpan.Zero && !token.IsCancellationRequested)
                            {
                                Thread.Sleep(Math.Min(1000, waitMs - waited));
                                waited += Math.Min(1000, waitMs - waited);
                                var waitMilliseconds = (int)Math.Min(1000, scheduledWait.TotalMilliseconds);
                                if (token.WaitHandle.WaitOne(waitMilliseconds)) break;
                                scheduledWait = scheduledStart.Value - DateTime.Now;
                            }
                        }
                    }

                    // optional start delay
                    int delay = 0;
                    try { this.Invoke(new Action(() => delay = (int)nudStartDelay.Value)); } catch { }
                    // optional start delay (cancellable)
                    for (int i = 0; i < delay && !token.IsCancellationRequested; i++)
                    {
                        this.Invoke(new Action(() => lblStatus.Text = $"Starting in {delay - i}...") );
                        Thread.Sleep(1000);
                        if (token.WaitHandle.WaitOne(1000)) break;
                    }

                    // auto-minimize
                    if (chkAutoMinimize.Checked)
                    if (autoMinimize)
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
                        int interval = 10;
                        bool windowIsMinimized = false;
                        try
                        {
                            this.Invoke(new Action(() =>
                            {
                                interval = (int)nudIntervalMs.Value;
                                windowIsMinimized = this.WindowState == FormWindowState.Minimized;
                            }));
                        }
                        catch { }

                        if (!chkRunWhenMinimized.Checked && this.WindowState == FormWindowState.Minimized)
                        if (!runWhenMinimized && windowIsMinimized)
                        {
                            doClick = false;
                        }

                        // pause on user input
                        try
                        {
                            if (chkPauseOnInput != null && chkPauseOnInput.Checked)
                            if (pauseOnInput)
                            {
                                var idleMs = GetIdleTimeMs();
                                var threshold = (int)(nudPauseIdleSeconds?.Value ?? 3) * 1000;
                                var threshold = pauseIdleSeconds * 1000;
                                if (idleMs < (uint)threshold) doClick = false;
                            }
                        }
                        catch { }

                        if (doClick && chkLimitToSpecificWindow.Checked)
                        {
                            var allowed = txtTargetGameWindows.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < allowed.Length; i++) allowed[i] = allowed[i].Trim().ToLowerInvariant();
                            if (allowed.Length > 0)
                        if (doClick && limitToSpecificWindow && targetWindows.Length > 0)
                        {
                            var fg = GetForegroundProcessName();
                                if (string.IsNullOrEmpty(fg) || Array.IndexOf(allowed, fg.ToLowerInvariant()) < 0)
                            var foreground = (fg ?? string.Empty).ToLowerInvariant();
                            if (foreground.Length == 0 || Array.IndexOf(targetWindows, foreground) < 0)
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
                                for (int i = 0; i < targetWindows.Length && !match; i++)
                                {
                                        if (!string.IsNullOrEmpty(allowed[i]) && (fg.ToLowerInvariant().Contains(allowed[i]) || (!string.IsNullOrEmpty(title) && title.Contains(allowed[i]))))
                                    if (!string.IsNullOrEmpty(targetWindows[i]) && (foreground.Contains(targetWindows[i]) || (!string.IsNullOrEmpty(title) && title.Contains(targetWindows[i]))))
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
                            PerformClick(clickType);
                            Interlocked.Add(ref clickCount, clickType == 1 ? 2 : 1);
                        }

                        int interval = 10;
                        try { this.Invoke(new Action(() => interval = (int)nudIntervalMs.Value)); } catch { }

                        // apply jitter
                        try
                        {
                            var jitter = (int)(nudJitterPercent?.Value ?? 0);
                            if (jitter > 0 && interval > 0)
                            if (jitterPercent > 0 && interval > 0)
                            {
                                var maxOffset = Math.Max(0, (int)(interval * jitter / 100.0));
                                var offset = rng.Next(-maxOffset, maxOffset + 1);
                                var maxOffset = Math.Max(0, (int)(interval * jitterPercent / 100.0));
                                var offset = jitterRandom.Next(-maxOffset, maxOffset + 1);
                                interval = Math.Max(1, interval + offset);
                            }
                        }
                        catch { }

                        if (interval > 0) Thread.Sleep(interval); else Thread.Sleep(0);
                        if (interval > 0) token.WaitHandle.WaitOne(interval);
                        else Thread.Yield();
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    this.Invoke(new Action(() => lblStatus.Text = "Error during autoclick."));
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (generation == autoClickerGeneration) lblStatus.Text = "Error during autoclick.";
                        }));
                    }
                    catch { }
                }
                finally
                {
                    // ensure UI state reset
                    this.Invoke(new Action(() =>
                    StopTargetMonitor(monitorCts);
                    clickerCts.Dispose();
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (generation != autoClickerGeneration) return;
                            isClicking = false;
                            btnStartClicker.Enabled = true;
                            btnStopClicker.Enabled = false;
                            progressBar.Visible = false;
                        }));
                    try { StopTargetMonitor(); } catch { }
                    }
                    catch { }
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
            autoClickerGeneration++;
            try { autoClickerCts?.Cancel(); } catch { }
            autoClickerCts = null;
            isClicking = false;
            lblStatus.ForeColor = originalStatusForeColor;
            btnStartClicker.Enabled = true;
            btnStopClicker.Enabled = false;
            progressBar.Visible = false;
            lblStatus.Text = "Stopped.";
                try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { }
                try { StopTargetMonitor(); } catch { }
            }
            StopTargetMonitor();
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
