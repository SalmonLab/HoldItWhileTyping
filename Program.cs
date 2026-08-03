using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using System.Text.Json;
using System.Windows.Forms;

namespace HoldItWhileTyping;

internal static class Program
{
    [STAThread]
    [SupportedOSPlatform("windows")]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var context = new HoldItContext();
        Application.Run(context);
    }
}

[SupportedOSPlatform("windows")]
internal sealed class HoldItContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly FocusGuardService _focusGuard;
    private readonly HoldItSettings _settings;
    private readonly ToolStripMenuItem _enabledMenuItem;
    private readonly ToolStripMenuItem _runAtStartupMenuItem;
    private readonly ToolStripMenuItem _timeoutMenuItem;
    private readonly ToolStripMenuItem[] _timeoutChoices;
    private readonly ToolStripMenuItem _transparentModeMenuItem;
    private readonly ToolStripMenuItem _excludedAppsMenuItem;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly int[] _timeoutChoicesMs = new[] { 800, 1200, 2000, 3000, 5000 };
    private const string StartupValueName = "HoldItWhileTyping";
    private const string StartupRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public HoldItContext()
    {
        _settings = HoldItSettings.Load();
        _focusGuard = new FocusGuardService(_settings.LockMilliseconds, _settings.ExcludedProcesses)
        {
            Enabled = _settings.Enabled,
            TransparentMode = _settings.TransparentMode
        };
        _focusGuard.Start();
        _focusGuard.FocusLockStateChanged += (_, _) => UpdateStatusText();

        _enabledMenuItem = new ToolStripMenuItem("Enable")
        {
            Checked = _settings.Enabled,
            CheckOnClick = true
        };
        _enabledMenuItem.Click += (_, _) =>
        {
            var enabled = _enabledMenuItem.Checked;
            _focusGuard.Enabled = enabled;
            _settings.Enabled = enabled;
            UpdateStatusText();
            _settings.Save();
        };

        _runAtStartupMenuItem = new ToolStripMenuItem("Run at startup")
        {
            Checked = _settings.RunAtStartup,
            CheckOnClick = true
        };
        _runAtStartupMenuItem.Click += (_, _) =>
        {
            SetRunAtStartup(_runAtStartupMenuItem.Checked);
        };

        _timeoutMenuItem = new ToolStripMenuItem("Hold timeout (ms)");
        _timeoutChoices = new ToolStripMenuItem[_timeoutChoicesMs.Length];
        for (var i = 0; i < _timeoutChoicesMs.Length; i++)
        {
            var ms = _timeoutChoicesMs[i];
            var item = new ToolStripMenuItem($"{ms} ms");
            item.Tag = ms;
            item.CheckOnClick = true;
            item.Click += (_, _) => SetTimeout((int)item.Tag);
            _timeoutChoices[i] = item;
            _timeoutMenuItem.DropDownItems.Add(item);
        }

        _transparentModeMenuItem = new ToolStripMenuItem("Transparent mode (do not lock while typing)")
        {
            Checked = _settings.TransparentMode,
            CheckOnClick = true
        };
        _transparentModeMenuItem.Click += (_, _) =>
        {
            SetTransparentMode(_transparentModeMenuItem.Checked);
        };

        _excludedAppsMenuItem = new ToolStripMenuItem("Excluded app list...")
        {
            ToolTipText = "Edit process names like: teams, slack, discord"
        };
        _excludedAppsMenuItem.Click += (_, _) => EditExcludedApplications();

        SetTimeout(_settings.LockMilliseconds, save: false);
        SetRunAtStartup(_settings.RunAtStartup, save: false);
        SetTransparentMode(_settings.TransparentMode, save: false);
        SetExcludedProcesses(_settings.ExcludedProcesses, save: false);

        _statusMenuItem = new ToolStripMenuItem("Status: Inactive")
        {
            Enabled = false
        };
        UpdateStatusText();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add(_runAtStartupMenuItem);
        menu.Items.Add(_timeoutMenuItem);
        menu.Items.Add(_transparentModeMenuItem);
        menu.Items.Add(_excludedAppsMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "HoldItWhileTyping",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _focusGuard.Dispose();
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void SetTimeout(int ms, bool save = true)
    {
        if (ms < 300)
        {
            ms = 300;
        }

        _focusGuard.LockMilliseconds = ms;
        _settings.LockMilliseconds = ms;

        foreach (var menuItem in _timeoutChoices)
        {
            menuItem.Checked = (int)menuItem.Tag == ms;
        }

        UpdateStatusText();

        if (save)
        {
            _settings.Save();
        }
    }

    private void SetRunAtStartup(bool enabled, bool save = true)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(StartupValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(StartupValueName, false);
            }

            _settings.RunAtStartup = enabled;
            _runAtStartupMenuItem.Checked = enabled;

            if (save)
            {
                _settings.Save();
            }
        }
        catch
        {
        }
    }

    private void SetTransparentMode(bool transparent, bool save = true)
    {
        _focusGuard.TransparentMode = transparent;
        _settings.TransparentMode = transparent;

        _transparentModeMenuItem.Checked = transparent;

        UpdateStatusText();

        if (save)
        {
            _settings.Save();
        }
    }

    private void SetExcludedProcesses(IEnumerable<string> processList, bool save = true)
    {
        var normalized = FocusGuardService.NormalizeProcessNames(processList).ToArray();
        _focusGuard.SetExcludedProcesses(normalized);
        _settings.ExcludedProcesses = normalized;

        var label = normalized.Length > 0
            ? $"Excluded app list... ({normalized.Length}: {string.Join(", ", normalized.Take(3))})"
            : "Excluded app list...";
        _excludedAppsMenuItem.Text = label;

        UpdateStatusText();

        if (save)
        {
            _settings.Save();
        }
    }

    private void EditExcludedApplications()
    {
        using var dialog = new ExcludedApplicationsDialog(_settings.ExcludedProcesses);
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        SetExcludedProcesses(dialog.ProcessList, save: true);
    }

    private void UpdateStatusText()
    {
        if (_statusMenuItem is null)
        {
            return;
        }

        if (!_focusGuard.Enabled)
        {
            _statusMenuItem.Text = "Disabled";
            return;
        }

        if (_focusGuard.TransparentMode)
        {
            _statusMenuItem.Text = "Transparent mode: no focus lock while typing";
            return;
        }

        _statusMenuItem.Text = $"Enabled: block for {_focusGuard.LockMilliseconds} ms | excluded apps {_focusGuard.ExcludedProcessCount}";
    }
}

[SupportedOSPlatform("windows")]
internal sealed class HoldItSettings
{
    public bool Enabled { get; set; } = true;
    public int LockMilliseconds { get; set; } = 2000;
    public bool TransparentMode { get; set; } = false;
    public bool RunAtStartup { get; set; } = false;
    public string[] ExcludedProcesses { get; set; } = Array.Empty<string>();

    private static string SettingsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoldItWhileTyping");

    private static string SettingsFilePath =>
        Path.Combine(SettingsFolder, "settings.json");

    public static HoldItSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new HoldItSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<HoldItSettings>(json);
            if (loaded is null)
            {
                return new HoldItSettings();
            }

            if (loaded.LockMilliseconds < 300)
            {
                loaded.LockMilliseconds = 300;
            }

            if (loaded.ExcludedProcesses is null)
            {
                loaded.ExcludedProcesses = Array.Empty<string>();
            }

            return loaded;
        }
        catch
        {
            return new HoldItSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(
                SettingsFilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }
        catch
        {
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class FocusGuardService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int SW_RESTORE = 9;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCRBUTTONDOWN = 0x00A4;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _foregroundHook = IntPtr.Zero;
    private IntPtr _anchorWindow = IntPtr.Zero;
    private DateTime _lastInputUtc = DateTime.UtcNow;
    private bool _running;
    private bool _restoring;
    private readonly object _syncRoot = new();
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly LowLevelMouseProc _mouseProc;
    private readonly WinEventProc _winEventProc;
    private readonly IntPtr _moduleHandle;
    private HashSet<string> _excludedProcesses = new(StringComparer.OrdinalIgnoreCase);

    public int LockMilliseconds { get; set; }
    public bool Enabled { get; set; }
    public bool TransparentMode { get; set; }
    public int ExcludedProcessCount => _excludedProcesses.Count;
    public event EventHandler? FocusLockStateChanged;

    public FocusGuardService(int lockMilliseconds, IEnumerable<string>? excludedProcesses = null)
    {
        _moduleHandle = Native.GetModuleHandle(null);
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
        _winEventProc = ForegroundChanged;
        LockMilliseconds = lockMilliseconds;
        SetExcludedProcesses(excludedProcesses);
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _keyboardHook = Native.SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _keyboardProc,
            _moduleHandle,
            0);

        _mouseHook = Native.SetWindowsHookEx(
            WH_MOUSE_LL,
            _mouseProc,
            _moduleHandle,
            0);

        _foregroundHook = Native.SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        CaptureAnchorWindow(Native.GetForegroundWindow());
        _running = true;
        FocusLockStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetExcludedProcesses(IEnumerable<string>? processNames)
    {
        _excludedProcesses = new HashSet<string>(
            NormalizeProcessNames(processNames),
            StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_foregroundHook != IntPtr.Zero)
        {
            Native.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        _running = false;
        FocusLockStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled)
        {
            var message = wParam.ToInt32();
            if (message is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
            {
                CaptureInput();
            }
        }

        return Native.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled)
        {
            var message = wParam.ToInt32();
            if (
                message == WM_LBUTTONDOWN ||
                message == WM_RBUTTONDOWN ||
                message == WM_MBUTTONDOWN ||
                message == WM_NCLBUTTONDOWN ||
                message == WM_NCRBUTTONDOWN)
            {
                CaptureInput();
            }
        }

        return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void ForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (!_running || !Enabled || _restoring)
        {
            return;
        }

        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!Native.IsWindow(hwnd))
            {
                return;
            }

            if (TransparentMode)
            {
                CaptureAnchorWindow(hwnd);
                return;
            }

            if (IsExcludedWindow(hwnd))
            {
                CaptureAnchorWindow(hwnd);
                return;
            }

            var elapsedMs = (DateTime.UtcNow - _lastInputUtc).TotalMilliseconds;
            if (elapsedMs > LockMilliseconds)
            {
                CaptureAnchorWindow(hwnd);
                return;
            }

            if (_anchorWindow == IntPtr.Zero)
            {
                CaptureAnchorWindow(hwnd);
                return;
            }

            if (hwnd != _anchorWindow)
            {
                RestoreAnchorWindow();
            }
        }
    }

    private void CaptureInput()
    {
        lock (_syncRoot)
        {
            _lastInputUtc = DateTime.UtcNow;
            CaptureAnchorWindow(Native.GetForegroundWindow());
        }
    }

    private void CaptureAnchorWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && Native.IsWindow(hwnd))
        {
            _anchorWindow = hwnd;
        }
    }

    private void RestoreAnchorWindow()
    {
        if (_anchorWindow == IntPtr.Zero || _restoring || !Native.IsWindow(_anchorWindow))
        {
            return;
        }

        try
        {
            _restoring = true;
            Native.ShowWindow(_anchorWindow, SW_RESTORE);
            Native.SetWindowPos(
                _anchorWindow,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW | SWP_ASYNCWINDOWPOS);
            Native.SetForegroundWindow(_anchorWindow);
        }
        finally
        {
            _restoring = false;
        }
    }

    private bool IsExcludedWindow(IntPtr hwnd)
    {
        if (_excludedProcesses.Count == 0)
        {
            return false;
        }

        try
        {
            var processId = Native.GetWindowProcessId(hwnd);
            if (processId <= 0)
            {
                return false;
            }

            using var process = Process.GetProcessById(processId);
            var processName = NormalizeSingleProcessName(process.ProcessName);
            return _excludedProcesses.Contains(processName);
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<string> NormalizeProcessNames(IEnumerable<string>? processNames)
    {
        if (processNames is null)
        {
            return Array.Empty<string>();
        }

        return processNames
            .Select(NormalizeSingleProcessName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name);
    }

    private static string NormalizeSingleProcessName(string processName)
    {
        var normalized = processName.Trim().ToLowerInvariant();

        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized;
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(
            int idHook,
            Delegate lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventProc lpfn,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        public static int GetWindowProcessId(IntPtr hwnd)
        {
            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            return processId == 0 ? 0 : (int)processId;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);
}

internal sealed class ExcludedApplicationsDialog : Form
{
    private readonly TextBox _textBox;

    public IEnumerable<string> ProcessList => FocusGuardService.NormalizeProcessNames(_textBox.Text
        .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

    public ExcludedApplicationsDialog(IEnumerable<string> initialValues)
    {
        var initial = string.Join(", ", initialValues);

        Text = "Exclude applications from focus blocking";
        Width = 460;
        Height = 260;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;

        var description = new Label
        {
            Text = "Enter process names separated by comma or semicolon. Examples: Teams, Slack, Discord",
            AutoSize = true,
            Left = 10,
            Top = 12,
            Width = 420
        };

        _textBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Left = 10,
            Top = 40,
            Width = 420,
            Height = 130,
            Text = initial
        };

        var okButton = new Button
        {
            Text = "Save",
            Left = 250,
            Width = 90,
            Top = 180,
            DialogResult = DialogResult.OK
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 350,
            Width = 80,
            Top = 180,
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(description);
        Controls.Add(_textBox);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}
