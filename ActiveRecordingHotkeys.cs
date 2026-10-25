using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RScreenRec
{
    // Exists only while recording. Explorer still launches the app when it is idle.
    internal sealed class ActiveRecordingHotkeys : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string name);

        private sealed class Binding
        {
            internal Keys Key;
            internal bool Shift;
            internal Action Action;
        }

        private readonly List<Binding> bindings = new List<Binding>();
        private readonly Dictionary<int, Action> held = new Dictionary<int, Action>();
        private readonly HashSet<int> initiallyHeld = new HashSet<int>();
        private readonly HashSet<int> modifiersDown = new HashSet<int>();
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim();
        private readonly HookProc callback;
        private Thread thread;
        private Control dispatcher;
        private ApplicationContext context;
        private IntPtr hook;
        private Exception failure;
        private volatile bool disposed;

        internal ActiveRecordingHotkeys(Action stop, Action pause)
        {
            Add("Screen recording", stop);
            Add("Window recording", stop);
            Add("Pause - resume", pause);
            callback = OnKey;
            if (bindings.Count == 0) return;
            thread = new Thread(Run) { IsBackground = true, Name = "ActiveRecordingHotkeys" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.Wait(5000)) { Dispose(); throw new TimeoutException("Recording hotkey initialization timed out."); }
            if (failure != null) { ready.Dispose(); throw new InvalidOperationException("Recording hotkeys could not start.", failure); }
        }

        private void Add(string mode, Action action)
        {
            string text = ShortcutSettings.ReadShortcut("RBG RScreenRec", mode);
            if (string.IsNullOrWhiteSpace(text)) return; // Explicitly disabled by the user.
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
                bindings.Add(new Binding { Key = key, Shift = shift, Action = action });
        }

        private void Run()
        {
            try
            {
                using (dispatcher = new Control())
                using (context = new ApplicationContext())
                {
                    IntPtr handle = dispatcher.Handle;
                    foreach (Binding binding in bindings)
                        if (IsDown((int)binding.Key)) initiallyHeld.Add((int)binding.Key);
                    foreach (int key in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
                        if (IsDown(key)) modifiersDown.Add(key);
                    hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
                    if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    Logger.Log("Active recording hotkeys ready. Bindings=" + bindings.Count + " [" +
                        string.Join(", ", bindings.ConvertAll(binding => (binding.Shift ? "Shift+" : "") + binding.Key)) + "]");
                    ready.Set();
                    if (!disposed) Application.Run(context);
                }
            }
            catch (Exception ex) { failure = ex; ready.Set(); }
            finally { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); }
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int kind = message.ToInt32();
                int key = Marshal.ReadInt32(data);
                bool down = kind == 0x100 || kind == 0x104;
                bool up = kind == 0x101 || kind == 0x105;
                int modifier = key == 0x10 ? 0xA0 : key == 0x11 ? 0xA2 : key == 0x12 ? 0xA4 : key;
                if ((modifier >= 0xA0 && modifier <= 0xA5) || modifier == 0x5B || modifier == 0x5C)
                {
                    if (down) modifiersDown.Add(modifier);
                    else if (up) modifiersDown.Remove(modifier);
                    return CallNextHookEx(hook, code, message, data);
                }
                if (initiallyHeld.Contains(key))
                {
                    if (up) initiallyHeld.Remove(key);
                    if (down) return new IntPtr(1); // Do not treat launch-key autorepeat as a new command.
                    return CallNextHookEx(hook, code, message, data);
                }
                Action action;
                if (held.TryGetValue(key, out action))
                {
                    if (up)
                    {
                        held.Remove(key);
                        // Act on release so Explorer never receives an unpaired main-key event.
                        try { action(); } catch (ObjectDisposedException) { }
                    }
                    return new IntPtr(1);
                }
                // LL callbacks run before the asynchronous key state is updated. Track modifier events instead.
                if (down && (modifiersDown.Contains(0xA2) || modifiersDown.Contains(0xA3)) &&
                    (modifiersDown.Contains(0xA4) || modifiersDown.Contains(0xA5)) &&
                    !modifiersDown.Contains(0x5B) && !modifiersDown.Contains(0x5C))
                {
                    bool shift = modifiersDown.Contains(0xA0) || modifiersDown.Contains(0xA1);
                    foreach (Binding binding in bindings)
                        if ((int)binding.Key == key && binding.Shift == shift)
                        {
                            held.Add(key, binding.Action);
                            return new IntPtr(1);
                        }
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }

        private static bool IsDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Control current = dispatcher;
            try { if (current != null && current.IsHandleCreated) current.BeginInvoke(new Action(() => context.ExitThread())); }
            catch (InvalidOperationException) { }
            if (thread != null && thread.IsAlive) thread.Join(2000);
            if (thread == null || !thread.IsAlive) ready.Dispose();
        }
    }
}
