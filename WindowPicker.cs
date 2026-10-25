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
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private const uint GW_OWNER = 4;
        private const uint GA_ROOT = 2;

        internal static IntPtr SelectWindow()
        {
            var choices = GetWindows();
            Screen screen = Screen.FromPoint(Cursor.Position);
            float scale = DpiHelper.GetScreenDpiScale(screen);
            Func<int, int> px = value => DpiHelper.ScaleValue(value, scale);
            using (var dialog = new Form())
            {
                dialog.SuspendLayout();
                dialog.Text = "Select window - RScreenRec";
                dialog.AutoScaleMode = AutoScaleMode.None;
                dialog.Font = new Font("Segoe UI", 9F);
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.FormBorderStyle = FormBorderStyle.Sizable;
                dialog.MinimizeBox = false;
                dialog.TopMost = true;
                dialog.ClientSize = new Size(
                    Math.Min(px(680), screen.WorkingArea.Width - px(48)),
                    Math.Min(px(430), screen.WorkingArea.Height - px(64)));

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(px(12)),
                    ColumnCount = 1,
                    RowCount = 4
                };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var search = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, px(8)) };
                var list = new ListBox
                {
                    Dock = DockStyle.Fill,
                    IntegralHeight = false,
                    HorizontalScrollbar = false,
                    Margin = new Padding(0)
                };
                var note = new Label
                {
                    Text = "Restore minimized windows before selecting them.",
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    ForeColor = Color.DimGray,
                    Margin = new Padding(0, px(8), 0, px(8))
                };
                var buttons = new TableLayoutPanel
                {
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    ColumnCount = 4,
                    RowCount = 1,
                    Margin = new Padding(0)
                };
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var pick = new Button { Text = "Click a window...", AutoSize = true, Padding = new Padding(px(8), px(4), px(8), px(4)), Margin = new Padding(0) };
                var confirm = new Button { Text = "Select", AutoSize = true, MinimumSize = new Size(px(88), 0), Padding = new Padding(px(8), px(4), px(8), px(4)), Margin = new Padding(px(8), 0, 0, 0), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(px(88), 0), Padding = new Padding(px(8), px(4), px(8), px(4)), Margin = new Padding(px(8), 0, 0, 0), DialogResult = DialogResult.Cancel };
                buttons.Controls.Add(pick, 0, 0);
                buttons.Controls.Add(confirm, 2, 0);
                buttons.Controls.Add(cancel, 3, 0);
                layout.Controls.Add(search, 0, 0);
                layout.Controls.Add(list, 0, 1);
                layout.Controls.Add(note, 0, 2);
                layout.Controls.Add(buttons, 0, 3);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = confirm;
                dialog.CancelButton = cancel;

                Action filter = () =>
                {
                    string query = search.Text.Trim();
                    list.BeginUpdate();
                    try
                    {
                        list.Items.Clear();
                        foreach (WindowChoice choice in choices)
                            if (query.Length == 0 || choice.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                                list.Items.Add(choice);
                        if (list.Items.Count > 0) list.SelectedIndex = 0;
                    }
                    finally { list.EndUpdate(); }
                };
                search.TextChanged += (sender, args) => filter();
                filter();
                pick.Click += (sender, args) =>
                {
                    IntPtr selected = PickWindowByClick();
                    if (selected == IntPtr.Zero) { dialog.Activate(); return; }
                    dialog.Tag = selected;
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };
                list.DoubleClick += (sender, args) =>
                {
                    if (list.SelectedItem != null) dialog.DialogResult = DialogResult.OK;
                };
                dialog.ResumeLayout(true);
                dialog.Location = new Point(
                    screen.WorkingArea.Left + (screen.WorkingArea.Width - dialog.Width) / 2,
                    screen.WorkingArea.Top + (screen.WorkingArea.Height - dialog.Height) / 2);
                dialog.Shown += (sender, args) =>
                {
                    dialog.Activate();
                    dialog.BringToFront();
                    SetForegroundWindow(dialog.Handle);
                    search.Focus();
                    Logger.Log(string.Format("Picker layout: font={0}, row={1}, client={2}, list={3}, select={4}, cancel={5}",
                        list.Font.Height, list.ItemHeight, dialog.ClientSize, list.Bounds, confirm.Bounds, cancel.Bounds));
                };

                if (dialog.ShowDialog() != DialogResult.OK) return IntPtr.Zero;
                if (dialog.Tag is IntPtr)
                    return IsEligibleWindow((IntPtr)dialog.Tag) ? (IntPtr)dialog.Tag : IntPtr.Zero;
                WindowChoice selectedChoice = list.SelectedItem as WindowChoice;
                return selectedChoice != null && IsEligibleWindow(selectedChoice.Handle)
                    ? selectedChoice.Handle : IntPtr.Zero;
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
