using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace WindowLayers
{
    public enum Band { Top, Normal, Bottom }

    public static class Native
    {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int length);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] public static extern IntPtr GetLastActivePopup(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        public static readonly IntPtr Top = IntPtr.Zero, Bottom = new IntPtr(1), Topmost = new IntPtr(-1), NotTopmost = new IntPtr(-2);
        public const uint Flags = 0x0001 | 0x0002 | 0x0010 | 0x0200 | 0x4000;
        public static string Title(IntPtr h) { var b = new StringBuilder(1024); GetWindowText(h, b, b.Capacity); return b.ToString(); }
        public static string Class(IntPtr h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
        public static bool IsTop(IntPtr h) { return (GetWindowLong(h, -20) & 8) != 0; }
        public static List<IntPtr> Order()
        {
            var result = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr p) { result.Add(h); return true; }, IntPtr.Zero);
            return result;
        }
        public static bool Candidate(IntPtr h)
        {
            if (!IsWindow(h) || !IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero || Title(h).Length == 0) return false;
            var c = Class(h);
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
            if ((GetWindowLong(h, -20) & 0x80) != 0) return false;
            int cloaked;
            try { if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false; } catch (DllNotFoundException) { }
            return true;
        }
        public static WindowRef Read(IntPtr h)
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            string process = "PID " + pid; long start = 0;
            try { using (var p = Process.GetProcessById((int)pid)) { process = p.ProcessName; start = p.StartTime.ToUniversalTime().Ticks; } } catch { }
            return new WindowRef { Handle = h, Pid = pid, Started = start, ProcessName = process, WindowClass = Class(h), Caption = Title(h), Band = Band.Normal };
        }
    }

    public class WindowRef
    {
        public IntPtr Handle;
        public uint Pid;
        public long Started;
        public string ProcessName, WindowClass, Caption;
        public Band Band;
        public bool Valid
        {
            get
            {
                uint pid;
                if (!Native.IsWindow(Handle)) return false;
                Native.GetWindowThreadProcessId(Handle, out pid);
                if (pid != Pid || Native.Class(Handle) != WindowClass) return false;
                if (Started != 0)
                {
                    try { using (var p = Process.GetProcessById((int)pid)) { if (p.StartTime.ToUniversalTime().Ticks != Started) return false; } }
                    catch { return false; }
                }
                return true;
            }
        }
    }

    public class LayerEngine
    {
        public readonly List<WindowRef> Windows = new List<WindowRef>();
        readonly Dictionary<WindowRef, bool> originals = new Dictionary<WindowRef, bool>();
        public bool Running { get; private set; }
        public string Notice = "";
        public IntPtr Controller;
        public bool PreserveControllerTopmost = true;
        public int Changes;
        public void Start()
        {
            if (Running) return;
            Running = true;
            Capture();
            Apply();
        }
        void Capture()
        {
            foreach (var w in Windows.Where(w => w.Valid))
                if (!originals.ContainsKey(w)) originals.Add(w, Native.IsTop(w.Handle));
        }
        public void Stop()
        {
            Running = false;
            int failures = 0;
            foreach (var item in originals.ToArray())
            {
                if (!item.Key.Valid || Native.IsTop(item.Key.Handle) == item.Value) { originals.Remove(item.Key); continue; }
                if (Native.SetWindowPos(item.Key.Handle, item.Value ? Native.Topmost : Native.NotTopmost, 0, 0, 0, 0, Native.Flags)) originals.Remove(item.Key);
                else failures++;
            }
            Notice = failures == 0 ? "" : "有 " + failures + " 个窗口未能恢复，请以相同权限运行后重试。";
        }
        public void Remove(WindowRef window)
        {
            bool original;
            if (originals.TryGetValue(window, out original))
            {
                if (window.Valid && Native.IsTop(window.Handle) != original && !Native.SetWindowPos(window.Handle, original ? Native.Topmost : Native.NotTopmost, 0, 0, 0, 0, Native.Flags))
                    throw new InvalidOperationException("无法恢复这个窗口的置顶状态，请先暂停并检查运行权限。");
                originals.Remove(window);
            }
            Windows.Remove(window);
        }
        bool Place(WindowRef window, IntPtr after)
        {
            Changes++;
            if (Native.SetWindowPos(window.Handle, after, 0, 0, 0, 0, Native.Flags))
            {
                if (PreserveControllerTopmost && Controller != IntPtr.Zero && Native.IsWindowVisible(Controller))
                    Native.SetWindowPos(Controller, Native.Topmost, 0, 0, 0, 0, Native.Flags);
                return true;
            }
            Notice = "无法调整：" + window.Caption + "（系统错误 " + Marshal.GetLastWin32Error() + "）。可能需要与目标应用相同的权限。";
            return false;
        }
        static bool Ordered(List<WindowRef> windows, List<IntPtr> order)
        {
            int prev = -1;
            foreach (var w in windows) { int index = order.IndexOf(w.Handle); if (index < 0 || index <= prev) return false; prev = index; }
            return true;
        }
        bool RepairPair(List<WindowRef> windows, List<IntPtr> order)
        {
            for (int i = 1; i < windows.Count; i++)
            {
                if (order.IndexOf(windows[i - 1].Handle) > order.IndexOf(windows[i].Handle))
                { Place(windows[i], windows[i - 1].Handle); return true; }
            }
            return false;
        }
        public void Apply()
        {
            if (!Running) return;
            Notice = "";
            Capture();
            var active = Windows.Where(w => w.Valid && Native.IsWindowVisible(w.Handle) && !Native.IsIconic(w.Handle)).ToList();
            var order = Native.Order();
            // WPS/Electron and many native programs keep modeless owned helper windows
            // alive. Ownership is not a modal signal; a disabled parent is.
            var blocked = active.Where(w => !Native.IsWindowEnabled(w.Handle)).ToList();
            if (blocked.Count > 0) Notice = "等待弹窗关闭：" + blocked[0].Caption + "；其他窗口继续排序。";
            active = active.Except(blocked).ToList();
            foreach (var w in active)
            {
                bool desired = w.Band == Band.Top;
                // Cross-process asynchronous requests must settle before the next dependent move.
                if (Native.IsTop(w.Handle) != desired) { Place(w, desired ? Native.Topmost : Native.NotTopmost); return; }
            }
            var top = active.Where(w => w.Band == Band.Top).ToList();
            var normal = active.Where(w => w.Band == Band.Normal).ToList();
            var bottom = active.Where(w => w.Band == Band.Bottom).ToList();
            if (RepairPair(top, order) || RepairPair(normal, order) || RepairPair(bottom, order)) return;
            if (bottom.Count > 0)
            {
                var handles = new HashSet<IntPtr>(bottom.Select(w => w.Handle));
                int firstBottom = order.FindIndex(h => handles.Contains(h));
                if (firstBottom >= 0)
                {
                    var lastOutside = order.Skip(firstBottom + 1).LastOrDefault(h => h != Controller && !handles.Contains(h) && Native.Candidate(h) && !Native.IsIconic(h) && !Native.IsTop(h));
                    if (lastOutside != IntPtr.Zero) Place(bottom[0], lastOutside);
                }
            }
        }
    }

    [Serializable] public class SavedWindow
    {
        public string ProcessName, WindowClass, Caption;
        public Band Band;
    }
    [Serializable] public class Profile
    {
        public string Name;
        public List<SavedWindow> Windows = new List<SavedWindow>();
        public override string ToString() { return Name; }
    }
    [Serializable] public class ProfileStore
    {
        public string LastUsedName;
        public List<Profile> Profiles = new List<Profile>();
        public static ProfileStore Load(string path)
        {
            if (!File.Exists(path)) return new ProfileStore();
            using (var r = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null }))
                return (ProfileStore)new XmlSerializer(typeof(ProfileStore)).Deserialize(r);
        }
        public void Save(string path)
        {
            string temp = path + ".tmp";
            try
            {
                using (var s = File.Create(temp)) new XmlSerializer(typeof(ProfileStore)).Serialize(s, this);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static List<WindowRef> Match(Profile profile, List<WindowRef> candidates, out int missing)
        {
            missing = 0;
            var result = new List<WindowRef>();
            var used = new HashSet<IntPtr>();
            foreach (var saved in profile.Windows)
            {
                var matches = candidates.Where(w => !used.Contains(w.Handle) && w.ProcessName == saved.ProcessName && w.WindowClass == saved.WindowClass && w.Caption == saved.Caption).ToList();
                if (matches.Count != 1) { missing++; continue; }
                var match = matches[0]; match.Band = saved.Band;
                if (!Enum.IsDefined(typeof(Band), match.Band)) match.Band = Band.Normal;
                used.Add(match.Handle); result.Add(match);
            }
            return result;
        }
    }

    public class MainForm : Form
    {
        readonly LayerEngine engine = new LayerEngine();
        readonly ListView available = new ListView(), managed = new ListView();
        readonly TextBox filter = new TextBox(), profileName = new TextBox();
        readonly ComboBox profiles = new ComboBox();
        readonly Button toggle = new Button();
        readonly Label status = new Label(), count = new Label();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        ProfileStore store = new ProfileStore();
        readonly string profilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.xml");
        List<WindowRef> candidates = new List<WindowRef>();
        bool exiting, hotkey;
        int ticks;
        static readonly Color Ink = Color.FromArgb(28, 40, 59), Muted = Color.FromArgb(89, 104, 124), Accent = Color.FromArgb(0, 113, 101);

        public MainForm()
        {
            Text = "叠窗 · Window Layers";
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(244, 247, 250); ForeColor = Ink;
            ClientSize = new Size(1140, 750); MinimumSize = new Size(1020, 720);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Icon = SystemIcons.Application; TopMost = true;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22, 16, 22, 14) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            var titles = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            titles.Controls.Add(new Label { Text = "叠窗  /  WINDOW LAYERS", Font = new Font(Font.FontFamily, 21F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 5) });
            titles.Controls.Add(new Label { Text = "让每个窗口，待在你指定的位置。", ForeColor = Muted, AutoSize = true });
            header.Controls.Add(titles, 0, 0);
            toggle.Text = "开始锁定"; toggle.Dock = DockStyle.Fill; toggle.Margin = new Padding(0, 5, 0, 18);
            StyleButton(toggle, true); toggle.Click += delegate { ToggleLock(); };
            header.Controls.Add(toggle, 1, 0); root.Controls.Add(header, 0, 0);

            var intro = new Label { Dock = DockStyle.Fill, Text = "① 添加窗口    ② 调整顺序和层级    ③ 开始锁定\n同一层级内，上面的窗口显示在前面。Ctrl + Alt + F10 随时暂停 / 继续。", ForeColor = Muted, Padding = new Padding(0, 7, 0, 0) };
            root.Controls.Add(intro, 0, 1);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            var left = Section("已打开的窗口", new Padding(0, 0, 12, 0));
            var right = Section("展示顺序 · 从前到后", new Padding(12, 0, 0, 0));
            body.Controls.Add(left, 0, 0); body.Controls.Add(right, 1, 0); root.Controls.Add(body, 0, 2);
            var search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            search.Controls.Add(new Label { Text = "筛选", AutoSize = true, Margin = new Padding(0, 7, 0, 0) }, 0, 0);
            filter.Dock = DockStyle.Fill; filter.Margin = new Padding(0, 2, 0, 4); filter.TextChanged += delegate { ShowAvailable(); }; search.Controls.Add(filter, 1, 0);
            left.Controls.Add(search, 0, 1);
            ConfigureList(available); available.Columns.Add("窗口标题", 272); available.Columns.Add("应用", 123); available.MultiSelect = true;
            available.DoubleClick += delegate { AddSelected(); }; left.Controls.Add(available, 0, 2);
            var leftActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
            leftActions.Controls.Add(ActionButton("刷新列表", delegate { RefreshWindows(); }));
            leftActions.Controls.Add(ActionButton("添加选中 →", delegate { AddSelected(); })); left.Controls.Add(leftActions, 0, 3);

            count.Text = "尚未添加窗口"; count.ForeColor = Muted; count.Dock = DockStyle.Fill; count.Padding = new Padding(0, 7, 0, 0); right.Controls.Add(count, 0, 1);
            ConfigureList(managed); managed.MultiSelect = false;
            managed.Columns.Add("顺序", 48); managed.Columns.Add("窗口标题", 280); managed.Columns.Add("层级", 66); managed.Columns.Add("状态", 80);
            managed.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            right.Controls.Add(managed, 0, 2);
            var rightActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), WrapContents = true };
            rightActions.Controls.Add(ActionButton("↑ 上移", delegate { MoveSelected(-1); }, 78));
            rightActions.Controls.Add(ActionButton("↓ 下移", delegate { MoveSelected(1); }, 78));
            rightActions.Controls.Add(ActionButton("置顶", delegate { SetBand(Band.Top); }, 65));
            rightActions.Controls.Add(ActionButton("普通", delegate { SetBand(Band.Normal); }, 65));
            rightActions.Controls.Add(ActionButton("置底", delegate { SetBand(Band.Bottom); }, 65));
            rightActions.Controls.Add(ActionButton("移除", delegate { RemoveSelected(); }, 65)); right.Controls.Add(rightActions, 0, 3);

            var profileRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Padding = new Padding(0, 14, 0, 0) };
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            profiles.DropDownStyle = ComboBoxStyle.DropDownList; profiles.Dock = DockStyle.Top; profiles.Margin = new Padding(0, 3, 12, 0);
            profiles.SelectedIndexChanged += delegate { if (profiles.SelectedItem != null) profileName.Text = ((Profile)profiles.SelectedItem).Name; };
            profileRow.Controls.Add(profiles, 0, 0); profileName.Dock = DockStyle.Top; profileName.Margin = new Padding(0, 3, 12, 0); profileName.Text = "我的窗口顺序";
            profileRow.Controls.Add(profileName, 1, 0); profileRow.Controls.Add(ActionButton("保存方案", delegate { SaveProfile(); }, 94), 2, 0);
            profileRow.Controls.Add(ActionButton("加载方案", delegate { LoadProfile(); }, 94), 3, 0);
            profileRow.Controls.Add(ActionButton("删除方案", delegate { DeleteProfile(); }, 94), 4, 0); root.Controls.Add(profileRow, 0, 3);

            status.Dock = DockStyle.Fill; status.ForeColor = Muted; status.Text = "已暂停 · 仅管理你添加的窗口；加载方案后需手动开始锁定。\n关闭主界面会收到托盘，右键托盘可彻底退出。";
            root.Controls.Add(status, 0, 4);
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开控制面板", null, delegate { ShowPanel(); });
            menu.Items.Add("暂停 / 继续锁定", null, delegate { ToggleLock(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出并恢复窗口", null, delegate { exiting = true; Close(); });
            tray.Icon = SystemIcons.Application; tray.Text = "叠窗 · 已暂停"; tray.ContextMenuStrip = menu; tray.Visible = true; tray.DoubleClick += delegate { ShowPanel(); };
            timer.Interval = 250;
            timer.Tick += delegate
            {
                try { engine.Apply(); if (++ticks % 4 == 0) { UpdateRows(); UpdateStatus(); } }
                catch (Exception ex) { engine.Stop(); UpdateStatus(); status.Text = "已暂停：" + ex.Message; }
            };
            Shown += delegate
            {
                engine.Controller = Handle;
                hotkey = Native.RegisterHotKey(Handle, 1, 0x4000 | 0x0002 | 0x0001, (uint)Keys.F10);
                RefreshWindows(); timer.Start();
                if (!hotkey) status.Text = "Ctrl + Alt + F10 已被占用，请使用界面或托盘暂停。";
            };
            FormClosing += OnClosing;
            try { store = ProfileStore.Load(profilePath); } catch (Exception ex) { status.Text = "方案读取失败，原文件保留：" + ex.Message; }
            RefreshProfiles(null);
        }
        TableLayoutPanel Section(string title, Padding margin)
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = margin };
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); p.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); p.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            p.Controls.Add(new Label { Text = title, Font = new Font(Font.FontFamily, 12F, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
            return p;
        }
        static void ConfigureList(ListView list)
        {
            list.Dock = DockStyle.Fill; list.View = View.Details; list.FullRowSelect = true; list.HideSelection = false;
            list.BorderStyle = BorderStyle.FixedSingle; list.BackColor = Color.White; list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        }
        static void StyleButton(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(207, 216, 226);
            b.BackColor = primary ? Accent : Color.White; b.ForeColor = primary ? Color.White : Ink; b.Cursor = Cursors.Hand;
        }
        Button ActionButton(string text, Action action, int width = 112)
        {
            var b = new Button { Text = text, Width = width, Height = 34, Margin = new Padding(0, 0, 7, 0) };
            StyleButton(b, false); b.Click += delegate { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "叠窗", MessageBoxButtons.OK, MessageBoxIcon.Information); } }; return b;
        }
        public void RefreshWindows()
        {
            uint self = (uint)Process.GetCurrentProcess().Id;
            candidates = Native.Order().Where(Native.Candidate).Select(Native.Read).Where(w => w.Pid != self).ToList();
            ShowAvailable(); UpdateRows();
        }
        void ShowAvailable()
        {
            var selected = new HashSet<IntPtr>(available.SelectedItems.Cast<ListViewItem>().Select(i => ((WindowRef)i.Tag).Handle));
            available.BeginUpdate(); available.Items.Clear();
            foreach (var w in candidates.Where(w => !engine.Windows.Any(m => m.Handle == w.Handle) && (w.Caption + " " + w.ProcessName).IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) >= 0))
            {
                var row = new ListViewItem(new[] { w.Caption, w.ProcessName }) { Tag = w, Selected = selected.Contains(w.Handle) }; available.Items.Add(row);
            }
            available.EndUpdate();
        }
        public void AddSelected()
        {
            foreach (ListViewItem row in available.SelectedItems)
            {
                var w = (WindowRef)row.Tag;
                if (w.Valid && !engine.Windows.Any(m => m.Handle == w.Handle)) engine.Windows.Add(w);
            }
            SortBands(); UpdateRows(); ShowAvailable(); engine.Apply(); UpdateStatus();
        }
        WindowRef Selected() { return managed.SelectedItems.Count == 0 ? null : (WindowRef)managed.SelectedItems[0].Tag; }
        void SortBands()
        {
            var sorted = engine.Windows.OrderBy(w => (int)w.Band).ToList(); engine.Windows.Clear(); engine.Windows.AddRange(sorted);
        }
        void UpdateRows()
        {
            var selected = Selected(); managed.BeginUpdate();
            bool rebuild = managed.Items.Count != engine.Windows.Count || managed.Items.Cast<ListViewItem>().Select(i => i.Tag).Where((tag, i) => !object.ReferenceEquals(tag, engine.Windows[i])).Any();
            if (rebuild)
            {
                managed.Items.Clear();
                foreach (var w in engine.Windows) managed.Items.Add(new ListViewItem(new[] { "", "", "", "" }) { Tag = w });
            }
            for (int i = 0; i < engine.Windows.Count; i++)
            {
                var w = engine.Windows[i]; var row = managed.Items[i]; bool valid = w.Valid;
                if (valid) w.Caption = Native.Title(w.Handle);
                row.SubItems[0].Text = (i + 1).ToString("00"); row.SubItems[1].Text = w.Caption;
                row.SubItems[2].Text = BandName(w.Band); row.SubItems[3].Text = !valid ? "已关闭" : Native.IsIconic(w.Handle) ? "已最小化" : engine.Running ? "锁定中" : "已暂停";
                row.ForeColor = valid ? Ink : Muted; if (w == selected) row.Selected = true;
            }
            managed.EndUpdate(); count.Text = engine.Windows.Count + " 个窗口  ·  置顶 → 普通 → 置底";
        }
        static string BandName(Band b) { return b == Band.Top ? "置顶" : b == Band.Bottom ? "置底" : "普通"; }
        void MoveSelected(int direction)
        {
            var w = Selected(); if (w == null) return; int i = engine.Windows.IndexOf(w), j = i + direction;
            if (j < 0 || j >= engine.Windows.Count) return;
            if (engine.Windows[j].Band != w.Band) { status.Text = "跨层移动请先点击“置顶 / 普通 / 置底”，再调整同层顺序。"; return; }
            engine.Windows.RemoveAt(i); engine.Windows.Insert(j, w); UpdateRows(); managed.Items[j].Selected = true; managed.Items[j].EnsureVisible(); engine.Apply();
        }
        void SetBand(Band b)
        {
            var w = Selected(); if (w == null) return; w.Band = b; SortBands(); UpdateRows(); managed.Items[engine.Windows.IndexOf(w)].Selected = true; engine.Apply();
        }
        void RemoveSelected()
        {
            var w = Selected(); if (w == null) return; engine.Remove(w); UpdateRows(); RefreshWindows(); UpdateStatus();
        }
        public void ToggleLock()
        {
            if (!engine.Running && !engine.Windows.Any(w => w.Valid)) { status.Text = "请先从左侧添加至少一个有效窗口。"; return; }
            if (engine.Running) engine.Stop(); else engine.Start(); UpdateRows(); UpdateStatus();
        }
        void UpdateStatus()
        {
            toggle.Text = engine.Running ? "暂停锁定" : "开始锁定";
            tray.Text = engine.Running ? "叠窗 · 正在锁定" : "叠窗 · 已暂停";
            status.Text = (engine.Running ? "● 正在保持顺序 · 最小化的窗口会跳过，不会被自动还原。" : "○ 已暂停 · 窗口恢复正常切换，可继续调整顺序。") + "\n" +
                (engine.Notice.Length > 0 ? engine.Notice : !hotkey ? "暂停快捷键不可用，请使用界面或托盘。" : "Ctrl + Alt + F10：暂停 / 继续。控制面板保持置顶，方便随时调整。") ;
        }
        void ShowPanel() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        void RefreshProfiles(string name)
        {
            profiles.Items.Clear(); foreach (var p in store.Profiles) profiles.Items.Add(p);
            for (int i = 0; i < profiles.Items.Count; i++) if (((Profile)profiles.Items[i]).Name == name) profiles.SelectedIndex = i;
            if (profiles.SelectedIndex < 0 && profiles.Items.Count > 0) profiles.SelectedIndex = 0;
        }
        void SaveProfile()
        {
            string name = profileName.Text.Trim(); if (name.Length == 0) throw new InvalidOperationException("请给方案起一个名字。");
            if (name.Length > 80) throw new InvalidOperationException("方案名称请控制在 80 字以内。");
            if (!engine.Windows.Any(w => w.Valid)) throw new InvalidOperationException("请先添加有效窗口，再保存方案。");
            var old = store.Profiles.FirstOrDefault(p => p.Name == name);
            if (old != null && MessageBox.Show(this, "覆盖方案“" + name + "”？", "保存方案", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var fresh = new Profile { Name = name };
            foreach (var w in engine.Windows.Where(w => w.Valid)) fresh.Windows.Add(new SavedWindow { ProcessName = w.ProcessName, WindowClass = w.WindowClass, Caption = Native.Title(w.Handle), Band = w.Band });
            var next = new ProfileStore { Profiles = store.Profiles.Where(p => p.Name != name).ToList() }; next.Profiles.Add(fresh); next.Save(profilePath); store = next;
            RefreshProfiles(name); status.Text = "已保存方案：" + name + "。下次加载会按应用、窗口类和完整标题匹配。";
        }
        void LoadProfile()
        {
            var profile = profiles.SelectedItem as Profile; if (profile == null) return;
            engine.Stop();
            if (engine.Notice.Length > 0) throw new InvalidOperationException(engine.Notice);
            RefreshWindows(); int missing; var matches = ProfileStore.Match(profile, candidates, out missing);
            engine.Windows.Clear(); engine.Windows.AddRange(matches); SortBands(); UpdateRows(); ShowAvailable(); UpdateStatus();
            status.Text = "已加载“" + profile.Name + "”：匹配 " + matches.Count + " 个；未匹配或有歧义 " + missing + " 个。\n当前已暂停，请确认顺序后点击“开始锁定”。标题变化的窗口需要重新添加。";
        }
        void DeleteProfile()
        {
            var p = profiles.SelectedItem as Profile; if (p == null) return;
            if (MessageBox.Show(this, "删除方案“" + p.Name + "”？", "删除方案", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var next = new ProfileStore { Profiles = store.Profiles.Where(x => x != p).ToList() }; next.Save(profilePath); store = next; RefreshProfiles(null);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) ToggleLock();
            base.WndProc(ref m);
        }
        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; Hide(); tray.ShowBalloonTip(2500, "叠窗仍在运行", "双击托盘图标打开；右键托盘可退出并恢复窗口。", ToolTipIcon.Info); return;
            }
            timer.Stop(); engine.Stop();
            if (engine.Notice.Length > 0 && e.CloseReason == CloseReason.UserClosing)
            { e.Cancel = true; exiting = false; timer.Start(); ShowPanel(); status.Text = engine.Notice; return; }
            if (hotkey) Native.UnregisterHotKey(Handle, 1); tray.Visible = false; tray.Dispose(); timer.Dispose();
        }
        public void Preview(string path)
        {
            Show(); Application.DoEvents();
            using (var bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(Point.Empty, Size)); bmp.Save(path); }
            exiting = true; Close();
        }
    }

    static class Program
    {
        [STAThread] static void Main(string[] args)
        {
            Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 1 && args[0] == "--close-existing")
            {
                string expected = Path.GetFullPath(args[1]);
                foreach (var p in Process.GetProcessesByName("WindowLayers"))
                {
                    using (p)
                    {
                        try
                        {
                            if (p.Id == Process.GetCurrentProcess().Id || !string.Equals(p.MainModule.FileName, expected, StringComparison.OrdinalIgnoreCase)) continue;
                            foreach (var h in Native.Order())
                            {
                                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                                if (pid == p.Id && Native.Title(h) == "叠窗 · 图标栏") Native.PostMessage(h, 0x8001, IntPtr.Zero, IntPtr.Zero);
                            }
                        }
                        catch (System.ComponentModel.Win32Exception) { }
                    }
                }
                return;
            }
            if (args.Length > 1 && args[0] == "--probe") { SelfTests.RunProbe(args[1], args.Length > 2 && args[2] == "top"); return; }
            if (args.Length > 0 && args[0] == "--self-test") { Environment.Exit(SelfTests.Run(args.Length > 1 ? args[1] : "test-results.txt")); return; }
            if (args.Length > 1 && args[0] == "--preview") { using (var form = new CompactForm(null, true)) form.Preview(args[1], args.Length > 2 ? int.Parse(args[2]) : 0); return; }
            bool created;
            using (var mutex = new Mutex(true, "Local\\WindowLayers.Desktop.2026", out created))
            {
                if (!created) { MessageBox.Show("叠窗已经在运行，请双击系统托盘图标打开。", "叠窗"); return; }
                Application.Run(new CompactForm());
            }
        }
    }
}
