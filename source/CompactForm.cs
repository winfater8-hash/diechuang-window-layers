using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowLayers
{
    public static class Surface
    {
        public static System.Drawing.Drawing2D.GraphicsPath Round(Rectangle rect, int radius)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath(); int d = radius * 2;
            p.AddArc(rect.X, rect.Y, d, d, 180, 90); p.AddArc(rect.Right-d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right-d, rect.Bottom-d, d, d, 0, 90); p.AddArc(rect.X, rect.Bottom-d, d, d, 90, 90); p.CloseFigure(); return p;
        }
    }
    public class SoftButton : Button
    {
        bool hover, pressed;
        public string PaintStamp {get{return Text+hover+pressed+Focused;}}
        public void PaintSmooth(Graphics g,Point origin)
        {
            var state=g.Save();g.TranslateTransform(origin.X,origin.Y);
            using(var path=new System.Drawing.Drawing2D.GraphicsPath())
            using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
            {
                path.AddString(Text,Font.FontFamily,(int)Font.Style,Font.SizeInPoints*g.DpiY/72F,new RectangleF(0,0,Width,Height),format);
                using(var pen=new Pen(Color.FromArgb(245,0,0,0),2.4F) {LineJoin=System.Drawing.Drawing2D.LineJoin.Round}) g.DrawPath(pen,path);
                using(var brush=new SolidBrush(pressed ? Color.FromArgb(215,230,235) : Color.White)) g.FillPath(brush,path);
            }
            g.Restore(state);
        }
        public SoftButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MouseEnter += delegate { hover=true; Invalidate(); }; MouseLeave += delegate { hover=false; pressed=false; Invalidate(); };
            MouseDown += delegate { pressed=true; Invalidate(); }; MouseUp += delegate { pressed=false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using(var surface=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
            using(var g=Graphics.FromImage(surface))
            {
            g.Clear(Color.Transparent); g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var color=pressed ? ControlPaint.Dark(BackColor, .08F) : hover ? ControlPaint.Light(BackColor, .12F) : BackColor;
            using(var path=Surface.Round(new Rectangle(0,0,Width-1,Height-1),7))
            using(var brush=new SolidBrush(color)) { g.FillPath(brush,path); }
            TextRenderer.DrawText(g,Text,Font,ClientRectangle,Enabled ? ForeColor : SystemColors.GrayText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            if(Focused) using(var pen=new Pen(Color.FromArgb(83,166,150))) using(var path=Surface.Round(new Rectangle(2,2,Width-5,Height-5),5)) g.DrawPath(pen,path);
            }
            using(var keyed=WindowIcons.ColorKeyImage(surface,Width,Height,CompactForm.ClearColor)) e.Graphics.DrawImageUnscaled(keyed,0,0);
            }
        }
    }
    public class ClearGrip : Control
    {
        public ClearGrip(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=CompactForm.ClearColor;}
        protected override void OnPaint(PaintEventArgs e)
        {
            // Fill only the small bounding box around the visible dot grid, including
            // the gaps between dots. The rest remains click-through.
            e.Graphics.Clear(CompactForm.ClearColor);
            using(var hitBrush=new SolidBrush(Color.White)) e.Graphics.FillRectangle(hitBrush,(Width-9)/2-2,5,13,19);
            using(var brush=new SolidBrush(Color.FromArgb(100,112,125)))
            for(int y=7;y<=19;y+=6) for(int x=8;x<=14;x+=6) e.Graphics.FillRectangle(brush,x,y,3,3);
        }
    }
    public static class WindowIcons
    {
        public static Bitmap ColorKeyImage(Bitmap source, int width, int height, Color key)
        {
            // Never blend fractional alpha against the magenta color key: that creates
            // non-key purple pixels which Windows treats as opaque. Resolve coverage
            // on a transparent surface first and keep the source RGB for visible pixels.
            using (var rgba = new Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(rgba))
                {
                    g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    if(source.Width==width && source.Height==height) g.DrawImageUnscaled(source,0,0);
                    else using(var attributes=new System.Drawing.Imaging.ImageAttributes())
                    {
                        attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                        g.DrawImage(source,new Rectangle(0,0,width,height),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
                    }
                }
                var result=new Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                {
                    var c=rgba.GetPixel(x,y);
                    result.SetPixel(x,y,c.A>=96 ? Color.FromArgb(255,c.R,c.G,c.B) : key);
                }
                return result;
            }
        }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr ClassIcon64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetClassLongW")] static extern uint ClassIcon32(IntPtr hwnd, int index);
        public static Icon Read(WindowRef w)
        {
            try
            {
                IntPtr h;
                SendMessageTimeout(w.Handle, 0x007F, new IntPtr(1), IntPtr.Zero, 2, 60, out h);
                if (h == IntPtr.Zero) SendMessageTimeout(w.Handle, 0x007F, IntPtr.Zero, IntPtr.Zero, 2, 60, out h);
                if (h == IntPtr.Zero) h = IntPtr.Size == 8 ? ClassIcon64(w.Handle, -14) : new IntPtr(unchecked((int)ClassIcon32(w.Handle, -14)));
                if (h != IntPtr.Zero) return (Icon)Icon.FromHandle(h).Clone();
                using (var p = Process.GetProcessById((int)w.Pid))
                {
                    string path = p.MainModule.FileName;
                    var icon = Icon.ExtractAssociatedIcon(path); if (icon != null) return icon;
                }
            }
            catch { }
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    public class IconTile : Control
    {
        public readonly WindowRef Window;
        public readonly IconStrip Strip;
        public int DuplicateNumber;
        readonly Icon icon;
        readonly Bitmap displayIcon;
        readonly Bitmap smoothIcon;
        Point down;
        bool pressing;
        public IconTile(WindowRef window, IconStrip strip)
        {
            Window = window; Strip = strip; icon = WindowIcons.Read(window);
            smoothIcon=icon.ToBitmap();
            using(var source=icon.ToBitmap()) displayIcon=WindowIcons.ColorKeyImage(source,28,28,CompactForm.ClearColor);
            Size = new Size(42, 36); Margin = new Padding(2, 2, 2, 2); TabStop = true; Cursor = Cursors.Hand;
            AllowDrop = true; AccessibleRole = AccessibleRole.PushButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            MouseEnter += delegate { UpdateTitle(); Invalidate(); };
            MouseLeave += delegate { pressing = false; Invalidate(); };
            MouseDown += delegate(object s, MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) { down = e.Location; pressing = true; } if (e.Button == MouseButtons.Right) strip.OwnerBar.ShowIconMenu(this); };
            MouseUp += delegate { pressing = false; };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!pressing || e.Button != MouseButtons.Left) return;
                if (Math.Abs(e.X - down.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - down.Y) < SystemInformation.DragSize.Height / 2) return;
                pressing = false;
                Strip.OwnerBar.Dragging = true; Strip.OwnerBar.SetDragSurface(true);
                try { var payload = new DataObject(); payload.SetData(typeof(WindowRef), Window); DoDragDrop(payload, DragDropEffects.Move); }
                finally { Strip.OwnerBar.Dragging = false; Strip.OwnerBar.ClearInsertMarkers(); Strip.OwnerBar.SetDragSurface(false); }
            };
            DoubleClick += delegate { strip.OwnerBar.Transfer(Window, !strip.Managed, strip.Managed ? -1 : strip.OwnerBar.Engine.Windows.Count); };
            DragEnter += delegate(object s, DragEventArgs e) { strip.DragOverAt(e); };
            DragOver += delegate(object s, DragEventArgs e) { strip.DragOverAt(e); };
            DragDrop += delegate(object s, DragEventArgs e) { strip.DropAt(e); };
            GotFocus += delegate { Invalidate(); };
            LostFocus += delegate { Invalidate(); };
            UpdateTitle();
        }
        public void UpdateTitle()
        {
            if (Window.Valid) Window.Caption = Native.Title(Window.Handle);
            string mode = Strip.Managed ? CompactForm.BandName(Window.Band) : "不锁定";
            AccessibleName = Window.Caption + " · " + mode;
            string caption = Window.Caption + "\n" + Window.ProcessName + " · " + mode +
                (Strip.Managed ? "\n左侧在前，右侧在后；拖到下排解除管理。" : "\n拖到上排加入排序。") + "\n右键设置；双击在上下排之间切换。";
            Strip.OwnerBar.Tips.SetToolTip(this, caption);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.DrawImageUnscaled(displayIcon,7,2);
            if(Focused) using(var pen=new Pen(Color.FromArgb(80,150,140))) g.DrawRectangle(pen,2,0,Width-5,Height-2);
        }
        public void PaintSmooth(Graphics g,Point origin)
        {
            using(var attributes=new System.Drawing.Imaging.ImageAttributes())
            {
                attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                g.DrawImage(smoothIcon,new Rectangle(origin.X+7,origin.Y+2,28,28),0,0,smoothIcon.Width,smoothIcon.Height,GraphicsUnit.Pixel,attributes);
            }
            if(Focused) using(var pen=new Pen(Color.FromArgb(180,100,170,150))) using(var path=Surface.Round(new Rectangle(origin.X+2,origin.Y,Width-5,Height-2),4))g.DrawPath(pen,path);
        }
        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.Control) != 0 || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)) { Strip.OwnerBar.Transfer(Window, e.KeyCode == Keys.Up, -1); e.Handled = true; }
            else if (e.Control && Strip.Managed && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
            {
                int index = Strip.OwnerBar.Engine.Windows.IndexOf(Window); int to = index + (e.KeyCode == Keys.Left ? -1 : 2);
                Strip.OwnerBar.Transfer(Window, true, to); e.Handled = true;
            }
            else if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10)) { Strip.OwnerBar.ShowIconMenu(this); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) { Strip.OwnerBar.Tips.SetToolTip(this, null); smoothIcon.Dispose();displayIcon.Dispose(); icon.Dispose(); } base.Dispose(disposing); }
    }

    public class IconStrip : FlowLayoutPanel
    {
        public readonly CompactForm OwnerBar;
        public readonly bool Managed;
        int insert = -1;
        public IconStrip(CompactForm owner, bool managed)
        {
            OwnerBar = owner; Managed = managed; Dock = DockStyle.Fill; AutoScroll = true; WrapContents = false;
            FlowDirection = FlowDirection.LeftToRight; Padding = new Padding(7, 2, 7, 0); Margin = new Padding(0, 2, 0, 2);
            BackColor = CompactForm.ClearColor;
            AllowDrop = true; SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            DragEnter += delegate(object s, DragEventArgs e) { DragOverAt(e); };
            DragOver += delegate(object s, DragEventArgs e) { DragOverAt(e); };
            DragLeave += delegate { ClearMarker(); };
            DragDrop += delegate(object s, DragEventArgs e) { DropAt(e); };
        }
        public List<WindowRef> Items { get { return Controls.OfType<IconTile>().Select(c => c.Window).ToList(); } }
        public void Render(List<WindowRef> windows)
        {
            if (Items.Select(w => w.Handle).SequenceEqual(windows.Select(w => w.Handle)))
            {
                foreach (var tile in Controls.OfType<IconTile>()) { tile.UpdateTitle(); tile.Invalidate(); } return;
            }
            var focused = Controls.OfType<IconTile>().FirstOrDefault(c => c.Focused);
            IntPtr focus = focused == null ? IntPtr.Zero : focused.Window.Handle;
            Point scroll = AutoScrollPosition;
            SuspendLayout(); foreach (Control c in Controls.Cast<Control>().ToArray()) c.Dispose(); Controls.Clear();
            foreach (var w in windows) Controls.Add(new IconTile(w, this));
            var duplicates = windows.GroupBy(w => w.ProcessName).Where(g => g.Count() > 1);
            foreach (var group in duplicates)
            {
                int number = 0; foreach (var tile in Controls.OfType<IconTile>().Where(c => c.Window.ProcessName == group.Key)) tile.DuplicateNumber = ++number;
            }
            ResumeLayout(true); AutoScrollPosition = new Point(-scroll.X, 0);
            var next = Controls.OfType<IconTile>().FirstOrDefault(c => c.Window.Handle == focus); if (next != null) next.Focus();
            Invalidate();
        }
        public int DropIndex(int screenX, int screenY)
        {
            Point point = PointToClient(new Point(screenX, screenY)); int index = 0;
            foreach (var tile in Controls.OfType<IconTile>()) { if (point.X < tile.Left + tile.Width / 2) return index; index++; }
            return index;
        }
        public void DragOverAt(DragEventArgs e)
        {
            var w = e.Data.GetData(typeof(WindowRef)) as WindowRef;
            if (w == null || !w.Valid) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Move; insert = DropIndex(e.X, e.Y);
            var point = PointToClient(new Point(e.X, e.Y));
            if (point.X < 18 && HorizontalScroll.Visible) AutoScrollPosition = new Point(Math.Max(0, -AutoScrollPosition.X - 12), 0);
            else if (point.X > ClientSize.Width - 18 && HorizontalScroll.Visible) AutoScrollPosition = new Point(-AutoScrollPosition.X + 12, 0);
            Invalidate();
        }
        public void DropAt(DragEventArgs e)
        {
            var w = e.Data.GetData(typeof(WindowRef)) as WindowRef; int index = DropIndex(e.X, e.Y); ClearMarker();
            if (w == null || !w.Valid) return;
            OwnerBar.Transfer(w, Managed, index); e.Effect = DragDropEffects.Move;
        }
        public void ClearMarker() { insert = -1; Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (insert >= 0)
            {
                var tiles = Controls.OfType<IconTile>().ToList(); int x = tiles.Count == 0 ? 5 : insert >= tiles.Count ? tiles[tiles.Count - 1].Right + 1 : tiles[insert].Left - 2;
                using (var pen = new Pen(Color.FromArgb(0, 119, 100), 2)) e.Graphics.DrawLine(pen, x, 5, x, 35);
            }
        }
    }

    public class CompactForm : Form
    {
        public static readonly Color ClearColor = Color.FromArgb(255,0,255);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        public readonly LayerEngine Engine = new LayerEngine();
        public readonly ToolTip Tips = new ToolTip { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 10000, ShowAlways = true };
        public readonly IconStrip Upper, Lower;
        public bool Dragging;
        readonly Button toggle = new SoftButton(), menuButton = new SoftButton();
        readonly Label hint = new Label(), state = new Label();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly string profilePath;
        readonly bool testing;
        ProfileStore store = new ProfileStore();
        List<WindowRef> candidates = new List<WindowRef>();
        ContextMenuStrip activeMenu;
        bool exiting, hotkey;
        bool revealHotkey;
        int ticks, hold;
        LayeredSurface alphaSurface;
        public string HoverStamp {get{return toggle.Text+((SoftButton)toggle).PaintStamp+((SoftButton)menuButton).PaintStamp;}}
        public void EnableSmoothSurface()
        {
            if(alphaSurface!=null)return;
            alphaSurface=new LayeredSurface(this);Opacity=1.0/255.0;alphaSurface.Refresh(true);
        }
        static readonly Color Accent = Color.FromArgb(0, 113, 96), Ink = Color.FromArgb(39, 52, 65);
        public CompactForm() : this(null, false) { }
        public CompactForm(string settingsPath, bool testMode)
        {
            testing = testMode; profilePath = settingsPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.xml");
            Text = "叠窗 · 图标栏"; Font = new Font("Microsoft YaHei UI", 9F); ForeColor = Ink;
            BackColor = ClearColor; TransparencyKey = ClearColor; AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None; MaximizeBox = false; ShowInTaskbar = true; TopMost = true;
            ClientSize = new Size(480, 158); MinimumSize = new Size(360, Height); MaximumSize = new Size(2000, Height);
            StartPosition = FormStartPosition.Manual; Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - Width - 28, Screen.PrimaryScreen.WorkingArea.Top + 65);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 2, 5, 2), ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); Controls.Add(root);
            root.Paint += delegate(object s,PaintEventArgs e){using(var pen=new Pen(Color.FromArgb(150,160,170))) e.Graphics.DrawRectangle(pen,0,0,root.Width-1,root.Height-1);};
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            var grip=new ClearGrip {Dock=DockStyle.Fill,Cursor=Cursors.SizeAll,Margin=new Padding(0,2,4,3),AccessibleName="移动工具栏"};
            grip.MouseDown += delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};
            Tips.SetToolTip(grip,"拖动移动工具栏"); header.Controls.Add(grip,0,0);
            toggle.Text = "开始锁定"; toggle.Dock = DockStyle.Fill; toggle.Margin = new Padding(2, 2, 4, 3); Style(toggle, true); toggle.Click += delegate { Safe(ToggleLock); }; header.Controls.Add(toggle, 2, 0);
            var refresh = new SoftButton { Text = "↻", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 3, 3), AccessibleName = "刷新窗口" }; Style(refresh, false); refresh.Click += delegate { Safe(RefreshWindows); }; header.Controls.Add(refresh, 3, 0); Tips.SetToolTip(refresh, "刷新窗口列表");
            menuButton.Text = "⋯"; menuButton.Dock = DockStyle.Fill; menuButton.Margin = new Padding(0, 2, 0, 3); menuButton.AccessibleName = "方案和设置菜单"; Style(menuButton, false); menuButton.Click += delegate { ShowMainMenu(); }; header.Controls.Add(menuButton, 4, 0); root.Controls.Add(header, 0, 0);
            Upper = new IconStrip(this, true); Lower = new IconStrip(this, false);
            root.Controls.Add(Row("锁定", Upper), 0, 1); root.Controls.Add(Row("自由", Lower), 0, 2);
            hint.Visible = false;
            var trayMenu = new ContextMenuStrip(); trayMenu.Items.Add("显示图标栏", null, delegate { ShowBar(); }); trayMenu.Items.Add("锁定 / 解除锁定", null, delegate { Safe(ToggleLock); }); trayMenu.Items.Add(new ToolStripSeparator()); trayMenu.Items.Add("退出并恢复窗口", null, delegate { ExitTool(); });
            tray.Icon = Icon; tray.Text = "叠窗 · 未锁定"; tray.ContextMenuStrip = trayMenu; tray.Visible = !testing; tray.DoubleClick += delegate { ShowBar(); };
            try { store = ProfileStore.Load(profilePath); } catch (Exception ex) { Tell("方案读取失败：" + ex.Message); }
            timer.Interval = 250; timer.Tick += delegate
            {
                try
                {
                    if (!Dragging) { Engine.Apply(); KeepSelfVisible(); }
                    if (++ticks % 8 == 0 && !Dragging && (activeMenu == null || !activeMenu.Visible)) RefreshWindows();
                    if (hold > 0) hold--; else UpdateState();
                    if(alphaSurface!=null)alphaSurface.Refresh(false);
                }
                catch (Exception ex) { Engine.Stop(); Tell("已解除锁定：" + ex.Message); }
            };
            Shown += delegate
            {
                Engine.Controller = Handle;
                Engine.PreserveControllerTopmost = false;
                Native.ShowWindow(Handle, 4);
                hotkey = !testing && Native.RegisterHotKey(Handle, 2, 0x4000 | 2 | 1, (uint)Keys.F10);
                revealHotkey = !testing && Native.RegisterHotKey(Handle,3,0x4000|2|1,(uint)Keys.F11);
                RefreshWindows();
                if (!testing)
                {
                    var previous = store.Profiles.FirstOrDefault(p => p.Name == store.LastUsedName) ?? (store.Profiles.Count == 1 ? store.Profiles[0] : null);
                    if (previous != null) LoadProfile(previous);
                    EnableSelfManagement();
                }
                timer.Start(); UpdateState();
                if(!testing) EnableSmoothSurface();
            };
            LocationChanged+=delegate{if(alphaSurface!=null)alphaSurface.Refresh(true);};
            VisibleChanged+=delegate{if(alphaSurface!=null)alphaSurface.Refresh(true);};
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
                timer.Stop(); Engine.Stop();
                if (Engine.Notice.Length > 0 && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; exiting = false; timer.Start(); ShowBar(); Tell(Engine.Notice); return; }
                if (hotkey) Native.UnregisterHotKey(Handle, 2); tray.Visible = false;
                if (revealHotkey) Native.UnregisterHotKey(Handle,3);
            };
        }
        Control Row(string label, IconStrip strip)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.ColumnCount=1; row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            row.Controls.Add(strip,0,0); return row;
        }
        static void Style(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.BackColor = primary ? Accent : Color.FromArgb(232,236,243); button.ForeColor = primary ? Color.White : Ink; button.Cursor = Cursors.Hand;
        }
        void Safe(Action action) { try { action(); } catch (Exception ex) { Tell(ex.Message); } }
        public static string BandName(Band b) { return b == Band.Top ? "置顶" : b == Band.Bottom ? "置底" : "普通"; }
        void Tell(string message) { hint.Text = message; Tips.SetToolTip(hint, message); hold = 24; }
        public void RefreshWindows()
        {
            uint self = (uint)Process.GetCurrentProcess().Id;
            var fresh = Native.Order().Where(Native.Candidate).Select(Native.Read).Where(w => w.Pid != self || (!testing && w.Handle==Handle)).ToList();
            foreach (var dead in Engine.Windows.Where(w => !w.Valid).ToArray()) Engine.Remove(dead);
            // Retain existing free-icon order, so activating another app never moves the icon under the pointer.
            candidates = candidates.Where(w => w.Valid && fresh.Any(f => f.Handle == w.Handle)).ToList();
            foreach (var w in fresh) if (!candidates.Any(c => c.Handle == w.Handle)) candidates.Add(w);
            Render();
        }
        void Render()
        {
            Upper.Render(Engine.Windows.ToList());
            Lower.Render(candidates.Where(w => !Engine.Windows.Any(m => m.Handle == w.Handle)).ToList());
            UpdateState();
        }
        void UpdateState()
        {
            toggle.Text = Engine.Running ? "解除锁定" : "开始锁定";
            state.Text = Engine.Running ? (Engine.Notice.Length > 0 ? "● 需注意" : "● 已锁定") : "○ 未锁定"; state.ForeColor = Engine.Running ? Accent : Color.FromArgb(116, 129, 140);
            tray.Text = Engine.Running ? "叠窗 · 已锁定" : "叠窗 · 未锁定";
            if (hold > 0 && Engine.Notice.Length == 0) return;
            string message = Engine.Notice.Length > 0 ? Engine.Notice : "上排从左到右：前 → 后  ·  右键置顶 / 置底";
            if (!testing && !hotkey) message = "快捷键被占用，可用按钮解除锁定。";
            hint.Text = message; Tips.SetToolTip(hint, message);
            Tips.SetToolTip(toggle, "Ctrl + Alt + F10：开始 / 解除锁定\n上排 " + Engine.Windows.Count + " 个受管窗口，下排不参与锁定。");
        }
        public void ToggleLock()
        {
            if (Engine.Running) Engine.Stop();
            else
            {
                if (!Engine.Windows.Any(w => w.Valid)) { Tell("先把窗口图标拖到上排，再开始锁定。"); return; }
                Engine.Start();
            }
            UpdateState();
        }
        public void Transfer(WindowRef w, bool toManaged, int dropIndex)
        {
            Safe(delegate
            {
                if (!w.Valid) { Tell("这个窗口已关闭，点击刷新更新列表。"); return; }
                int oldIndex = Engine.Windows.FindIndex(x => x.Handle == w.Handle);
                WindowRef actual = oldIndex >= 0 ? Engine.Windows[oldIndex] : w;
                if (toManaged)
                {
                    int index = dropIndex < 0 ? Engine.Windows.Count : Math.Max(0, Math.Min(dropIndex, Engine.Windows.Count));
                    if (oldIndex >= 0) { Engine.Windows.RemoveAt(oldIndex); if (index > oldIndex) index--; }
                    index = Math.Max(0, Math.Min(index, Engine.Windows.Count));
                    if (oldIndex >= 0 && index == oldIndex) { Engine.Windows.Insert(index, actual); Render(); return; }
                    // A dropped icon adopts its neighbor's band, keeping the literal left-to-right order meaningful.
                    if (Engine.Windows.Count > 0) actual.Band = Engine.Windows[Math.Min(index, Engine.Windows.Count - 1)].Band;
                    else actual.Band = Band.Top;
                    Engine.Windows.Insert(index, actual);
                    Tell("已加入顺序 · " + BandName(actual.Band) + (Engine.Running ? "，正在生效。" : "；点击开始锁定生效。"));
                }
                else
                {
                    if (oldIndex >= 0) Engine.Remove(actual);
                    if(actual.Handle==Handle) TopMost=false;
                    candidates.RemoveAll(c => c.Handle == actual.Handle);
                    var free = candidates.Where(c => !Engine.Windows.Any(m => m.Handle == c.Handle)).ToList();
                    int index = dropIndex < 0 ? free.Count : Math.Max(0, Math.Min(dropIndex, free.Count));
                    if (index < free.Count) candidates.Insert(candidates.IndexOf(free[index]), actual); else candidates.Add(actual);
                    Tell("已移到自由区 · 不再锁定这个窗口。");
                }
                if (Engine.Windows.Count == 0 && Engine.Running) Engine.Stop();
                Render(); Engine.Apply();
                var strip = toManaged ? Upper : Lower; var tile = strip.Controls.OfType<IconTile>().FirstOrDefault(t => t.Window.Handle == w.Handle); if (tile != null) tile.Focus();
            });
        }
        public void SetBand(WindowRef w, Band band)
        {
            if (!Engine.Windows.Contains(w)) { Tell("先把图标拖到上排，再设置置顶或置底。"); return; }
            w.Band = band; var ordered = Engine.Windows.OrderBy(x => (int)x.Band).ToList(); Engine.Windows.Clear(); Engine.Windows.AddRange(ordered);
            Render(); Engine.Apply(); Tell("已设为" + BandName(band) + " · 置顶在左，普通居中，置底在右。");
        }
        public void ShowIconMenu(IconTile tile)
        {
            CloseMenu(); activeMenu = new ContextMenuStrip(); var w = tile.Window;
            var title = new ToolStripMenuItem(w.Caption.Length > 52 ? w.Caption.Substring(0, 52) + "…" : w.Caption) { Enabled = false }; activeMenu.Items.Add(title); activeMenu.Items.Add(new ToolStripSeparator());
            if (tile.Strip.Managed)
            {
                foreach (Band band in Enum.GetValues(typeof(Band)))
                {
                    Band choice = band; var item = new ToolStripMenuItem(BandName(band)) { Checked = w.Band == band }; item.Click += delegate { Safe(delegate { SetBand(w, choice); }); }; activeMenu.Items.Add(item);
                }
                activeMenu.Items.Add(new ToolStripSeparator()); activeMenu.Items.Add("移到下排 · 不锁定", null, delegate { Transfer(w, false, -1); });
            }
            else activeMenu.Items.Add("加入上排 · 参与排序", null, delegate { Transfer(w, true, -1); });
            activeMenu.Show(tile, new Point(0, tile.Height));
        }
        void CloseMenu() { if (activeMenu != null) { activeMenu.Close(); activeMenu.Dispose(); activeMenu = null; } }
        void ShowMainMenu()
        {
            CloseMenu(); activeMenu = new ContextMenuStrip();
            activeMenu.Items.Add("保存当前方案…", null, delegate { Safe(PromptSaveProfile); });
            var load = new ToolStripMenuItem("加载方案（加载后解除锁定）");
            foreach (var p in store.Profiles)
            {
                Profile profile = p; load.DropDownItems.Add(p.Name, null, delegate { Safe(delegate { LoadProfile(profile); }); });
            }
            load.Enabled = store.Profiles.Count > 0; activeMenu.Items.Add(load);
            activeMenu.Items.Add("刷新窗口", null, delegate { Safe(RefreshWindows); }); activeMenu.Items.Add(new ToolStripSeparator());
            activeMenu.Items.Add("隐藏到托盘", null, delegate { Hide(); }); activeMenu.Items.Add("退出并恢复窗口", null, delegate { ExitTool(); });
            activeMenu.Show(menuButton, new Point(0, menuButton.Height));
        }
        void PromptSaveProfile()
        {
            using (var dialog = new Form { Text = "保存窗口方案", ClientSize = new Size(310, 100), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, TopMost = true, Font = Font })
            {
                var box = new TextBox { Text = "我的窗口顺序", Location = new Point(12, 14), Width = 286 };
                var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(212, 54), Size = new Size(86, 30) };
                dialog.Controls.Add(box); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string name = box.Text.Trim();
                    if (store.Profiles.Any(p => p.Name == name) && MessageBox.Show(this, "覆盖方案“" + name + "”？", "保存方案", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    SaveProfile(name);
                }
            }
        }
        public void SaveProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidOperationException("请输入 1 到 80 字的方案名称。");
            var fresh = new Profile { Name = name };
            foreach (var w in Engine.Windows.Where(w => w.Valid)) fresh.Windows.Add(new SavedWindow { ProcessName = w.ProcessName, WindowClass = w.WindowClass, Caption = Native.Title(w.Handle), Band = w.Band });
            if (fresh.Windows.Count == 0) throw new InvalidOperationException("先把需要排序的图标拖到上排，再保存方案。");
            var next = new ProfileStore { LastUsedName = name, Profiles = store.Profiles.Where(p => p.Name != name).ToList() }; next.Profiles.Add(fresh); next.Save(profilePath); store = next; Tell("已保存方案：" + name);
        }
        public void LoadProfile(Profile profile)
        {
            Engine.Stop(); if (Engine.Notice.Length > 0) throw new InvalidOperationException(Engine.Notice);
            RefreshWindows(); int missing; var windows = ProfileStore.Match(profile, candidates, out missing);
            Engine.Windows.Clear(); Engine.Windows.AddRange(windows.OrderBy(w => (int)w.Band)); Render();
            store.LastUsedName = profile.Name;
            if (!testing) store.Save(profilePath);
            Tell("已加载 " + windows.Count + " 个，跳过 " + missing + " 个；确认后开始锁定。");
        }
        public void ClearInsertMarkers() { Upper.ClearMarker(); Lower.ClearMarker(); }
        public void EnableSelfManagement()
        {
            if(!Engine.Windows.Any(w=>w.Handle==Handle)) {var self=Native.Read(Handle);self.Band=Band.Top;Engine.Windows.Insert(0,self);}
            Render();
        }
        void KeepSelfVisible()
        {
            if(!Visible || !Engine.Running || Engine.Windows.Count==0) return;
            var first=Engine.Windows[0];
            if(first.Handle!=Handle || first.Band!=Band.Top) return;
            if(Native.Order().TakeWhile(h=>h!=Handle).Any(h=>Native.Candidate(h)&&!Native.IsIconic(h)&&Native.IsTop(h)))
                Native.SetWindowPos(Handle,Native.Topmost,0,0,0,0,Native.Flags);
        }
        public void SetDragSurface(bool active)
        {
            // Color-keyed holes pass mouse input through. Temporarily reveal drop surfaces
            // during internal drags so even an empty row can reliably accept a drop.
            Upper.BackColor=Lower.BackColor=active ? Color.FromArgb(237,240,243) : ClearColor;
            Upper.Invalidate(); Lower.Invalidate();
        }
        public void ShowBar() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        public void RevealBar()
        {
            ShowBar(); var self=Engine.Windows.FirstOrDefault(w=>w.Handle==Handle);
            if(self!=null) Engine.Remove(self);
            self=Native.Read(Handle);self.Band=Band.Top;Engine.Windows.Insert(0,self);
            TopMost=true;Native.SetWindowPos(Handle,Native.Topmost,0,0,0,0,Native.Flags);Render();
        }
        public void ExitTool() { exiting = true; Close(); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 2) Safe(ToggleLock);
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 3) Safe(RevealBar);
            if (m.Msg == 0x8001) { ExitTool(); return; }
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing && alphaSurface!=null){alphaSurface.Dispose();alphaSurface=null;}
            if (disposing) { timer.Stop(); Engine.Stop(); if (hotkey && IsHandleCreated) Native.UnregisterHotKey(Handle, 2); CloseMenu(); tray.Visible = false; tray.Dispose(); timer.Dispose(); }
            if(disposing && revealHotkey && IsHandleCreated) Native.UnregisterHotKey(Handle,3);
            base.Dispose(disposing);
            if (disposing) Tips.Dispose();
        }
        public void Preview(string path, int width)
        {
            if (width > 0) Width = width; Show(); Application.DoEvents();
            var lines = new List<string>(); DescribeLayout(this, lines); File.WriteAllLines(path + ".layout.txt", lines);
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(path); }
            ExitTool();
        }
        static void DescribeLayout(Control parent, List<string> lines)
        {
            lines.Add(parent.GetType().Name + " | " + parent.Text + " | " + parent.Bounds + " | Font=" + parent.Font.Size);
            foreach (Control child in parent.Controls) DescribeLayout(child, lines);
        }
    }
}

