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
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = true,
                ClientSize = new Size(455, recorder ? 395 : 358),
                Font = new Font("Segoe UI", 9F)
            })
            {
                var introduction = new Label
                {
                    Location = new Point(18, 13),
                    Size = new Size(419, 43),
                    Text = "Assign Ctrl+Alt+letter or Ctrl+Alt+F1-F12 combinations.\r\nPress Delete in a shortcut box to remove the assignment."
                };
                var firstLabel = new Label
                {
                    Location = new Point(18, 65), Size = new Size(405, 21), Text = firstMode
                };
                var firstInput = CreateShortcutInput(18, 88, 418);
                firstInput.Text = ReadShortcut(applicationName, firstMode) ?? firstSuggestion;
                var secondLabel = new Label
                {
                    Location = new Point(18, 124), Size = new Size(405, 21), Text = secondMode
                };
                var secondInput = CreateShortcutInput(18, 147, 418);
                secondInput.Text = ReadShortcut(applicationName, secondMode) ?? secondSuggestion;

                var thirdLabel = new Label
                {
                    Location = new Point(18, 184), Size = new Size(405, 21),
                    Text = thirdMode, Visible = recorder
                };
                var thirdInput = CreateShortcutInput(18, 207, 418);
                thirdInput.Visible = recorder;
                if (recorder)
                    thirdInput.Text = ReadShortcut(applicationName, thirdMode) ?? "Ctrl+Alt+P";

                var badgeLabel = new Label
                {
                    Text = "REC badge size (%)", Location = new Point(18, 257),
                    Size = new Size(220, 23), Visible = recorder
                };
                var badgeScale = new NumericUpDown
                {
                    Location = new Point(338, 254), Size = new Size(98, 27),
                    Minimum = 70, Maximum = 130, Increment = 5,
                    Value = VisualPreferences.BadgeSizePercent, Visible = recorder
                };

                var borderLabel = new Label
                {
                    Text = "Cyan border fade (milliseconds)", Location = new Point(18, 198),
                    Size = new Size(285, 23), Visible = !recorder
                };
                var fadeDuration = new NumericUpDown
                {
                    Location = new Point(338, 195), Size = new Size(98, 27),
                    Minimum = 120, Maximum = 500, Increment = 20,
                    Value = VisualPreferences.BorderFadeDurationMs, Visible = !recorder
                };
                var magnifier = new CheckBox
                {
                    Text = "Show optional pixel magnifier when selecting an area",
                    Location = new Point(18, 240), Size = new Size(418, 25),
                    Checked = VisualPreferences.SelectionMagnifierEnabled,
                    Visible = !recorder
                };

                var note = new Label
                {
                    Location = new Point(18, recorder ? 294 : 275),
                    Size = new Size(418, 44),
                    ForeColor = Color.DimGray,
                    Text = "Windows Explorer handles global shortcuts; no background app.\r\nIf the executable is moved, save shortcuts again."
                };

                var saveButton = new Button
                {
                    Text = "Save settings",
                    Location = new Point(228, recorder ? 351 : 314),
                    Size = new Size(117, 30)
                };
                var cancelButton = new Button
                {
                    Text = "Cancel",
                    Location = new Point(357, recorder ? 351 : 314),
                    Size = new Size(79, 30),
                    DialogResult = DialogResult.Cancel
                };
                saveButton.Click += (sender, args) =>
                {
                    string first = firstInput.Text.Trim();
                    string second = secondInput.Text.Trim();
                    string third = recorder ? thirdInput.Text.Trim() : "";
                    if ((first.Length > 0 && MatchesShortcut(first, second)) ||
                        (recorder && third.Length > 0 &&
                            (MatchesShortcut(first, third) || MatchesShortcut(second, third))))
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
                applicationName + " - " + mode + ".lnk");
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
