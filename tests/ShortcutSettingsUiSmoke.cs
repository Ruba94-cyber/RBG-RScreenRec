using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using FlaButton = FlaUI.Core.AutomationElements.Button;
using FlaTextBox = FlaUI.Core.AutomationElements.TextBox;

internal static class ShortcutSettingsUiSmoke
{
    private const string SettingsTitle = "RBG RScreenRec - Settings";
    private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(10);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: ShortcutSettingsUiSmoke <RBG - RScreenRec.exe>");
            return 2;
        }

        string executable = Path.GetFullPath(args[0]);
        string testRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        bool success = false;
        Process activeApp = null;
        try
        {
            using (var automation = new UIA3Automation())
            {
                var desktop = automation.GetDesktop();
                activeApp = Launch(executable, testRoot, true);
                Window settings = WaitForWindow(desktop, SettingsTitle, activeApp.Id, UiTimeout);
                if (GetShortcutInputs(settings).Length != 3)
                    throw new InvalidOperationException("Ctrl+Shift launch did not open the three-mode settings dialog.");
                ClickButton(settings, "Cancel");
                if (!activeApp.WaitForExit(5000) || activeApp.ExitCode != 0)
                    throw new InvalidOperationException("Cancel did not close the Ctrl+Shift settings launch cleanly.");
                activeApp.Dispose();
                activeApp = null;

                activeApp = Launch(executable, testRoot, false);
                settings = WaitForWindow(desktop, SettingsTitle, activeApp.Id, UiTimeout);
                settings.Focus();
                var shortcuts = GetShortcutInputs(settings);
                if (shortcuts.Length != 3)
                    throw new InvalidOperationException("Expected three RScreenRec shortcut inputs, found " + shortcuts.Length + ".");
                string[] entered = GetShortcutInputs(settings).Select(input => input.Text).ToArray();
                if (!HasHotkey(entered, "Ctrl+Alt+R") || !HasHotkey(entered, "Ctrl+Alt+W") || !HasHotkey(entered, "Ctrl+Alt+P"))
                    throw new InvalidOperationException("The settings UI did not show the three default shortcuts: " + string.Join(" / ", entered));

                ClickButton(settings, "Save settings");
                Thread.Sleep(500);
                if (!activeApp.WaitForExit(5000))
                {
                    string openWindows = string.Join(" | ", desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                        .Select(window => window.Name + " [" + string.Join(",", window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)).Select(button => button.Name)) + "]"));
                    string currentInputs = string.Join(" / ", GetShortcutInputs(settings).Select(input => input.Text));
                    string errorText = string.Join(" | ", settings.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                        .Select(element => element.Name).Where(name => name.IndexOf("Could not save settings", StringComparison.OrdinalIgnoreCase) >= 0));
                    throw new TimeoutException("Saving RScreenRec settings left the process open. Inputs: " + currentInputs + ". Error text: " + errorText + ". Visible windows/buttons: " + openWindows);
                }
                if (activeApp.ExitCode != 0)
                    throw new InvalidOperationException("Saving RScreenRec settings exited with code " + activeApp.ExitCode + ".");
                activeApp.Dispose();
                activeApp = null;

                VerifySavedShortcuts(testRoot);

                activeApp = Launch(executable, testRoot, false);
                settings = WaitForWindow(desktop, SettingsTitle, activeApp.Id, UiTimeout);
                string[] saved = GetShortcutInputs(settings).Select(input => input.Text).ToArray();
                if (!HasHotkey(saved, "Ctrl+Alt+R") || !HasHotkey(saved, "Ctrl+Alt+W") ||
                    !HasHotkey(saved, "Ctrl+Alt+P"))
                    throw new InvalidOperationException("Saved shortcuts were not reloaded by the settings UI: " + string.Join(" / ", saved));
                ClickButton(settings, "Cancel");
                if (!activeApp.WaitForExit(5000) || activeApp.ExitCode != 0)
                    throw new InvalidOperationException("Cancel did not close the reopened settings dialog cleanly.");
                activeApp.Dispose();
                activeApp = null;
            }

            success = true;
            Console.WriteLine("FlaUI settings smoke passed: Ctrl+Shift launch, settings save/reopen, .lnk persistence for all three default modes, and cancel.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (activeApp != null)
            {
                try
                {
                    if (!activeApp.HasExited)
                    {
                        activeApp.Kill();
                        activeApp.WaitForExit(3000);
                    }
                }
                catch { }
                activeApp.Dispose();
            }
            if (success && Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
            else
                Console.Error.WriteLine("Settings UI test data retained at: " + testRoot);
        }
    }

    private static Process Launch(string executable, string testRoot, bool useCtrlShift)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)
        };
        start.EnvironmentVariables["RBG_SHORTCUT_TEST_ROOT"] = testRoot;
        start.EnvironmentVariables["RBG_VISUAL_SETTINGS_TEST_ROOT"] = testRoot;
        start.EnvironmentVariables["RSCREENREC_BENCHMARK_OUTPUT_DIRECTORY"] = testRoot;

        if (!useCtrlShift)
        {
            start.Arguments = "--settings";
            return Process.Start(start);
        }

        Keyboard.Press(VirtualKeyShort.LCONTROL);
        Keyboard.Press(VirtualKeyShort.LSHIFT);
        try
        {
            Process app = Process.Start(start);
            Thread.Sleep(300);
            return app;
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.LSHIFT);
            Keyboard.Release(VirtualKeyShort.LCONTROL);
        }
    }

    private static Window WaitForWindow(AutomationElement desktop, string title, int expectedProcessId, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            AutomationElement[] windows = desktop.FindAllChildren(cf => cf
                .ByName(title)
                .And(cf.ByControlType(ControlType.Window)));
            foreach (AutomationElement element in windows)
            {
                uint processId;
                GetWindowThreadProcessId(element.FrameworkAutomationElement.NativeWindowHandle.Value, out processId);
                if (processId == (uint)expectedProcessId)
                    return element.AsWindow();
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("FlaUI did not find the window owned by process " + expectedProcessId + ": " + title);
    }

    private static FlaTextBox[] GetShortcutInputs(Window settings)
    {
        return settings.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
            .Select(element => element.AsTextBox())
            .Where(input => input.Text.StartsWith("Ctrl+Alt+", StringComparison.OrdinalIgnoreCase) ||
                input.Text.StartsWith("Alt+Ctrl+", StringComparison.OrdinalIgnoreCase))
            .OrderBy(input => input.BoundingRectangle.Y)
            .ToArray();
    }

    private static bool HasHotkey(string[] values, string expected)
    {
        return values.Any(value => Normalize(value) == Normalize(expected));
    }

    private static void ClickButton(Window window, string name)
    {
        FlaButton button = FindButton(window, name);
        button.Click();
    }

    private static FlaButton FindButton(Window window, string name)
    {
        FlaButton button = window.FindFirstDescendant(cf => cf
            .ByName(name)
            .And(cf.ByControlType(ControlType.Button)))?.AsButton();
        if (button == null)
            throw new InvalidOperationException("Could not find button: " + name);
        return button;
    }

    private static void VerifySavedShortcuts(string root)
    {
        string[] links = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
        if (links.Length != 3)
            throw new InvalidOperationException("Expected three saved shortcuts, found " + links.Length + ".");

        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            throw new NotSupportedException("Windows Script Host is unavailable for shortcut verification.");
        object shell = Activator.CreateInstance(shellType);
        try
        {
            var actual = links.Select(path =>
            {
                object shortcut = null;
                try
                {
                    shortcut = ((dynamic)shell).CreateShortcut(path);
                    return new
                    {
                        Hotkey = (string)((dynamic)shortcut).Hotkey,
                        Arguments = (string)((dynamic)shortcut).Arguments
                    };
                }
                finally
                {
                    if (shortcut != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                }
            }).ToArray();

            if (!actual.Any(link => Normalize(link.Hotkey) == Normalize("Ctrl+Alt+R") && link.Arguments == "--record-screen") ||
                !actual.Any(link => Normalize(link.Hotkey) == Normalize("Ctrl+Alt+W") && link.Arguments == "--record-window") ||
                !actual.Any(link => Normalize(link.Hotkey) == Normalize("Ctrl+Alt+P") && link.Arguments == "--toggle-pause"))
                throw new InvalidOperationException("The saved Windows shortcuts did not preserve hotkeys and mode arguments.");
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    private static string Normalize(string hotkey)
    {
        return string.Join("+", hotkey.Split('+').Select(part => part.Trim().ToUpperInvariant()).OrderBy(part => part, StringComparer.Ordinal));
    }
}
