using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SideLock;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string RunValueName = "SideLock";
    private const string LegacyRunValueName = "Fix Side Keys Microphone";
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly AppSettings _settings;
    private readonly MouseHook _mouseHook;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _blockMouse5MenuItem;
    private readonly ToolStripMenuItem _blockMouse4MenuItem;
    private readonly ToolStripMenuItem _startWithWindowsMenuItem;
    private bool _exiting;

    public TrayApplicationContext()
    {
        MigrateLegacyStartupEntry();
        _settings = AppSettings.Load();
        _mouseHook = new MouseHook(_settings.BlockMouse4, _settings.BlockMouse5);

        _startWithWindowsMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = IsStartWithWindowsEnabled()
        };
        _startWithWindowsMenuItem.CheckedChanged += StartWithWindowsMenuItem_CheckedChanged;

        var settingsMenuItem = new ToolStripMenuItem("Settings");
        settingsMenuItem.DropDownItems.Add(_startWithWindowsMenuItem);

        _blockMouse5MenuItem = new ToolStripMenuItem("Block Mouse5")
        {
            CheckOnClick = true,
            Checked = _settings.BlockMouse5
        };
        _blockMouse5MenuItem.CheckedChanged += BlockMouse5MenuItem_CheckedChanged;

        _blockMouse4MenuItem = new ToolStripMenuItem("Block Mouse4")
        {
            CheckOnClick = true,
            Checked = _settings.BlockMouse4
        };
        _blockMouse4MenuItem.CheckedChanged += BlockMouse4MenuItem_CheckedChanged;

        var exitMenuItem = new ToolStripMenuItem("Exit");
        exitMenuItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.Add(settingsMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_blockMouse5MenuItem);
        menu.Items.Add(_blockMouse4MenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "SideLock",
            ContextMenuStrip = menu,
            Visible = true
        };

        _mouseHook.Install();
    }

    private void BlockMouse5MenuItem_CheckedChanged(object? sender, EventArgs e)
    {
        _settings.BlockMouse5 = _blockMouse5MenuItem.Checked;
        _mouseHook.BlockMouse5 = _settings.BlockMouse5;
        _settings.Save();
    }

    private void BlockMouse4MenuItem_CheckedChanged(object? sender, EventArgs e)
    {
        _settings.BlockMouse4 = _blockMouse4MenuItem.Checked;
        _mouseHook.BlockMouse4 = _settings.BlockMouse4;
        _settings.Save();
    }

    private void StartWithWindowsMenuItem_CheckedChanged(object? sender, EventArgs e)
    {
        SetStartWithWindows(_startWithWindowsMenuItem.Checked);
    }

    private static bool IsStartWithWindowsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
        return key?.GetValue(RunValueName) is string;
    }

    private static void MigrateLegacyStartupEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
        if (key?.GetValue(LegacyRunValueName) is not string)
        {
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(executablePath))
        {
            key.SetValue(RunValueName, $"\"{executablePath}\"");
        }

        key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
    }

    private static void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true);

        if (enabled)
        {
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine the application path.");
            key.SetValue(RunValueName, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _mouseHook.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        if (!_exiting)
        {
            ExitApplication();
            return;
        }

        base.ExitThreadCore();
    }
}

internal sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const ushort XButton1 = 1; // Mouse4 / Back
    private const ushort XButton2 = 2; // Mouse5 / Forward
    private const ushort VkF23 = 0x86;
    private const ushort VkF24 = 0x87;
    private const uint KeyeventfKeyup = 0x0002;

    private readonly LowLevelMouseProc _callback;
    private IntPtr _hookId;
    private bool _mouse4Pressed;
    private bool _mouse5Pressed;

    public MouseHook(bool blockMouse4, bool blockMouse5)
    {
        BlockMouse4 = blockMouse4;
        BlockMouse5 = blockMouse5;
        _callback = HookCallback;
    }

    public bool BlockMouse4 { get; set; }
    public bool BlockMouse5 { get; set; }

    public void Install()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hookId = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(module?.ModuleName), 0);

        if (_hookId == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the mouse hook.");
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(_hookId, code, wParam, lParam);
        }

        var message = unchecked((int)wParam.ToInt64());
        if (message is not (WmXButtonDown or WmXButtonUp))
        {
            return CallNextHookEx(_hookId, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
        var xButton = unchecked((ushort)(data.MouseData >> 16));

        var handled = xButton switch
        {
            XButton1 => HandleMouseButton(message, BlockMouse4, ref _mouse4Pressed, VkF23),
            XButton2 => HandleMouseButton(message, BlockMouse5, ref _mouse5Pressed, VkF24),
            _ => false
        };

        return handled ? (IntPtr)1 : CallNextHookEx(_hookId, code, wParam, lParam);
    }

    private static bool HandleMouseButton(int message, bool isBlocked, ref bool isPressed, ushort virtualKey)
    {
        if (message == WmXButtonDown && isBlocked)
        {
            if (!isPressed)
            {
                SendKey(virtualKey, keyUp: false);
                isPressed = true;
            }

            return true;
        }

        if (message == WmXButtonUp && isPressed)
        {
            SendKey(virtualKey, keyUp: true);
            isPressed = false;
            return true;
        }

        return false;
    }

    private static void SendKey(ushort virtualKey, bool keyUp)
    {
        var input = new Input
        {
            Type = 1,
            Union = new InputUnion
            {
                Keyboard = new KeybdInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyeventfKeyup : 0
                }
            }
        };

        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    public void Dispose()
    {
        if (_mouse4Pressed)
        {
            SendKey(VkF23, keyUp: true);
            _mouse4Pressed = false;
        }

        if (_mouse5Pressed)
        {
            SendKey(VkF24, keyUp: true);
            _mouse5Pressed = false;
        }

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeybdInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}

internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SideLock",
        "settings.json");

    public bool BlockMouse4 { get; set; }
    public bool BlockMouse5 { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }

            var legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FixSideKeysMicrophone",
                "settings.json");

            if (File.Exists(legacyPath))
            {
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(legacyPath)) ?? new AppSettings();
            }
        }
        catch (IOException)
        {
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return new AppSettings();
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(this));
    }
}

internal static class AppIcon
{
    public static Icon Load()
    {
        try
        {
            if (Environment.ProcessPath is { Length: > 0 } executablePath)
            {
                var icon = Icon.ExtractAssociatedIcon(executablePath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // Use the embedded icon when extracting the executable icon is unavailable.
        }

        return new Icon(typeof(AppIcon), "sidelock.ico");
    }
}
