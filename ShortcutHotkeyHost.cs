using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RScreenRec
{
    // Keeps the configured shortcuts available when the recorder is not running.
    // The Start Menu links remain usable if this helper is stopped.
    internal sealed class ShortcutHotkeyHost
    {
        private const string HostArgument = "--shortcut-host";
        private const string MutexName = @"Local\RBG.RScreenRec.ShortcutHost";
        private const string StopEventName = @"Local\RBG.RScreenRec.ShortcutHost.Stop";
        private const string ReadyEventName = @"Local\RBG.RScreenRec.ShortcutHost.Ready";
        private const string StartupLinkName = "RBG RScreenRec - Shortcut host.lnk";

        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        private sealed class Binding
        {
            internal int Key;
            internal bool Shift;
            internal string Arguments;
        }

        private readonly List<Binding> bindings;
        private readonly HashSet<int> modifiersDown = new HashSet<int>();
        private readonly HashSet<int> initiallyHeld = new HashSet<int>();
        private readonly Dictionary<int, Binding> held = new Dictionary<int, Binding>();
        private readonly HookProc callback;
        private IntPtr hook;

        private ShortcutHotkeyHost(List<Binding> configured)
        {
            bindings = configured;
            callback = OnKey;
        }

        internal static bool IsRequested(string[] args)
        {
            foreach (string arg in args)
                if (string.Equals(arg, HostArgument, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static void EnsureRunning()
        {
            if (IsIsolated() || HostRunning()) return;
            try
            {
                if (LoadBindings().Count == 0) return;
                EnsureStartupLink();
                StartHost();
            }
            catch (Exception ex) { Trace.WriteLine("Shortcut host startup failed: " + ex); }
        }

        internal static void RefreshAfterSettingsSave()
        {
            if (IsIsolated()) return;
            bool enabled = LoadBindings().Count > 0;
            if (enabled) EnsureStartupLink();
            else RemoveStartupLink();
            StopHost();
            if (!enabled) return;
            StartHost();
            if (!WaitUntilReady())
                throw new IOException("The keyboard shortcut helper could not start. Please save the settings again.");
        }

        internal static void Run()
        {
            bool first;
            using (var mutex = new Mutex(true, MutexName, out first))
            {
                if (!first) return;
                try
                {
                    List<Binding> configured = LoadBindings();
                    if (configured.Count > 0) new ShortcutHotkeyHost(configured).RunLoop();
                }
                catch (Exception ex) { Trace.WriteLine("Shortcut host failed: " + ex); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private void RunLoop()
        {
            using (var context = new ApplicationContext())
            using (var control = new Control())
            using (var stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName))
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName))
            using (var timer = new System.Windows.Forms.Timer { Interval = 200 })
            {
                IntPtr handle = control.Handle;
                foreach (Binding binding in bindings)
                    if (IsDown(binding.Key)) initiallyHeld.Add(binding.Key);
                foreach (int key in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
                    if (IsDown(key)) modifiersDown.Add(key);
                hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
                if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    timer.Tick += (sender, args) => { if (stop.WaitOne(0)) context.ExitThread(); };
                    timer.Start();
                    ready.Set();
                    Application.Run(context);
                }
                finally
                {
                    timer.Stop();
                    UnhookWindowsHookEx(hook);
                    hook = IntPtr.Zero;
                }
            }
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int kind = message.ToInt32();
                bool down = kind == 0x100 || kind == 0x104;
                bool up = kind == 0x101 || kind == 0x105;
                if (down || up)
                {
                    int key = Marshal.ReadInt32(data);
                    int scanCode = Marshal.ReadInt32(data, 4);
                    int flags = Marshal.ReadInt32(data, 8);
                    int modifier = NormalizeModifier(key, scanCode, flags);
                    if ((modifier >= 0xA0 && modifier <= 0xA5) || modifier == 0x5B || modifier == 0x5C)
                    {
                        if (down) modifiersDown.Add(modifier);
                        else modifiersDown.Remove(modifier);
                        return CallNextHookEx(hook, code, message, data);
                    }
                    if (initiallyHeld.Contains(key))
                    {
                        if (up) initiallyHeld.Remove(key);
                        if (down) return new IntPtr(1);
                        return CallNextHookEx(hook, code, message, data);
                    }
                    Binding active;
                    if (held.TryGetValue(key, out active))
                    {
                        if (up)
                        {
                            held.Remove(key);
                            ThreadPool.QueueUserWorkItem(state => Launch((string)state), active.Arguments);
                        }
                        return new IntPtr(1);
                    }
                    if (down && (modifiersDown.Contains(0xA2) || modifiersDown.Contains(0xA3)) &&
                        (modifiersDown.Contains(0xA4) || modifiersDown.Contains(0xA5)) &&
                        !modifiersDown.Contains(0x5B) && !modifiersDown.Contains(0x5C))
                    {
                        bool shift = modifiersDown.Contains(0xA0) || modifiersDown.Contains(0xA1);
                        foreach (Binding binding in bindings)
                            if (binding.Key == key && binding.Shift == shift)
                            {
                                held.Add(key, binding);
                                return new IntPtr(1);
                            }
                    }
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }

        private static int NormalizeModifier(int key, int scanCode, int flags)
        {
            if (key == 0x10) return scanCode == 0x36 ? 0xA1 : 0xA0;
            if (key == 0x11) return (flags & 1) != 0 ? 0xA3 : 0xA2;
            if (key == 0x12) return (flags & 1) != 0 ? 0xA5 : 0xA4;
            return key;
        }

        private static bool IsDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

        private static List<Binding> LoadBindings()
        {
            var result = new List<Binding>();
            Add(result, "Screen recording", "--record-screen");
            Add(result, "Window recording", "--record-window");
            Add(result, "Pause - resume", "--toggle-pause");
            return result;
        }

        private static void Add(List<Binding> result, string mode, string arguments)
        {
            string text = ShortcutSettings.ReadShortcut("RBG RScreenRec", mode);
            if (string.IsNullOrWhiteSpace(text)) return;
            bool shift = false, control = false, alt = false;
            Keys key = Keys.None;
            foreach (string part in text.Split('+'))
            {
                string token = part.Trim();
                if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || token.Equals("Control", StringComparison.OrdinalIgnoreCase)) control = true;
                else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
                else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
                else if (!Enum.TryParse(token, true, out key)) return;
            }
            if (control && alt && key != Keys.None)
                result.Add(new Binding { Key = (int)key, Shift = shift, Arguments = arguments });
        }

        private static void Launch(string arguments)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(Application.ExecutablePath, arguments)
                {
                    WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                })) { }
            }
            catch (Exception ex) { Trace.WriteLine("Shortcut launch failed: " + ex); }
        }

        private static bool IsIsolated()
        {
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RBG_SHORTCUT_TEST_ROOT"));
        }

        private static bool HostRunning()
        {
            try { using (Mutex.OpenExisting(MutexName)) return true; }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return true; }
        }

        private static void StartHost()
        {
            using (Process.Start(new ProcessStartInfo(Application.ExecutablePath, HostArgument)
            {
                WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath),
                UseShellExecute = false,
                CreateNoWindow = true
            })) { }
        }

        private static void StopHost()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (HostRunning() && DateTime.UtcNow < deadline)
            {
                try
                {
                    using (var stop = EventWaitHandle.OpenExisting(StopEventName)) stop.Set();
                }
                catch (WaitHandleCannotBeOpenedException) { }
                Thread.Sleep(50);
            }
            if (HostRunning()) throw new IOException("The previous shortcut helper did not stop.");
        }

        private static bool WaitUntilReady()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using (var ready = EventWaitHandle.OpenExisting(ReadyEventName))
                        if (ready.WaitOne(100)) return true;
                }
                catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(50); }
            }
            return false;
        }

        private static string StartupLinkPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLinkName);
        }

        private static void EnsureStartupLink()
        {
            string path = StartupLinkPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) throw new NotSupportedException("Windows Script Host is not available.");
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(type);
                shortcut = ((dynamic)shell).CreateShortcut(path);
                dynamic link = shortcut;
                if (File.Exists(path) &&
                    string.Equals((string)link.TargetPath, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string)link.Arguments, HostArgument, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace((string)link.Hotkey))
                    return;
                link.TargetPath = Application.ExecutablePath;
                link.Arguments = HostArgument;
                link.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                link.Description = "Keep RScreenRec keyboard shortcuts ready";
                link.IconLocation = Application.ExecutablePath + ",0";
                link.Hotkey = "";
                link.Save();
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void RemoveStartupLink()
        {
            string path = StartupLinkPath();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
