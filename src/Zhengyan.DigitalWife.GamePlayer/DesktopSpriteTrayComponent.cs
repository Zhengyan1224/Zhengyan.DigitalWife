using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.GamePlayer;

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
            _tray.Pump();
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

        if (OperatingSystem.IsLinux())
        {
            _tray = LinuxGtkDesktopSpriteTray.TryCreate(
                ResolveTrayIconPath(settings),
                settings.DesktopSpriteTrayMenuItems ?? [],
                _onMenuItemClicked);
            if (_tray is not null)
            {
                return;
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            _tray = MacDesktopSpriteTray.TryCreate(
                ResolveTrayIconPath(settings),
                settings.DesktopSpriteTrayMenuItems ?? [],
                _onMenuItemClicked);
            if (_tray is not null)
            {
                return;
            }
        }

        if (!_unsupportedLogged)
        {
            _unsupportedLogged = true;
            Console.WriteLine("[DesktopSprite] System tray is not available on this platform or required native libraries are missing.");
        }
    }

    public override void Dispose()
    {
        DisposeTray();
        base.Dispose();
    }

    private string ResolveTrayIconPath(GameWindowSettings settings)
    {
        string platformIconPath = OperatingSystem.IsWindows()
            ? settings.DesktopSpriteTrayWindowsIconPath
            : OperatingSystem.IsLinux()
                ? settings.DesktopSpriteTrayLinuxIconPath
                : OperatingSystem.IsMacOS()
                    ? settings.DesktopSpriteTrayMacOSIconPath
                    : string.Empty;
        if (!string.IsNullOrWhiteSpace(platformIconPath))
        {
            return _resolveProjectPath(platformIconPath);
        }

        if (!string.IsNullOrWhiteSpace(settings.DesktopSpriteTrayIconPath))
        {
            return _resolveProjectPath(settings.DesktopSpriteTrayIconPath);
        }

        if (!string.IsNullOrWhiteSpace(settings.IconPath))
        {
            return _resolveProjectPath(settings.IconPath);
        }

        string defaultIcon = OperatingSystem.IsWindows() ? "logo.ico" : "logo.png";
        return Path.Combine(AppContext.BaseDirectory, "Resources", "Logo", defaultIcon);
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
            .Append(settings.DesktopSpriteTrayWindowsIconPath).Append('|')
            .Append(settings.DesktopSpriteTrayLinuxIconPath).Append('|')
            .Append(settings.DesktopSpriteTrayMacOSIconPath).Append('|')
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
        void Pump();
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

        public void Pump()
        {
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

    private sealed class LinuxGtkDesktopSpriteTray : IDesktopSpriteTray
    {
        private const int AppIndicatorCategoryApplicationStatus = 0;
        private const int AppIndicatorStatusPassive = 0;
        private const int AppIndicatorStatusActive = 1;

        private static bool _missingLibrariesLogged;
        private static bool _gtkInitFailedLogged;
        private static bool _legacyFallbackLogged;
        private readonly List<DesktopSpriteTrayMenuItemSettings> _items;
        private readonly List<ActivatedCallback> _callbacks = [];
        private readonly Action<DesktopSpriteTrayMenuItemSettings> _onMenuItemClicked;
        private readonly IntPtr _indicator;
        private readonly IntPtr _statusIcon;
        private readonly IntPtr _menu;
        private StatusIconPopupCallback? _statusIconPopupCallback;
        private ActivatedCallback? _statusIconActivateCallback;
        private bool _disposed;

        private LinuxGtkDesktopSpriteTray(
            IntPtr indicator,
            IntPtr statusIcon,
            IntPtr menu,
            IEnumerable<DesktopSpriteTrayMenuItemSettings> items,
            Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked)
        {
            _indicator = indicator;
            _statusIcon = statusIcon;
            _menu = menu;
            _items = items
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Select(CloneMenuItem)
                .ToList();
            _onMenuItemClicked = onMenuItemClicked;
        }

        public static LinuxGtkDesktopSpriteTray? TryCreate(
            string iconPath,
            IEnumerable<DesktopSpriteTrayMenuItemSettings> items,
            Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked)
        {
            if (!OperatingSystem.IsLinux())
            {
                return null;
            }

            if (!LinuxTrayNative.TryLoadGtk())
            {
                if (!_missingLibrariesLogged)
                {
                    _missingLibrariesLogged = true;
                    Console.WriteLine("[DesktopSprite] Linux tray requires GTK3 native libraries, for example: libgtk-3-0.");
                }

                return null;
            }

            int argc = 0;
            IntPtr argv = IntPtr.Zero;
            if (!LinuxTrayNative.InitializeGtk(ref argc, ref argv))
            {
                if (!_gtkInitFailedLogged)
                {
                    _gtkInitFailedLogged = true;
                    Console.WriteLine("[DesktopSprite] Linux tray could not initialize GTK; system tray is disabled.");
                }

                return null;
            }

            if (PreferLegacyGtkStatusIcon())
            {
                return TryCreateGtkStatusIcon(iconPath, items, onMenuItemClicked)
                    ?? TryCreateAppIndicator(iconPath, items, onMenuItemClicked);
            }

            return TryCreateAppIndicator(iconPath, items, onMenuItemClicked)
                ?? TryCreateGtkStatusIcon(iconPath, items, onMenuItemClicked);
        }

        private static LinuxGtkDesktopSpriteTray? TryCreateAppIndicator(
            string iconPath,
            IEnumerable<DesktopSpriteTrayMenuItemSettings> items,
            Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked)
        {
            if (!LinuxTrayNative.TryLoadAppIndicator())
            {
                return null;
            }

            string id = "zhengyan-digitalwife-" + Environment.ProcessId;
            string iconNameOrPath = ResolveLinuxIcon(iconPath, out string iconThemePath, out string iconName);
            if (!string.IsNullOrWhiteSpace(iconThemePath) && LinuxTrayNative.SupportsCustomIconPath)
            {
                iconNameOrPath = iconName;
            }

            IntPtr indicator = LinuxTrayNative.app_indicator_new(
                id,
                iconNameOrPath,
                AppIndicatorCategoryApplicationStatus);
            if (indicator == IntPtr.Zero)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(iconThemePath) && LinuxTrayNative.SupportsCustomIconPath)
            {
                LinuxTrayNative.app_indicator_set_icon_theme_path(indicator, iconThemePath);
                LinuxTrayNative.app_indicator_set_icon_full(indicator, iconName, "Zhengyan.DigitalWife");
            }

            IntPtr menu = LinuxTrayNative.gtk_menu_new();
            if (menu == IntPtr.Zero)
            {
                return null;
            }

            LinuxGtkDesktopSpriteTray tray = new(indicator, IntPtr.Zero, menu, items, onMenuItemClicked);
            tray.BuildMenu();
            LinuxTrayNative.app_indicator_set_status(indicator, AppIndicatorStatusActive);
            LinuxTrayNative.app_indicator_set_menu(indicator, menu);
            return tray;
        }

        private static LinuxGtkDesktopSpriteTray? TryCreateGtkStatusIcon(
            string iconPath,
            IEnumerable<DesktopSpriteTrayMenuItemSettings> items,
            Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked)
        {
            try
            {
                IntPtr statusIcon = LinuxTrayNative.gtk_status_icon_new();
                if (statusIcon == IntPtr.Zero)
                {
                    return null;
                }

                IntPtr menu = LinuxTrayNative.gtk_menu_new();
                if (menu == IntPtr.Zero)
                {
                    return null;
                }

                LinuxGtkDesktopSpriteTray tray = new(IntPtr.Zero, statusIcon, menu, items, onMenuItemClicked);
                tray.BuildMenu();
                tray.ConfigureStatusIcon(iconPath);
                tray.ConnectStatusIconMenu();
                LinuxTrayNative.gtk_status_icon_set_visible(statusIcon, true);

                if (!_legacyFallbackLogged)
                {
                    _legacyFallbackLogged = true;
                    Console.WriteLine("[DesktopSprite] Linux tray is using legacy GtkStatusIcon fallback.");
                }

                return tray;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
        }

        public void Pump()
        {
            if (_disposed)
            {
                return;
            }

            for (int i = 0; i < 64 && LinuxTrayNative.g_main_context_pending(IntPtr.Zero); i++)
            {
                LinuxTrayNative.g_main_context_iteration(IntPtr.Zero, false);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_indicator != IntPtr.Zero)
            {
                LinuxTrayNative.app_indicator_set_status(_indicator, AppIndicatorStatusPassive);
            }

            if (_statusIcon != IntPtr.Zero)
            {
                LinuxTrayNative.gtk_status_icon_set_visible(_statusIcon, false);
            }
        }

        private void BuildMenu()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                int index = i;
                DesktopSpriteTrayMenuItemSettings item = _items[i];
                IntPtr menuItem = LinuxTrayNative.gtk_menu_item_new_with_label(item.Text);
                if (menuItem == IntPtr.Zero)
                {
                    continue;
                }

                LinuxTrayNative.gtk_widget_set_sensitive(menuItem, item.Enabled);
                ActivatedCallback callback = (_, _) => DispatchIndex(index);
                _callbacks.Add(callback);
                LinuxTrayNative.g_signal_connect_data(
                    menuItem,
                    "activate",
                    Marshal.GetFunctionPointerForDelegate(callback),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0);
                LinuxTrayNative.gtk_menu_shell_append(_menu, menuItem);
                LinuxTrayNative.gtk_widget_show(menuItem);
            }

            LinuxTrayNative.gtk_widget_show(_menu);
        }

        private void ConfigureStatusIcon(string iconPath)
        {
            LinuxTrayNative.gtk_status_icon_set_tooltip_text(_statusIcon, "Zhengyan.DigitalWife");

            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                LinuxTrayNative.gtk_status_icon_set_from_file(_statusIcon, Path.GetFullPath(iconPath));
                return;
            }

            LinuxTrayNative.gtk_status_icon_set_from_icon_name(_statusIcon, "application-x-executable");
        }

        private void ConnectStatusIconMenu()
        {
            _statusIconPopupCallback = (_, button, activateTime, _) => ShowStatusIconMenu(button, activateTime);
            LinuxTrayNative.g_signal_connect_data(
                _statusIcon,
                "popup-menu",
                Marshal.GetFunctionPointerForDelegate(_statusIconPopupCallback),
                IntPtr.Zero,
                IntPtr.Zero,
                0);

            _statusIconActivateCallback = (_, _) => ShowStatusIconMenu(0, 0);
            LinuxTrayNative.g_signal_connect_data(
                _statusIcon,
                "activate",
                Marshal.GetFunctionPointerForDelegate(_statusIconActivateCallback),
                IntPtr.Zero,
                IntPtr.Zero,
                0);
        }

        private void ShowStatusIconMenu(uint button, uint activateTime)
        {
            try
            {
                LinuxTrayNative.gtk_menu_popup_at_pointer(_menu, IntPtr.Zero);
            }
            catch (EntryPointNotFoundException)
            {
                LinuxTrayNative.gtk_menu_popup(_menu, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, button, activateTime);
            }
        }

        private void DispatchIndex(int index)
        {
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

        private static string ResolveLinuxIcon(string iconPath, out string iconThemePath, out string iconName)
        {
            iconThemePath = string.Empty;
            iconName = string.Empty;
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                string fullPath = Path.GetFullPath(iconPath);
                iconThemePath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                iconName = Path.GetFileNameWithoutExtension(fullPath);
                return fullPath;
            }

            iconName = "application-x-executable";
            return "application-x-executable";
        }

        private static bool PreferLegacyGtkStatusIcon()
        {
            string desktop = string.Join(
                ' ',
                Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty,
                Environment.GetEnvironmentVariable("DESKTOP_SESSION") ?? string.Empty,
                Environment.GetEnvironmentVariable("GDMSESSION") ?? string.Empty)
                .ToLowerInvariant();
            return desktop.Contains("xfce", StringComparison.Ordinal)
                || desktop.Contains("lxde", StringComparison.Ordinal)
                || desktop.Contains("mate", StringComparison.Ordinal)
                || desktop.Contains("kali", StringComparison.Ordinal)
                || desktop.Contains("trinity", StringComparison.Ordinal);
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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ActivatedCallback(IntPtr widget, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StatusIconPopupCallback(IntPtr statusIcon, uint button, uint activateTime, IntPtr userData);

        private static class LinuxTrayNative
        {
            private const int RtldLazy = 1;
            private static bool _gtkLoaded;
            private static bool _gtkLoadAttempted;
            private static bool _appIndicatorLoaded;
            private static bool _appIndicatorLoadAttempted;
            private static bool _gtkInitialized;
            private static AppIndicatorNewDelegate? _appIndicatorNew;
            private static AppIndicatorSetStatusDelegate? _appIndicatorSetStatus;
            private static AppIndicatorSetMenuDelegate? _appIndicatorSetMenu;
            private static AppIndicatorSetIconThemePathDelegate? _appIndicatorSetIconThemePath;
            private static AppIndicatorSetIconFullDelegate? _appIndicatorSetIconFull;

            public static bool SupportsCustomIconPath => _appIndicatorSetIconThemePath is not null && _appIndicatorSetIconFull is not null;

            public static bool TryLoadGtk()
            {
                if (_gtkLoadAttempted)
                {
                    return _gtkLoaded;
                }

                _gtkLoadAttempted = true;
                _gtkLoaded = TryLoadLibrary("libgtk-3.so.0", out _);
                return _gtkLoaded;
            }

            public static bool InitializeGtk(ref int argc, ref IntPtr argv)
            {
                if (_gtkInitialized)
                {
                    return true;
                }

                _gtkInitialized = gtk_init_check(ref argc, ref argv);
                return _gtkInitialized;
            }

            internal static IntPtr app_indicator_new(string id, string iconName, int category)
            {
                return _appIndicatorNew?.Invoke(id, iconName, category) ?? IntPtr.Zero;
            }

            internal static void app_indicator_set_status(IntPtr indicator, int status)
            {
                _appIndicatorSetStatus?.Invoke(indicator, status);
            }

            internal static void app_indicator_set_menu(IntPtr indicator, IntPtr menu)
            {
                _appIndicatorSetMenu?.Invoke(indicator, menu);
            }

            internal static void app_indicator_set_icon_theme_path(IntPtr indicator, string iconThemePath)
            {
                _appIndicatorSetIconThemePath?.Invoke(indicator, iconThemePath);
            }

            internal static void app_indicator_set_icon_full(IntPtr indicator, string iconName, string iconDescription)
            {
                _appIndicatorSetIconFull?.Invoke(indicator, iconName, iconDescription);
            }

            public static bool TryLoadAppIndicator()
            {
                if (_appIndicatorLoadAttempted)
                {
                    return _appIndicatorLoaded;
                }

                _appIndicatorLoadAttempted = true;
                foreach (string libraryName in new[] { "libayatana-appindicator3.so.1", "libappindicator3.so.1" })
                {
                    if (TryLoadLibrary(libraryName, out IntPtr library) && TryBindAppIndicator(library))
                    {
                        _appIndicatorLoaded = true;
                        return true;
                    }
                }

                _appIndicatorLoaded = false;
                return false;
            }

            private static bool TryBindAppIndicator(IntPtr library)
            {
                try
                {
                    _appIndicatorNew = GetRequiredDelegate<AppIndicatorNewDelegate>(library, "app_indicator_new");
                    _appIndicatorSetStatus = GetRequiredDelegate<AppIndicatorSetStatusDelegate>(library, "app_indicator_set_status");
                    _appIndicatorSetMenu = GetRequiredDelegate<AppIndicatorSetMenuDelegate>(library, "app_indicator_set_menu");
                    _appIndicatorSetIconThemePath = TryGetDelegate<AppIndicatorSetIconThemePathDelegate>(library, "app_indicator_set_icon_theme_path");
                    _appIndicatorSetIconFull = TryGetDelegate<AppIndicatorSetIconFullDelegate>(library, "app_indicator_set_icon_full");
                    return true;
                }
                catch (EntryPointNotFoundException)
                {
                    _appIndicatorNew = null;
                    _appIndicatorSetStatus = null;
                    _appIndicatorSetMenu = null;
                    _appIndicatorSetIconThemePath = null;
                    _appIndicatorSetIconFull = null;
                    return false;
                }
            }

            private static bool TryLoadLibrary(string name, out IntPtr handle)
            {
                if (NativeLibrary.TryLoad(name, out handle))
                {
                    return true;
                }

                handle = dlopen(name, RtldLazy);
                return handle != IntPtr.Zero;
            }

            private static T GetRequiredDelegate<T>(IntPtr library, string symbol)
                where T : Delegate
            {
                if (TryGetSymbol(library, symbol, out IntPtr address))
                {
                    return Marshal.GetDelegateForFunctionPointer<T>(address);
                }

                throw new EntryPointNotFoundException(symbol);
            }

            private static T? TryGetDelegate<T>(IntPtr library, string symbol)
                where T : Delegate
            {
                return TryGetSymbol(library, symbol, out IntPtr address)
                    ? Marshal.GetDelegateForFunctionPointer<T>(address)
                    : null;
            }

            private static bool TryGetSymbol(IntPtr library, string symbol, out IntPtr address)
            {
                if (NativeLibrary.TryGetExport(library, symbol, out address))
                {
                    return true;
                }

                address = dlsym(library, symbol);
                return address != IntPtr.Zero;
            }

            [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr dlopen(string fileName, int flags);

            [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr dlsym(IntPtr handle, string symbol);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool gtk_init_check(ref int argc, ref IntPtr argv);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr gtk_menu_new();

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr gtk_menu_item_new_with_label(string label);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void gtk_menu_shell_append(IntPtr menuShell, IntPtr child);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void gtk_widget_show(IntPtr widget);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void gtk_widget_set_sensitive(IntPtr widget, bool sensitive);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void gtk_menu_popup_at_pointer(IntPtr menu, IntPtr triggerEvent);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void gtk_menu_popup(IntPtr menu, IntPtr parentMenuShell, IntPtr parentMenuItem, IntPtr func, IntPtr data, uint button, uint activateTime);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr gtk_status_icon_new();

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void gtk_status_icon_set_from_file(IntPtr statusIcon, string filename);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void gtk_status_icon_set_from_icon_name(IntPtr statusIcon, string iconName);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void gtk_status_icon_set_visible(IntPtr statusIcon, bool visible);

            [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void gtk_status_icon_set_tooltip_text(IntPtr statusIcon, string tooltipText);

            [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool g_main_context_pending(IntPtr context);

            [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool g_main_context_iteration(IntPtr context, bool mayBlock);

            [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
            internal static extern nint g_signal_connect_data(
                IntPtr instance,
                string detailedSignal,
                IntPtr cHandler,
                IntPtr data,
                IntPtr destroyData,
                int connectFlags);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate IntPtr AppIndicatorNewDelegate(string id, string iconName, int category);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void AppIndicatorSetStatusDelegate(IntPtr indicator, int status);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void AppIndicatorSetMenuDelegate(IntPtr indicator, IntPtr menu);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void AppIndicatorSetIconThemePathDelegate(IntPtr indicator, string iconThemePath);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void AppIndicatorSetIconFullDelegate(IntPtr indicator, string iconName, string iconDescription);
        }
    }

    private sealed class MacDesktopSpriteTray : IDesktopSpriteTray
    {
        private readonly List<DesktopSpriteTrayMenuItemSettings> _items;
        private readonly Action<DesktopSpriteTrayMenuItemSettings> _onMenuItemClicked;
        private readonly IntPtr _statusItem;
        private readonly IntPtr _target;
        private readonly IntPtr _menu;
        private bool _disposed;

        private MacDesktopSpriteTray(
            IntPtr statusItem,
            IntPtr target,
            IntPtr menu,
            IEnumerable<DesktopSpriteTrayMenuItemSettings> items,
            Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked)
        {
            _statusItem = statusItem;
            _target = target;
            _menu = menu;
            _items = items
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Select(CloneMenuItem)
                .ToList();
            _onMenuItemClicked = onMenuItemClicked;
        }

        public static MacDesktopSpriteTray? TryCreate(
            string iconPath,
            IEnumerable<DesktopSpriteTrayMenuItemSettings> items,
            Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return null;
            }

            if (!MacNative.TryLoadFrameworks())
            {
                return null;
            }

            IntPtr appKit = MacNative.objc_getClass("NSApplication");
            if (appKit != IntPtr.Zero)
            {
                IntPtr sharedApp = MacNative.objc_msgSend(appKit, MacNative.sel_registerName("sharedApplication"));
                if (sharedApp != IntPtr.Zero)
                {
                    MacNative.objc_msgSend_nint(sharedApp, MacNative.sel_registerName("setActivationPolicy:"), 1);
                }
            }

            IntPtr statusBarClass = MacNative.objc_getClass("NSStatusBar");
            IntPtr systemStatusBar = MacNative.objc_msgSend(statusBarClass, MacNative.sel_registerName("systemStatusBar"));
            IntPtr statusItem = MacNative.objc_msgSend_Double(systemStatusBar, MacNative.sel_registerName("statusItemWithLength:"), -1.0);
            if (statusItem == IntPtr.Zero)
            {
                return null;
            }

            IntPtr menuClass = MacNative.objc_getClass("NSMenu");
            IntPtr menu = MacNative.objc_msgSend(MacNative.objc_msgSend(menuClass, MacNative.sel_registerName("alloc")), MacNative.sel_registerName("init"));
            IntPtr target = MacTrayTarget.EnsureTarget(onMenuItemClicked);
            if (target == IntPtr.Zero)
            {
                return null;
            }

            MacDesktopSpriteTray tray = new(statusItem, target, menu, items, onMenuItemClicked);
            tray.BuildMenu();
            tray.ConfigureStatusItem(iconPath);
            MacNative.objc_msgSend_IntPtr(statusItem, MacNative.sel_registerName("setMenu:"), menu);
            return tray;
        }

        public void Pump()
        {
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            MacTrayTarget.UnregisterTray(this);
            IntPtr statusBarClass = MacNative.objc_getClass("NSStatusBar");
            IntPtr systemStatusBar = MacNative.objc_msgSend(statusBarClass, MacNative.sel_registerName("systemStatusBar"));
            if (systemStatusBar != IntPtr.Zero && _statusItem != IntPtr.Zero)
            {
                MacNative.objc_msgSend_IntPtr(systemStatusBar, MacNative.sel_registerName("removeStatusItem:"), _statusItem);
            }
        }

        private void BuildMenu()
        {
            IntPtr menuItemClass = MacNative.objc_getClass("NSMenuItem");
            IntPtr action = MacNative.sel_registerName("desktopSpriteTrayMenuItem:");
            for (int i = 0; i < _items.Count; i++)
            {
                DesktopSpriteTrayMenuItemSettings item = _items[i];
                IntPtr title = MacNative.CreateNSString(item.Text);
                IntPtr menuItem = MacNative.objc_msgSend_IntPtr_IntPtr_IntPtr(
                    MacNative.objc_msgSend(menuItemClass, MacNative.sel_registerName("alloc")),
                    MacNative.sel_registerName("initWithTitle:action:keyEquivalent:"),
                    title,
                    action,
                    MacNative.CreateNSString(string.Empty));
                if (menuItem == IntPtr.Zero)
                {
                    continue;
                }

                MacTrayTarget.RegisterMenuItem(menuItem, this, i);
                MacNative.objc_msgSend_IntPtr(menuItem, MacNative.sel_registerName("setTarget:"), _target);
                MacNative.objc_msgSend_bool(menuItem, MacNative.sel_registerName("setEnabled:"), item.Enabled);
                MacNative.objc_msgSend_IntPtr(_menu, MacNative.sel_registerName("addItem:"), menuItem);
            }
        }

        private void ConfigureStatusItem(string iconPath)
        {
            IntPtr button = MacNative.objc_msgSend(_statusItem, MacNative.sel_registerName("button"));
            if (button == IntPtr.Zero)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                IntPtr imageClass = MacNative.objc_getClass("NSImage");
                IntPtr image = MacNative.objc_msgSend_ReturnIntPtr_IntPtr(
                    MacNative.objc_msgSend(imageClass, MacNative.sel_registerName("alloc")),
                    MacNative.sel_registerName("initWithContentsOfFile:"),
                    MacNative.CreateNSString(Path.GetFullPath(iconPath)));
                if (image != IntPtr.Zero)
                {
                    MacNative.objc_msgSend_bool(image, MacNative.sel_registerName("setTemplate:"), true);
                    MacNative.objc_msgSend_IntPtr(button, MacNative.sel_registerName("setImage:"), image);
                    return;
                }
            }

            MacNative.objc_msgSend_IntPtr(button, MacNative.sel_registerName("setTitle:"), MacNative.CreateNSString("DW"));
        }

        private void DispatchIndex(int index)
        {
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

        private sealed class MacTrayTarget
        {
            private static readonly object Sync = new();
            private static readonly Dictionary<IntPtr, (MacDesktopSpriteTray Tray, int Index)> MenuItems = [];
            private static readonly TrayMenuActionDelegate Callback = OnMenuItemSelected;
            private static IntPtr _target;

            public static IntPtr EnsureTarget(Action<DesktopSpriteTrayMenuItemSettings> onMenuItemClicked)
            {
                _ = onMenuItemClicked;
                lock (Sync)
                {
                    if (_target != IntPtr.Zero)
                    {
                        return _target;
                    }

                    IntPtr nsObject = MacNative.objc_getClass("NSObject");
                    IntPtr cls = MacNative.objc_lookUpClass("ZhengyanDigitalWifeTrayTarget");
                    if (cls == IntPtr.Zero)
                    {
                        cls = MacNative.objc_allocateClassPair(nsObject, "ZhengyanDigitalWifeTrayTarget", 0);
                        if (cls == IntPtr.Zero)
                        {
                            return IntPtr.Zero;
                        }

                        IntPtr selector = MacNative.sel_registerName("desktopSpriteTrayMenuItem:");
                        MacNative.class_addMethod(cls, selector, Marshal.GetFunctionPointerForDelegate(Callback), "v@:@");
                        MacNative.objc_registerClassPair(cls);
                    }

                    _target = MacNative.objc_msgSend(MacNative.objc_msgSend(cls, MacNative.sel_registerName("alloc")), MacNative.sel_registerName("init"));
                    return _target;
                }
            }

            public static void RegisterMenuItem(IntPtr menuItem, MacDesktopSpriteTray tray, int index)
            {
                lock (Sync)
                {
                    MenuItems[menuItem] = (tray, index);
                }
            }

            public static void UnregisterTray(MacDesktopSpriteTray tray)
            {
                lock (Sync)
                {
                    foreach (IntPtr menuItem in MenuItems
                        .Where(pair => ReferenceEquals(pair.Value.Tray, tray))
                        .Select(pair => pair.Key)
                        .ToArray())
                    {
                        MenuItems.Remove(menuItem);
                    }
                }
            }

            private static void OnMenuItemSelected(IntPtr self, IntPtr selector, IntPtr sender)
            {
                _ = self;
                _ = selector;
                (MacDesktopSpriteTray Tray, int Index) item;
                lock (Sync)
                {
                    if (!MenuItems.TryGetValue(sender, out item))
                    {
                        return;
                    }
                }

                item.Tray.DispatchIndex(item.Index);
            }

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void TrayMenuActionDelegate(IntPtr self, IntPtr selector, IntPtr sender);
        }

        private static class MacNative
        {
            internal static bool TryLoadFrameworks()
            {
                return NativeLibrary.TryLoad("/System/Library/Frameworks/Foundation.framework/Foundation", out _)
                    && NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);
            }

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
            internal static extern IntPtr sel_registerName(string selectorName);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
            internal static extern IntPtr objc_getClass(string className);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_lookUpClass")]
            internal static extern IntPtr objc_lookUpClass(string className);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_allocateClassPair")]
            internal static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_registerClassPair")]
            internal static extern void objc_registerClassPair(IntPtr cls);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_addMethod")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            internal static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            internal static extern void objc_msgSend_nint(IntPtr receiver, IntPtr selector, nint value);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            internal static extern void objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            internal static extern IntPtr objc_msgSend_ReturnIntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            internal static extern IntPtr objc_msgSend_Double(IntPtr receiver, IntPtr selector, double value);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            internal static extern IntPtr objc_msgSend_IntPtr_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

            internal static IntPtr CreateNSString(string value)
            {
                IntPtr nsString = objc_getClass("NSString");
                IntPtr utf8 = Marshal.StringToHGlobalAnsi(value ?? string.Empty);
                try
                {
                    return objc_msgSend_ReturnIntPtr_IntPtr(
                        nsString,
                        sel_registerName("stringWithUTF8String:"),
                        utf8);
                }
                finally
                {
                    Marshal.FreeHGlobal(utf8);
                }
            }
        }
    }
}
