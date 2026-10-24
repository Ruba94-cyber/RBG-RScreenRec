using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RScreenRec
{
    internal static class WindowPicker
    {
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private const uint GW_OWNER = 4;
        private const uint GA_ROOT = 2;

        internal static IntPtr SelectWindow()
        {
            var choices = GetWindows();
            using (var dialog = new Form())
            using (var search = new TextBox())
            using (var list = new ListBox())
            using (var pick = new Button())
            using (var confirm = new Button())
            using (var cancel = new Button())
            {
                dialog.Text = "Select a window - RScreenRec";
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new Size(560, 395);
                dialog.Font = new Font("Segoe UI", 9F);

                search.Location = new Point(12, 12);
                search.Size = new Size(536, 25);
                list.Location = new Point(12, 47);
                list.Size = new Size(536, 278);
                list.HorizontalScrollbar = true;
                search.TextChanged += (sender, args) =>
                {
                    string query = search.Text.Trim();
                    list.BeginUpdate();
                    try
                    {
                        list.Items.Clear();
                        foreach (WindowChoice choice in choices)
                        {
                            if (query.Length == 0 ||
                                choice.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                                list.Items.Add(choice);
                        }
                        if (list.Items.Count > 0) list.SelectedIndex = 0;
                    }
                    finally { list.EndUpdate(); }
                };
                foreach (WindowChoice choice in choices)
                    list.Items.Add(choice);
                if (list.Items.Count > 0) list.SelectedIndex = 0;

                pick.Text = "Click a window...";
                pick.Location = new Point(12, 350);
                pick.Size = new Size(168, 30);
                pick.Click += (sender, args) =>
                {
                    IntPtr selected = PickWindowByClick();
                    if (selected == IntPtr.Zero)
                    {
                        dialog.Activate();
                        return;
                    }
                    dialog.Tag = selected;
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };

                confirm.Text = "Select";
                confirm.Location = new Point(360, 350);
                confirm.Size = new Size(88, 30);
                confirm.DialogResult = DialogResult.OK;
                cancel.Text = "Cancel";
                cancel.Location = new Point(459, 350);
                cancel.Size = new Size(89, 30);
                cancel.DialogResult = DialogResult.Cancel;

                var note = new Label
                {
                    Text = "Restore minimized windows before selecting them.",
                    Location = new Point(12, 329),
                    Size = new Size(536, 18),
                    ForeColor = Color.DimGray
                };
                dialog.Controls.AddRange(new Control[] { search, list, note, pick, confirm, cancel });
                dialog.AcceptButton = confirm;
                dialog.CancelButton = cancel;
                list.DoubleClick += (sender, args) =>
                {
                    if (list.SelectedItem != null) dialog.DialogResult = DialogResult.OK;
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                    return IntPtr.Zero;
                if (dialog.Tag is IntPtr)
                    return IsEligibleWindow((IntPtr)dialog.Tag) ? (IntPtr)dialog.Tag : IntPtr.Zero;
                WindowChoice choiceSelected = list.SelectedItem as WindowChoice;
                return choiceSelected != null && IsEligibleWindow(choiceSelected.Handle)
                    ? choiceSelected.Handle : IntPtr.Zero;
            }
        }

        private static List<WindowChoice> GetWindows()
        {
            var choices = new List<WindowChoice>();
            EnumWindows((hwnd, unused) =>
            {
                if (!IsEligibleWindow(hwnd))
                    return true;
                int length = GetWindowTextLength(hwnd);
                if (length <= 0) return true;
                var title = new StringBuilder(length + 1);
                GetWindowText(hwnd, title, title.Capacity);
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                choices.Add(new WindowChoice { Handle = hwnd, Title = title.ToString(), ProcessId = pid });
                return true;
            }, IntPtr.Zero);
            choices.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Title, b.Title));
            return choices;
        }

        private static bool IsEligibleWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd))
                return false;
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero || GetAncestor(hwnd, GA_ROOT) != hwnd)
                return false;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return pid != (uint)Process.GetCurrentProcess().Id;
        }

        private static IntPtr PickWindowByClick()
        {
            Point selectedPoint = Point.Empty;
            using (var overlay = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = SystemInformation.VirtualScreen,
                BackColor = Color.Black,
                Opacity = 0.20,
                TopMost = true,
                ShowInTaskbar = false,
                Cursor = Cursors.Cross,
                KeyPreview = true
            })
            {
                overlay.MouseDown += (sender, args) =>
                {
                    if (args.Button == MouseButtons.Right)
                    {
                        overlay.DialogResult = DialogResult.Cancel;
                        overlay.Close();
                    }
                    else if (args.Button == MouseButtons.Left)
                    {
                        selectedPoint = Cursor.Position;
                        overlay.DialogResult = DialogResult.OK;
                        overlay.Hide();
                        overlay.Close();
                    }
                };
                overlay.KeyDown += (sender, args) =>
                {
                    if (args.KeyCode == Keys.Escape)
                    {
                        overlay.DialogResult = DialogResult.Cancel;
                        overlay.Close();
                    }
                };
                if (overlay.ShowDialog() != DialogResult.OK)
                    return IntPtr.Zero;
            }

            // The overlay is disposed before testing the underlying window.
            Application.DoEvents();
            IntPtr selected = WindowFromPoint(new POINT { X = selectedPoint.X, Y = selectedPoint.Y });
            IntPtr root = GetAncestor(selected, GA_ROOT);
            return IsEligibleWindow(root) ? root : IntPtr.Zero;
        }

        private sealed class WindowChoice
        {
            public IntPtr Handle;
            public uint ProcessId;
            public string Title;
            public override string ToString() { return Title + " (PID " + ProcessId + ")"; }
        }
    }
}
