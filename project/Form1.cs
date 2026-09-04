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

        private Label lblPassphrase;
        private TextBox txtPassphrase;

        private Label lblStartDelay;
        private NumericUpDown nudStartDelay;

        private CheckBox chkAutoMinimize;
        private CheckBox chkRunWhenMinimized;
        private CheckBox chkUseHotkey;
        private ComboBox cmbHotkey;
        private Label lblHotkeyHint;

        private CheckBox chkRestrictToAllowed;
        private TextBox txtAllowedProcesses;

        private Button btnStartClicker;
        private Button btnStopClicker;

        // fields
        private CancellationTokenSource autoClickerCts;
        private Task autoClickerTask;
        private volatile bool isClicking;

        // native
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0x9000;

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
            this.Text = "Mentality v1.0.0";
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
            lblPassphrase = new Label { Text = "(optional) Passphrase:", Location = new Point(8, y), Size = new Size(140, 22) };
            txtPassphrase = new TextBox { Location = new Point(150, y - 2), Size = new Size(300, 24) };

            y += 34;
            lblStartDelay = new Label { Text = "Start delay (s):", Location = new Point(8, y), Size = new Size(100, 22) };
            nudStartDelay = new NumericUpDown { Location = new Point(120, y - 2), Size = new Size(80, 24), Minimum = 0, Maximum = 3600, Value = 0 };

            y += 36;
            chkAutoMinimize = new CheckBox { Text = "Auto-minimize when running", Location = new Point(8, y), Size = new Size(200, 22) };
            chkRunWhenMinimized = new CheckBox { Text = "Keep clicking when minimized", Location = new Point(220, y), Size = new Size(200, 22) };

            y += 30;
            chkUseHotkey = new CheckBox { Text = "Use hotkey", Location = new Point(8, y), Size = new Size(100, 22) };
            cmbHotkey = new ComboBox { Location = new Point(120, y - 2), Size = new Size(120, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbHotkey.Items.AddRange(new object[] { "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12","None" });
            cmbHotkey.SelectedItem = "F6";
            lblHotkeyHint = new Label { Text = "Toggle with selected key (global)", Location = new Point(252, y), Size = new Size(300, 18), ForeColor = Color.Gray };

            y += 34;
            chkRestrictToAllowed = new CheckBox { Text = "Only when allowed processes active", Location = new Point(8, y), Size = new Size(260, 22) };
            txtAllowedProcesses = new TextBox { Location = new Point(280, y - 2), Size = new Size(300, 24) };
            var lblAllowedHint = new Label { Text = "Comma-separated process names (no .exe)", Location = new Point(280, y + 26), Size = new Size(400, 18), ForeColor = Color.Gray };

            y += 60;
            btnStartClicker = new Button { Text = "Start Clicking", Location = new Point(8, y), Size = new Size(140, 40) };
            btnStartClicker.Click += (s, e) => StartAutoClicker();
            btnStopClicker = new Button { Text = "Stop Clicking", Location = new Point(160, y), Size = new Size(140, 40), Enabled = false };
            btnStopClicker.Click += (s, e) => StopAutoClicker();

            // add to panel
            mainPanel.Controls.Add(lblStatus);
            mainPanel.Controls.Add(progressBar);
            mainPanel.Controls.Add(lblInterval);
            mainPanel.Controls.Add(nudIntervalMs);
            mainPanel.Controls.Add(trackInterval);
            mainPanel.Controls.Add(lblButton);
            mainPanel.Controls.Add(rdoLeftButton);
            mainPanel.Controls.Add(rdoRightButton);
            mainPanel.Controls.Add(lblPassphrase);
            mainPanel.Controls.Add(txtPassphrase);
            mainPanel.Controls.Add(lblStartDelay);
            mainPanel.Controls.Add(nudStartDelay);
            mainPanel.Controls.Add(chkAutoMinimize);
            mainPanel.Controls.Add(chkRunWhenMinimized);
            mainPanel.Controls.Add(chkUseHotkey);
            mainPanel.Controls.Add(cmbHotkey);
            mainPanel.Controls.Add(lblHotkeyHint);
            mainPanel.Controls.Add(chkRestrictToAllowed);
            mainPanel.Controls.Add(txtAllowedProcesses);
            mainPanel.Controls.Add(lblAllowedHint);
            mainPanel.Controls.Add(btnStartClicker);
            mainPanel.Controls.Add(btnStopClicker);

            // footer
            var sep = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.Gainsboro };
            var footerLabel = new Label { Text = "Status:", Location = new Point(8, 4), AutoSize = true };
            sep.Controls.Add(footerLabel);

            this.Controls.Add(sep);
            this.Controls.Add(mainPanel);
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopAutoClicker();
            try { UnregisterHotKey(this.Handle, HOTKEY_ID); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                // toggle
                if (isClicking) StopAutoClicker(); else StartAutoClicker();
                return;
            }
            base.WndProc(ref m);
        }

        private uint GetVkFromCombo()
        {
            if (cmbHotkey == null || cmbHotkey.SelectedItem == null) return 0;
            var s = cmbHotkey.SelectedItem.ToString();
            if (s.Equals("None", StringComparison.OrdinalIgnoreCase)) return 0;
            if (s.StartsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(s.Substring(1), out int n) && n >= 1 && n <= 24)
                {
                    return (uint)(0x70 + (n - 1));
                }
            }
            return 0;
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
                    var vk = GetVkFromCombo();
                    if (vk != 0)
                    {
                        UnregisterHotKey(this.Handle, HOTKEY_ID);
                        RegisterHotKey(this.Handle, HOTKEY_ID, 0, vk);
                    }
                }
                catch { }
            }

            autoClickerCts = new CancellationTokenSource();
            var token = autoClickerCts.Token;

            autoClickerTask = Task.Run(() =>
            {
                try
                {
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
                        bool doClick = true;

                        if (!chkRunWhenMinimized.Checked && this.WindowState == FormWindowState.Minimized)
                        {
                            doClick = false;
                        }

                        if (doClick && chkRestrictToAllowed.Checked)
                        {
                            var allowed = txtAllowedProcesses.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < allowed.Length; i++) allowed[i] = allowed[i].Trim().ToLowerInvariant();
                            if (allowed.Length > 0)
                            {
                                var fg = GetForegroundProcessName();
                                if (string.IsNullOrEmpty(fg) || Array.IndexOf(allowed, fg.ToLowerInvariant()) < 0)
                                    doClick = false;
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
                        if (interval > 0) Thread.Sleep(interval); else Thread.Sleep(0);
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    this.Invoke(new Action(() => lblStatus.Text = "Error during autoclick."));
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
            }
        }

        private void LogError(Exception ex)
        {
            try
            {
                var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt");
                File.AppendAllText(p, $"[{DateTime.Now}] {ex}\r\n");
            }
            catch { }
        }
    }
}
