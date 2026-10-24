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
            using (var dialog = new Form
            {
                Text = applicationName + " - Keyboard shortcuts",
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = true,
                ClientSize = new Size(445, 264),
                Font = new Font("Segoe UI", 9F)
            })
            {
                var introduction = new Label
                {
                    AutoSize = false,
                    Location = new Point(18, 14),
                    Size = new Size(410, 40),
                    Text = "Press Ctrl+Alt+letter or Ctrl+Alt+F1-F12 in each box.\r\nPress Delete to remove an assignment."
                };

                var firstLabel = new Label
                {
                    Location = new Point(18, 67),
                    Size = new Size(190, 22),
                    Text = firstMode
                };
                var firstInput = CreateShortcutInput(18, 91, 408);
                firstInput.Text = ReadShortcut(applicationName, firstMode) ?? firstSuggestion;

                var secondLabel = new Label
                {
                    Location = new Point(18, 127),
                    Size = new Size(190, 22),
                    Text = secondMode
                };
                var secondInput = CreateShortcutInput(18, 151, 408);
                secondInput.Text = ReadShortcut(applicationName, secondMode) ?? secondSuggestion;

                var note = new Label
                {
                    Location = new Point(18, 187),
                    Size = new Size(408, 33),
                    ForeColor = Color.DimGray,
                    Text = "Windows Explorer activates the shortcuts; it must be running.\r\nMoving the executable requires saving these settings again."
                };

                var saveButton = new Button
                {
                    Text = "Save shortcuts",
                    Location = new Point(222, 227),
                    Size = new Size(120, 29)
                };
                var cancelButton = new Button
                {
                    Text = "Cancel",
                    Location = new Point(350, 227),
                    Size = new Size(78, 29),
                    DialogResult = DialogResult.Cancel
                };

                saveButton.Click += (sender, args) =>
                {
                    string first = firstInput.Text.Trim();
                    string second = secondInput.Text.Trim();
                    if (first.Length > 0 && string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(dialog, "The two modes cannot share the same shortcut.",
                            applicationName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        SaveShortcut(applicationName, firstMode, firstArgument, first);
                        SaveShortcut(applicationName, secondMode, secondArgument, second);
                        dialog.DialogResult = DialogResult.OK;
                        dialog.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(dialog, "Unable to save Windows shortcuts: " + ex.Message,
                            applicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                dialog.Controls.AddRange(new Control[]
                {
                    introduction, firstLabel, firstInput, secondLabel, secondInput,
                    note, saveButton, cancelButton
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
            string root = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrEmpty(root))
                throw new InvalidOperationException("The current user's Start Menu was not found.");

            // Names are fixed by callers; never take arbitrary file paths from user input.
            return Path.Combine(root, ShortcutFolderName,
                applicationName + " - " + mode + ".lnk");
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
