using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RScreenRec
{
    /// <summary>
    /// Windows Explorer handles hotkeys assigned to Start Menu shortcuts.
    /// No application process or keyboard hook has to remain running.
    /// </summary>
    internal static class ShortcutSettings
    {
        private const string ShortcutFolderName = "RBG Utilities";

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        public static void Show(string applicationName, string firstMode, string secondMode,
            string firstArgument, string secondArgument, string firstSuggestion, string secondSuggestion)
        {
            bool recorder = applicationName.IndexOf("RScreenRec", StringComparison.OrdinalIgnoreCase) >= 0;
            const string thirdMode = "Pause / resume";
            const string thirdArgument = "--toggle-pause";
            using (var dialog = new Form
            {
                Text = applicationName + " - Settings",
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.Sizable,
                MaximizeBox = true,
                MinimizeBox = false,
                ShowInTaskbar = true,
                AutoScaleDimensions = new SizeF(96F, 96F),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScroll = true,
                ClientSize = new Size(550, recorder ? 490 : 445),
                Font = new Font("Segoe UI", 9F)
            })
            {
                var introduction = new Label
                {
                    Location = new Point(22, 18),
                    Size = new Size(506, 52),
                    Text = "Assign Ctrl+Alt+letter or Ctrl+Alt+F1-F12 combinations.\r\nPress Delete in a shortcut box to remove the assignment."
                };
                var firstLabel = new Label
                {
                    Location = new Point(22, 80), Size = new Size(506, 25), Text = firstMode
                };
                var firstInput = CreateShortcutInput(22, 110, 506);
                firstInput.Text = ReadShortcut(applicationName, firstMode) ?? firstSuggestion;
                var secondLabel = new Label
                {
                    Location = new Point(22, 155), Size = new Size(506, 25), Text = secondMode
                };
                var secondInput = CreateShortcutInput(22, 185, 506);
                secondInput.Text = ReadShortcut(applicationName, secondMode) ?? secondSuggestion;

                var thirdLabel = new Label
                {
                    Location = new Point(22, 230), Size = new Size(506, 25),
                    Text = thirdMode, Visible = recorder
                };
                var thirdInput = CreateShortcutInput(22, 260, 506);
                thirdInput.Visible = recorder;
                if (recorder)
                    thirdInput.Text = ReadShortcut(applicationName, thirdMode) ?? "Ctrl+Alt+P";

                var badgeLabel = new Label
                {
                    Text = "REC badge size (%)", Location = new Point(22, 315),
                    Size = new Size(280, 28), Visible = recorder
                };
                var badgeScale = new NumericUpDown
                {
                    Location = new Point(418, 310), Size = new Size(110, 32),
                    Minimum = 70, Maximum = 130, Increment = 5,
                    Value = VisualPreferences.BadgeSizePercent, Visible = recorder
                };

                var borderLabel = new Label
                {
                    Text = "Cyan border fade (milliseconds)", Location = new Point(22, 236),
                    Size = new Size(340, 28), Visible = !recorder
                };
                var fadeDuration = new NumericUpDown
                {
                    Location = new Point(418, 231), Size = new Size(110, 32),
                    Minimum = 120, Maximum = 500, Increment = 20,
                    Value = VisualPreferences.BorderFadeDurationMs, Visible = !recorder
                };
                var magnifier = new CheckBox
                {
                    Text = "Show optional pixel magnifier when selecting an area",
                    Location = new Point(22, 274), Size = new Size(506, 30),
                    Checked = VisualPreferences.SelectionMagnifierEnabled,
                    Visible = !recorder
                };

                var note = new Label
                {
                    Location = new Point(22, recorder ? 354 : 315),
                    Size = new Size(506, 50),
                    ForeColor = Color.DimGray,
                    Text = "Windows Explorer handles global shortcuts; no background app.\r\nIf the executable is moved, save shortcuts again."
                };

                var saveButton = new Button
                {
                    Text = "Save settings",
                    Location = new Point(300, recorder ? 420 : 382),
                    Size = new Size(137, 34)
                };
                var cancelButton = new Button
                {
                    Text = "Cancel",
                    Location = new Point(448, recorder ? 420 : 382),
                    Size = new Size(80, 34),
                    DialogResult = DialogResult.Cancel
                };
                saveButton.Click += (sender, args) =>
                {
                    string first = firstInput.Text.Trim();
                    string second = secondInput.Text.Trim();
                    string third = recorder ? thirdInput.Text.Trim() : "";
                    if (!HasDistinctShortcuts(first, second, third, recorder))
                    {
                        MessageBox.Show(dialog, "Each mode must use a different shortcut.",
                            applicationName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        string conflict = FindShortcutConflict(applicationName, firstMode,
                            first, secondMode, second);
                        if (recorder && conflict == null)
                            conflict = FindShortcutConflict(applicationName, thirdMode,
                                third, firstMode, first);
                        if (conflict != null)
                        {
                            MessageBox.Show(dialog, conflict, applicationName,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        string oldFirst = ReadShortcut(applicationName, firstMode);
                        string oldSecond = ReadShortcut(applicationName, secondMode);
                        string oldThird = recorder ? ReadShortcut(applicationName, thirdMode) : null;
                        try
                        {
                            SaveShortcut(applicationName, firstMode, firstArgument, first);
                            SaveShortcut(applicationName, secondMode, secondArgument, second);
                            if (recorder)
                                SaveShortcut(applicationName, thirdMode, thirdArgument, third);

                            if (!MatchesShortcut(ReadShortcut(applicationName, firstMode), first) ||
                                !MatchesShortcut(ReadShortcut(applicationName, secondMode), second) ||
                                (recorder && !MatchesShortcut(ReadShortcut(applicationName, thirdMode), third)))
                                throw new IOException("Windows did not persist the shortcut keys.");

                            if (recorder)
                                VisualPreferences.BadgeSizePercent = (int)badgeScale.Value;
                            else
                            {
                                VisualPreferences.BorderFadeDurationMs = (int)fadeDuration.Value;
                                VisualPreferences.SelectionMagnifierEnabled = magnifier.Checked;
                            }
                        }
                        catch
                        {
                            try { SaveShortcut(applicationName, firstMode, firstArgument, oldFirst ?? ""); }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                            try { SaveShortcut(applicationName, secondMode, secondArgument, oldSecond ?? ""); }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                            if (recorder)
                            {
                                try { SaveShortcut(applicationName, thirdMode, thirdArgument, oldThird ?? ""); }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                            }
                            throw;
                        }

                        dialog.DialogResult = DialogResult.OK;
                        dialog.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(dialog, "Could not save settings: " + ex.Message,
                            applicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                dialog.Controls.AddRange(new Control[]
                {
                    introduction, firstLabel, firstInput, secondLabel, secondInput,
                    thirdLabel, thirdInput, badgeLabel, badgeScale, borderLabel,
                    fadeDuration, magnifier, note, saveButton, cancelButton
                });
                dialog.AcceptButton = saveButton;
                dialog.CancelButton = cancelButton;
                dialog.Shown += (sender, args) =>
                {
                    Rectangle workArea = Screen.FromControl(dialog).WorkingArea;
                    int maxWidth = Math.Max(1, workArea.Width - 24);
                    int maxHeight = Math.Max(1, workArea.Height - 48);
                    if (dialog.Width > maxWidth || dialog.Height > maxHeight)
                    {
                        dialog.Size = new Size(
                            Math.Min(dialog.Width, maxWidth),
                            Math.Min(dialog.Height, maxHeight));
                    }

                    dialog.Location = new Point(
                        workArea.Left + Math.Max(0, (workArea.Width - dialog.Width) / 2),
                        workArea.Top + Math.Max(0, (workArea.Height - dialog.Height) / 2));
                    dialog.TopMost = true;
                    dialog.Activate();
                    dialog.BringToFront();
                    SetForegroundWindow(dialog.Handle);
                    dialog.BeginInvoke((Action)(() =>
                    {
                        if (dialog.IsDisposed)
                            return;
                        dialog.TopMost = false;
                        dialog.Activate();
                        SetForegroundWindow(dialog.Handle);
                    }));
                };
                dialog.ShowDialog();
            }
        }

        private static TextBox CreateShortcutInput(int left, int top, int width)
        {
            var input = new TextBox
            {
                Location = new Point(left, top),
                Size = new Size(width, 25),
                ReadOnly = true,
                ShortcutsEnabled = false
            };

            input.KeyDown += (sender, e) =>
            {
                if (!e.Control && !e.Alt && !e.Shift &&
                    (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back))
                {
                    input.Clear();
                }
                else if (e.Control && e.Alt)
                {
                    Keys key = e.KeyCode;
                    if ((key >= Keys.A && key <= Keys.Z) ||
                        (key >= Keys.F1 && key <= Keys.F12))
                    {
                        input.Text = "Ctrl+Alt+" + (e.Shift ? "Shift+" : "") + key;
                    }
                }
                else if (e.KeyCode == Keys.Tab)
                {
                    return;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
            };
            return input;
        }

        private static string GetShortcutPath(string applicationName, string mode)
        {
            string root = Environment.GetEnvironmentVariable("RBG_SHORTCUT_TEST_ROOT");
            if (string.IsNullOrWhiteSpace(root))
                root = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrEmpty(root))
                throw new InvalidOperationException("The current user's Start Menu was not found.");

            // Names are fixed by callers; never take arbitrary file paths from user input.
            return Path.Combine(root, ShortcutFolderName,
                applicationName + " - " + SanitizeFileNamePart(mode) + ".lnk");
        }

        private static string SanitizeFileNamePart(string value)
        {
            string safe = value ?? "";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                safe = safe.Replace(invalid, '-');
            return safe.Trim();
        }

        private static bool MatchesShortcut(string persisted, string requested)
        {
            string[] left = (persisted ?? "").Replace(" ", "").Split('+');
            string[] right = (requested ?? "").Replace(" ", "").Split('+');
            Array.Sort(left, StringComparer.OrdinalIgnoreCase);
            Array.Sort(right, StringComparer.OrdinalIgnoreCase);
            return string.Equals(string.Join("+", left), string.Join("+", right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasDistinctShortcuts(string first, string second, string third, bool includeThird)
        {
            return !((first.Length > 0 && MatchesShortcut(first, second)) ||
                (includeThird && third.Length > 0 &&
                    (MatchesShortcut(first, third) || MatchesShortcut(second, third))));
        }

        // Detect conflicts within both RBG utilities. Explorer and other software can still
        // reserve keys independently; Windows does not expose a reliable universal lookup.
        private static string FindShortcutConflict(string app, string firstMode, string first,
            string secondMode, string second)
        {
            string directory = Path.GetDirectoryName(GetShortcutPath(app, firstMode));
            if (!Directory.Exists(directory))
                return null;
            string firstPath = GetShortcutPath(app, firstMode);
            string secondPath = GetShortcutPath(app, secondMode);
            foreach (string path in Directory.GetFiles(directory, "*.lnk"))
            {
                if (string.Equals(path, firstPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, secondPath, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(path).StartsWith(app + " - ",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                string hotkey = ReadHotkeyFromFile(path);
                if (!string.IsNullOrEmpty(hotkey) &&
                    (MatchesShortcut(hotkey, first) || MatchesShortcut(hotkey, second)))
                {
                    return "That shortcut is already assigned to " +
                        Path.GetFileNameWithoutExtension(path) + ". Choose another combination.";
                }
            }
            return null;
        }

        private static string ReadHotkeyFromFile(string path)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                shell = Activator.CreateInstance(type);
                shortcut = ((dynamic)shell).CreateShortcut(path);
                return (string)((dynamic)shortcut).Hotkey;
            }
            catch (Exception) { return null; }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }
        }

        private static string ReadShortcut(string applicationName, string mode)
        {
            string path = GetShortcutPath(applicationName, mode);
            if (!File.Exists(path))
                return null;

            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return "";
                shell = Activator.CreateInstance(shellType);
                shortcut = ((dynamic)shell).CreateShortcut(path);
                string value = (string)((dynamic)shortcut).Hotkey;
                return string.IsNullOrWhiteSpace(value) ? "" : value;
            }
            catch
            {
                return "";
            }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }
        }

        private static void SaveShortcut(string applicationName, string mode, string arguments, string hotkey)
        {
            string path = GetShortcutPath(applicationName, mode);
            if (string.IsNullOrEmpty(hotkey))
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new NotSupportedException("Windows Script Host is not available.");

            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = ((dynamic)shell).CreateShortcut(path);
                dynamic link = shortcut;
                link.TargetPath = Application.ExecutablePath;
                link.Arguments = arguments;
                link.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                link.Description = applicationName + " - " + mode;
                link.IconLocation = Application.ExecutablePath + ",0";
                link.Hotkey = hotkey;
                link.Save();
            }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }
        }

        private static void ReleaseComObject(object instance)
        {
            if (instance != null && Marshal.IsComObject(instance))
                Marshal.FinalReleaseComObject(instance);
        }
    }
}
