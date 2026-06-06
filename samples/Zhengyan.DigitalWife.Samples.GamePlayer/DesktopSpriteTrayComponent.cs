using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class DesktopSpriteTrayComponent(
    Func<GameWindowSettings> getWindowSettings,
    Func<string, string> resolveProjectPath,
    Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked) : GameComponent
{
    private readonly Func<GameWindowSettings> _getWindowSettings = getWindowSettings;
    private readonly Func<string, string> _resolveProjectPath = resolveProjectPath;
    private readonly Action<DesktopSpriteTrayMenuItemSettings> _onMenuItemClicked = onMenuItemClicked;
    private IDesktopSpriteTray? _tray;
    private string _fingerprint = string.Empty;
    private bool _unsupportedLogged;

    public override void Update(GameTime gameTime)
    {
        _ = gameTime;

        if (Game is null)
        {
            return;
        }

        GameWindowSettings settings = _getWindowSettings();
        if (!settings.DesktopSpriteMode || !settings.DesktopSpriteTrayEnabled)
        {
            DisposeTray();
            _fingerprint = string.Empty;
            return;
        }

        string fingerprint = BuildFingerprint(settings);
        if (_tray is not null && string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        DisposeTray();
        _fingerprint = fingerprint;

        if (OperatingSystem.IsWindows())
        {
            _tray = WindowsDesktopSpriteTray.TryCreate(
                Game.Window,
                ResolveTrayIconPath(settings),
                settings.DesktopSpriteTrayMenuItems ?? [],
                _onMenuItemClicked);
            return;
        }

        if (!_unsupportedLogged)
        {
            _unsupportedLogged = true;
            Console.WriteLine("[DesktopSprite] System tray is currently implemented only on Windows; Linux/macOS run with tray disabled.");
        }
    }

    public override void Dispose()
    {
        DisposeTray();
        base.Dispose();
    }

    private string ResolveTrayIconPath(GameWindowSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.DesktopSpriteTrayIconPath))
        {
            return _resolveProjectPath(settings.DesktopSpriteTrayIconPath);
        }

        if (!string.IsNullOrWhiteSpace(settings.IconPath))
        {
            return _resolveProjectPath(settings.IconPath);
        }

        return Path.Combine(AppContext.BaseDirectory, "Resources", "Logo", "logo.ico");
    }

    private void DisposeTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    private static string BuildFingerprint(GameWindowSettings settings)
    {
        StringBuilder builder = new();
        builder.Append(settings.DesktopSpriteMode).Append('|')
            .Append(settings.DesktopSpriteTrayEnabled).Append('|')
            .Append(settings.DesktopSpriteTrayIconPath).Append('|')
            .Append(settings.IconPath);

        foreach (DesktopSpriteTrayMenuItemSettings item in settings.DesktopSpriteTrayMenuItems ?? [])
        {
            builder.Append('|')
                .Append(item.Id).Append(':')
                .Append(item.Text).Append(':')
                .Append(item.Enabled).Append(':')
                .Append(item.BuiltInAction).Append(':')
                .Append(item.EventName);
        }

        return builder.ToString();
    }

    private interface IDesktopSpriteTray : IDisposable
    {
    }

    private sealed class WindowsDesktopSpriteTray : IDesktopSpriteTray
    {
        private const int TrayId = 1;
        private const int CommandBase = 1000;
        private const int WmCommand = 0x0111;
        private const int WmDestroy = 0x0002;
        private const int WmTrayIcon = 0x8000 + 42;
        private const int WmRButtonUp = 0x0205;
        private const int WmContextMenu = 0x007B;
        private const int WmLButtonUp = 0x0202;
        private const uint NimAdd = 0x00000000;
        private const uint NimDelete = 0x00000002;
        private const uint NifMessage = 0x00000001;
        private const uint NifIcon = 0x00000002;
        private const uint NifTip = 0x00000004;
        private const uint MfString = 0x00000000;
        private const uint MfGrayed = 0x00000001;
        private const uint TpmRightButton = 0x0002;
        private const uint TpmReturnCmd = 0x0100;
        private const uint ImageIcon = 1;
        private const uint LrLoadFromFile = 0x00000010;
        private const uint LrDefaultSize = 0x00000040;
        private const int IdiApplication = 32512;

        private readonly List<DesktopSpriteTrayMenuItemSettings> _items;
        private readonly Action<DesktopSpriteTrayMenuItemSettings> _onMenuItemClicked;
        private readonly WndProc _wndProc;
        private readonly IntPtr _hwnd;
        private readonly IntPtr _icon;
        private bool _disposed;

        private WindowsDesktopSpriteTray(
            IntPtr hwnd,
            IntPtr icon,
            IEnumerable<DesktopSpriteTrayMenuItemSettings> items,
            Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked,
            WndProc wndProc)
        {
            _hwnd = hwnd;
            _icon = icon;
            _items = items
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Select(CloneMenuItem)
                .ToList();
            _onMenuItemClicked = onMenuItemClicked;
            _wndProc = wndProc;
        }

        public static WindowsDesktopSpriteTray? TryCreate(
            IWindow window,
            string iconPath,
            IEnumerable<DesktopSpriteTrayMenuItemSettings> items,
            Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            IntPtr ownerHwnd = DesktopSpritePlatform.TryGetWindowsHwnd(window);
            if (ownerHwnd == IntPtr.Zero)
            {
                return null;
            }

            WndProc wndProc = WindowProc;
            string className = "ZhengyanDigitalWifeTray_" + Guid.NewGuid().ToString("N");
            WndClass wndClass = new()
            {
                LpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                HInstance = GetModuleHandle(null),
                LpszClassName = className
            };

            ushort atom = RegisterClass(ref wndClass);
            if (atom == 0)
            {
                Console.Error.WriteLine("[DesktopSprite] Failed to register Windows tray window class.");
                return null;
            }

            IntPtr hwnd = CreateWindowEx(
                0,
                className,
                "Zhengyan.DigitalWife Tray",
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                wndClass.HInstance,
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                Console.Error.WriteLine("[DesktopSprite] Failed to create Windows tray message window.");
                return null;
            }

            IntPtr icon = LoadTrayIcon(iconPath);
            WindowsDesktopSpriteTray tray = new(hwnd, icon, items, onMenuItemClicked, wndProc);
            SetWindowLongPtr(hwnd, GwlUserData, GCHandle.ToIntPtr(GCHandle.Alloc(tray)));

            if (!tray.AddIcon())
            {
                tray.Dispose();
                return null;
            }

            return tray;

            IntPtr WindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
            {
                IntPtr handlePointer = GetWindowLongPtr(hWnd, GwlUserData);
                if (handlePointer != IntPtr.Zero
                    && GCHandle.FromIntPtr(handlePointer).Target is WindowsDesktopSpriteTray current)
                {
                    return current.HandleMessage(hWnd, message, wParam, lParam);
                }

                return DefWindowProc(hWnd, message, wParam, lParam);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            NotifyIconData data = CreateNotifyIconData();
            _ = Shell_NotifyIcon(NimDelete, ref data);

            IntPtr handlePointer = GetWindowLongPtr(_hwnd, GwlUserData);
            if (handlePointer != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GwlUserData, IntPtr.Zero);
                GCHandle.FromIntPtr(handlePointer).Free();
            }

            if (_icon != IntPtr.Zero)
            {
                DestroyIcon(_icon);
            }

            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
            }
        }

        private bool AddIcon()
        {
            NotifyIconData data = CreateNotifyIconData();
            data.UFlags = NifMessage | NifIcon | NifTip;
            data.UCallbackMessage = WmTrayIcon;
            data.HIcon = _icon != IntPtr.Zero ? _icon : LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication));
            data.SzTip = "Zhengyan.DigitalWife";

            if (!Shell_NotifyIcon(NimAdd, ref data))
            {
                Console.Error.WriteLine("[DesktopSprite] Failed to add Windows system tray icon.");
                return false;
            }

            return true;
        }

        private NotifyIconData CreateNotifyIconData()
        {
            return new NotifyIconData
            {
                CbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                HWnd = _hwnd,
                UID = TrayId,
                SzTip = string.Empty,
                SzInfo = string.Empty,
                SzInfoTitle = string.Empty
            };
        }

        private IntPtr HandleMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmTrayIcon)
            {
                int mouseMessage = unchecked((int)lParam.ToInt64());
                if (mouseMessage is WmRButtonUp or WmContextMenu or WmLButtonUp)
                {
                    ShowMenu(hwnd);
                    return IntPtr.Zero;
                }
            }
            else if (message == WmCommand)
            {
                int commandId = wParam.ToInt32() & 0xffff;
                DispatchCommand(commandId);
                return IntPtr.Zero;
            }
            else if (message == WmDestroy)
            {
                return IntPtr.Zero;
            }

            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        private void ShowMenu(IntPtr hwnd)
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return;
            }

            try
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    DesktopSpriteTrayMenuItemSettings item = _items[i];
                    uint flags = MfString | (item.Enabled ? 0 : MfGrayed);
                    AppendMenu(menu, flags, (UIntPtr)(CommandBase + i), item.Text);
                }

                GetCursorPos(out Point point);
                SetForegroundWindow(hwnd);
                int command = TrackPopupMenu(
                    menu,
                    TpmRightButton | TpmReturnCmd,
                    point.X,
                    point.Y,
                    0,
                    hwnd,
                    IntPtr.Zero);
                if (command != 0)
                {
                    DispatchCommand(command);
                }
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        private void DispatchCommand(int commandId)
        {
            int index = commandId - CommandBase;
            if (index < 0 || index >= _items.Count)
            {
                return;
            }

            DesktopSpriteTrayMenuItemSettings item = _items[index];
            if (item.Enabled)
            {
                _onMenuItemClicked(item);
            }
        }

        private static DesktopSpriteTrayMenuItemSettings CloneMenuItem(DesktopSpriteTrayMenuItemSettings item)
        {
            return new DesktopSpriteTrayMenuItemSettings
            {
                Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
                Text = string.IsNullOrWhiteSpace(item.Text) ? "Menu Item" : item.Text,
                Enabled = item.Enabled,
                BuiltInAction = NormalizeBuiltInAction(item.BuiltInAction),
                EventName = item.EventName ?? string.Empty
            };
        }

        private static string NormalizeBuiltInAction(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            return normalized switch
            {
                "toggle_visibility" or "toggle" or "show_hide" or "showhide" => "toggle_visibility",
                "exit" or "quit" => "exit",
                _ => "none"
            };
        }

        private static IntPtr LoadTrayIcon(string iconPath)
        {
            if (!string.IsNullOrWhiteSpace(iconPath)
                && File.Exists(iconPath)
                && string.Equals(Path.GetExtension(iconPath), ".ico", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr icon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
                if (icon != IntPtr.Zero)
                {
                    return icon;
                }
            }

            return IntPtr.Zero;
        }

        private const int GwlUserData = -21;

        private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClass
        {
            public uint Style;

            public IntPtr LpfnWndProc;

            public int ClsExtra;

            public int WndExtra;

            public IntPtr HInstance;

            public IntPtr HIcon;

            public IntPtr HCursor;

            public IntPtr HbrBackground;

            public string? LpszMenuName;

            public string LpszClassName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint CbSize;

            public IntPtr HWnd;

            public uint UID;

            public uint UFlags;

            public uint UCallbackMessage;

            public IntPtr HIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string SzTip;

            public uint DwState;

            public uint DwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string SzInfo;

            public uint UTimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string SzInfoTitle;

            public uint DwInfoFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;

            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClass(ref WndClass lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int TrackPopupMenu(
            IntPtr hMenu,
            uint uFlags,
            int x,
            int y,
            int nReserved,
            IntPtr hWnd,
            IntPtr prcRect);
    }
}
