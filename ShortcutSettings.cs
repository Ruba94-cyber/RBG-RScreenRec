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
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);

        private static void NotifyShortcutChanged(string path, uint eventId)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RBG_SHORTCUT_TEST_ROOT"))) return;
            SHChangeNotify(eventId, 0x1005, path, IntPtr.Zero); // PATHW | FLUSH
        }
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
            Screen screen = Screen.FromPoint(Cursor.Position);
            using (var dialog = new Form())
            {
                dialog.Text = applicationName + " - Settings";
                dialog.AutoScaleMode = AutoScaleMode.None;
                dialog.Font = new Font("Segoe UI", 10F);
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = screen.WorkingArea.Location;
                dialog.FormBorderStyle = FormBorderStyle.Sizable;
                dialog.MaximizeBox = true;
                dialog.MinimizeBox = false;
                dialog.BackColor = SystemColors.Window;
                float scale;
                using (Graphics graphics = dialog.CreateGraphics()) scale = graphics.DpiX / 96F;
                Func<int, int> px = value => (int)Math.Round(value * scale);
                dialog.ClientSize = new Size(
                    Math.Min(px(660), screen.WorkingArea.Width - px(48)),
                    Math.Min(px(recorder ? 520 : 470), screen.WorkingArea.Height - px(64)));

                var outer = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, Padding = new Padding(px(20)),
                    ColumnCount = 1, RowCount = 3
                };
                outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var introduction = new Label
                {
                    AutoSize = true, Dock = DockStyle.Fill,
                    Text = "Choose your keyboard shortcuts and visual preferences.",
                    Margin = new Padding(0, 0, 0, px(16))
                };
                var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0) };
                var fields = new TableLayoutPanel
                {
                    Dock = DockStyle.Top, AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
                    RowCount = 0, Padding = new Padding(0, 0, px(8), 0)
                };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
                Action<Control, Control> addRow = (label, control) =>
                {
                    int row = fields.RowCount++;
                    fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    label.Dock = DockStyle.Fill;
                    label.Margin = new Padding(0, px(5), px(12), px(12));
                    control.Dock = DockStyle.Fill;
                    control.Margin = new Padding(0, 0, 0, px(12));
                    fields.Controls.Add(label, 0, row);
                    fields.Controls.Add(control, 1, row);
                };
                Action<Control> addFullRow = control =>
                {
                    int row = fields.RowCount++;
                    fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    control.Dock = DockStyle.Fill;
                    fields.Controls.Add(control, 0, row);
                    fields.SetColumnSpan(control, 2);
                };
                var shortcutsTitle = new Label
                {
                    Text = "Keyboard shortcuts", AutoSize = true,
                    Font = new Font(dialog.Font, FontStyle.Bold),
                    Margin = new Padding(0, 0, 0, px(8))
                };
                addFullRow(shortcutsTitle);
                addFullRow(new Label
                {
                    Text = "Press Ctrl+Alt+letter or Ctrl+Alt+F1–F12 in a box. Add Shift if needed. Press Delete to clear.",
                    AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, px(16))
                });
                var firstInput = CreateShortcutInput(0, 0, px(250));
                firstInput.AccessibleName = firstMode + " shortcut";
                firstInput.Text = ReadShortcut(applicationName, firstMode) ?? firstSuggestion;
                addRow(new Label { Text = firstMode, AutoSize = true }, firstInput);
                var secondInput = CreateShortcutInput(0, 0, px(250));
                secondInput.AccessibleName = secondMode + " shortcut";
                secondInput.Text = ReadShortcut(applicationName, secondMode) ?? secondSuggestion;
                addRow(new Label { Text = secondMode, AutoSize = true }, secondInput);
                TextBox thirdInput = null;
                if (recorder)
                {
                    thirdInput = CreateShortcutInput(0, 0, px(250));
                    thirdInput.AccessibleName = "Pause / resume shortcut";
                    thirdInput.Text = ReadShortcut(applicationName, thirdMode) ?? "Ctrl+Alt+P";
                    addRow(new Label { Text = thirdMode, AutoSize = true }, thirdInput);
                }
                addFullRow(new Label
                {
                    Text = recorder ? "Recording appearance" : "Capture appearance",
                    Font = new Font(dialog.Font, FontStyle.Bold), AutoSize = true,
                    Margin = new Padding(0, px(10), 0, px(16))
                });
                NumericUpDown badgeScale = null;
                NumericUpDown fadeDuration = null;
                CheckBox magnifier = null;
                if (recorder)
                {
                    badgeScale = new NumericUpDown
                    {
                        Minimum = 70, Maximum = 130, Increment = 5,
                        Value = VisualPreferences.BadgeSizePercent, AccessibleName = "REC badge size (%)"
                    };
                    addRow(new Label { Text = "REC badge size (%)", AutoSize = true }, badgeScale);
                }
                else
                {
                    fadeDuration = new NumericUpDown
                    {
                        Minimum = 120, Maximum = 500, Increment = 20,
                        Value = VisualPreferences.BorderFadeDurationMs, AccessibleName = "Cyan border fade (milliseconds)"
                    };
                    addRow(new Label { Text = "Cyan border fade (milliseconds)", AutoSize = true }, fadeDuration);
                    magnifier = new CheckBox
                    {
                        Text = "Show optional pixel magnifier when selecting an area",
                        AutoSize = true, Checked = VisualPreferences.SelectionMagnifierEnabled,
                        Margin = new Padding(0, 0, 0, px(12))
                    };
                    addFullRow(magnifier);
                }
                addFullRow(new Label
                {
                    AutoSize = true, ForeColor = Color.DimGray,
                    Text = recorder
                        ? "Press a recording shortcut again to stop. Pause / resume keeps the video open.\r\nIf you move this app, save the shortcuts again from its new location."
                        : "Save to enable the shortcuts. If you move this app, save them again from its new location.",
                    Margin = new Padding(0, px(8), 0, 0)
                });
                viewport.Controls.Add(fields);
                var footer = new FlowLayoutPanel
                {
                    AutoSize = true, Dock = DockStyle.Fill, WrapContents = false,
                    FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, px(16), 0, 0)
                };
                var saveButton = new Button
                {
                    Text = "Save settings", AutoSize = true,
                    Padding = new Padding(px(12), px(6), px(12), px(6)),
                    MinimumSize = new Size(px(130), 0), Margin = new Padding(px(8), 0, 0, 0)
                };
                var cancelButton = new Button
                {
                    Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel,
                    Padding = new Padding(px(12), px(6), px(12), px(6)),
                    MinimumSize = new Size(px(100), 0), Margin = new Padding(px(8), 0, 0, 0)
                };
                footer.Controls.Add(cancelButton);
                footer.Controls.Add(saveButton);
                outer.Controls.Add(introduction, 0, 0);
                outer.Controls.Add(viewport, 0, 1);
                outer.Controls.Add(footer, 0, 2);
                dialog.Controls.Add(outer);
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
                dialog.AcceptButton = saveButton;
                dialog.CancelButton = cancelButton;
                dialog.Location = new Point(
                    screen.WorkingArea.Left + (screen.WorkingArea.Width - dialog.Width) / 2,
                    screen.WorkingArea.Top + (screen.WorkingArea.Height - dialog.Height) / 2);
                dialog.Shown += (sender, args) =>
                {
                    dialog.TopMost = true;
                    dialog.Activate();
                    dialog.BringToFront();
                    SetForegroundWindow(dialog.Handle);
                    firstInput.Focus();
                    dialog.BeginInvoke((Action)(() =>
                    {
                        if (dialog.IsDisposed) return;
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

        internal static string ReadShortcut(string applicationName, string mode)
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
                {
                    File.Delete(path);
                    NotifyShortcutChanged(path, 0x4);
                }
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new NotSupportedException("Windows Script Host is not available.");
            object shell = null;
            object shortcut = null;
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                // A fresh link registration is required: editing a cached key may leave it inactive.
                if (File.Exists(path))
                {
                    File.Delete(path);
                    NotifyShortcutChanged(path, 0x4); // DELETE
                }
                shortcut = ((dynamic)shell).CreateShortcut(path);
                dynamic link = shortcut;
                link.TargetPath = Application.ExecutablePath;
                link.Arguments = arguments;
                link.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                link.Description = applicationName + " - " + mode;
                link.IconLocation = Application.ExecutablePath + ",0";
                link.Hotkey = hotkey;
                link.Save();
                NotifyShortcutChanged(path, 0x2); // CREATE
            }
            catch
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        NotifyShortcutChanged(path, 0x4);
                    }
                    if (previous != null)
                    {
                        File.WriteAllBytes(path, previous);
                        NotifyShortcutChanged(path, 0x2);
                    }
                }
                catch (Exception restoreError) { System.Diagnostics.Debug.WriteLine(restoreError); }
                throw;
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
