using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace WindowLayers
{
    public static class SelfTests
    {
        static readonly List<string> results = new List<string>();
        static readonly List<Process> probes = new List<Process>();
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        static void Pump(int ms)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }
        static void Settle(LayerEngine engine)
        {
            for (int i = 0; i < 20; i++) { engine.Apply(); Pump(50); }
        }
        static void Test(string name, Action run)
        {
            try { run(); results.Add("PASS  " + name); }
            catch (Exception ex) { results.Add("FAIL  " + name + ": " + ex.Message); }
        }
        static WindowRef Probe(string title, bool top)
        {
            var process = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--probe " + title + (top ? " top" : "")) { UseShellExecute = false }); probes.Add(process);
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 10000)
            {
                var h = Native.Order().FirstOrDefault(x => Native.Title(x) == title);
                if (h != IntPtr.Zero && Native.IsWindowVisible(h) && Native.IsTop(h) == top)
                {
                    // A newly visible cross-process Form may still be finishing its TopMost initialization.
                    Pump(120);
                    if (Native.IsTop(h) == top) return Native.Read(h);
                }
                Pump(80);
            }
            throw new Exception("Probe did not appear: " + title);
        }
        static void Before(WindowRef a, WindowRef b)
        {
            var order = Native.Order(); Assert(order.IndexOf(a.Handle) >= 0 && order.IndexOf(a.Handle) < order.IndexOf(b.Handle), a.Caption + " [" + order.IndexOf(a.Handle) + ", top=" + Native.IsTop(a.Handle) + "] must precede " + b.Caption + " [" + order.IndexOf(b.Handle) + ", top=" + Native.IsTop(b.Handle) + "]");
        }
        public static int Run(string report)
        {
            var engine = new LayerEngine();
            Test("Color-key rendering preserves black and original RGB without purple fringes", delegate
            {
                using(var source=new Bitmap(4,1,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    source.SetPixel(0,0,Color.FromArgb(255,0,0,0));
                    source.SetPixel(1,0,Color.FromArgb(128,0,0,0));
                    source.SetPixel(2,0,Color.FromArgb(180,80,120,160));
                    source.SetPixel(3,0,Color.FromArgb(0,0,0,0));
                    using(var rendered=WindowIcons.ColorKeyImage(source,4,1,CompactForm.ClearColor))
                    {
                        Assert(rendered.GetPixel(0,0).ToArgb()==Color.Black.ToArgb(),"opaque black changed");
                        Assert(rendered.GetPixel(1,0).ToArgb()==Color.Black.ToArgb(),"black edge picked up purple");
                        var color=rendered.GetPixel(2,0); Assert(Math.Abs(color.R-80)<=1 && Math.Abs(color.G-120)<=1 && Math.Abs(color.B-160)<=1,"source RGB changed");
                        Assert(rendered.GetPixel(3,0).ToArgb()==CompactForm.ClearColor.ToArgb(),"transparent pixel not keyed");
                    }
                }
            });
            try
            {
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 7);
                var a = Probe("WL_A_" + suffix, false); var b = Probe("WL_B_" + suffix, false);
                var c = Probe("WL_C_" + suffix, true); var u = Probe("WL_U_" + suffix, false);
                Test("Enumerate independent process windows and validate identities", delegate { Assert(a.Valid && b.Valid && c.Valid && u.Valid, "identity"); Assert(Native.Candidate(a.Handle), "enumeration"); Assert(a.Pid != Process.GetCurrentProcess().Id, "must be cross-process"); });
                Test("Normal group converges to A > B > C", delegate
                {
                    Assert(Native.IsTop(c.Handle), "topmost probe initialization");
                    engine.Windows.AddRange(new[] { a, b, c }); engine.Start(); Settle(engine);
                    Before(a, b); Before(b, c); Assert(!Native.IsTop(c.Handle), "normal removes previous topmost");
                });
                Test("Lock repairs order after an external Z-order change", delegate
                {
                    Assert(Native.SetWindowPos(a.Handle, c.Handle, 0, 0, 0, 0, Native.Flags), "external request failed"); Pump(200); Before(c, a); Settle(engine); Before(a, b); Before(b, c);
                });
                Test("Reconciliation does not steal foreground keyboard focus", delegate
                {
                    var focus = Native.GetForegroundWindow();
                    Native.SetWindowPos(a.Handle, b.Handle, 0, 0, 0, 0, Native.Flags); Pump(100); Settle(engine);
                    Assert(Native.GetForegroundWindow() == focus, "foreground window changed");
                });
                Test("Top band stays topmost in the chosen internal order", delegate
                {
                    a.Band = Band.Top; b.Band = Band.Top; Settle(engine);
                    Assert(Native.IsTop(a.Handle) && Native.IsTop(b.Handle), "top flags"); Before(a, b); Before(b, u);
                    Native.SetWindowPos(b.Handle, Native.Topmost, 0, 0, 0, 0, Native.Flags); Pump(80); Settle(engine); Before(a, b);
                });
                Test("Bottom band stays below unmanaged windows and preserves A > B", delegate
                {
                    a.Band = Band.Bottom; b.Band = Band.Bottom; Settle(engine);
                    Before(u, a); Before(c, a); Before(a, b); Assert(!Native.IsTop(a.Handle), "bottom must not be topmost");
                    Native.SetWindowPos(u.Handle, Native.Bottom, 0, 0, 0, 0, Native.Flags); Pump(100); Settle(engine); Before(u, a); Before(a, b);
                });
                Test("Stable order causes no redundant SetWindowPos calls", delegate
                {
                    Settle(engine); int count = engine.Changes; Settle(engine); Assert(engine.Changes == count, "unnecessary reorder churn");
                });
                Test("Pause restores original topmost state, then stops enforcing", delegate
                {
                    engine.Stop(); Pump(250); Assert(Native.IsTop(c.Handle), "original topmost lost"); Assert(!Native.IsTop(a.Handle) && !Native.IsTop(b.Handle), "original ordinary flags lost");
                    Native.SetWindowPos(b.Handle, Native.Top, 0, 0, 0, 0, Native.Flags); Pump(100); var before = Native.Order(); engine.Apply(); Pump(100);
                    Assert(before.Where(h => h == a.Handle || h == b.Handle).SequenceEqual(Native.Order().Where(h => h == a.Handle || h == b.Handle)), "paused engine changed order");
                });
                Test("Removing an active window restores its original topmost flag", delegate
                {
                    a.Band = Band.Top; engine.Start(); Settle(engine); Assert(Native.IsTop(a.Handle), "not pinned"); engine.Remove(a); Pump(200); Assert(!Native.IsTop(a.Handle), "remove did not restore"); engine.Stop(); Pump(200);
                });
                Test("Chinese UI adds, reorders, changes bands, toggles hotkey and recovers from tray", delegate
                {
                    using (var form = new MainForm())
                    {
                        try
                        {
                            form.Show(); Native.ShowWindow(form.Handle, 4); Pump(150); form.RefreshWindows();
                            var fields = typeof(MainForm).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            var source = (ListView)fields.First(f => f.Name == "available").GetValue(form);
                            var target = (ListView)fields.First(f => f.Name == "managed").GetValue(form);
                            var controller = (LayerEngine)fields.First(f => f.Name == "engine").GetValue(form);
                            foreach (ListViewItem item in source.Items) if (((WindowRef)item.Tag).Handle == a.Handle || ((WindowRef)item.Tag).Handle == b.Handle) item.Selected = true;
                            form.AddSelected(); Assert(controller.Windows.Count == 2, "UI add");
                            target.Items[1].Selected = true;
                            var chosen = controller.Windows[1]; FindButton(form, "↑ 上移").PerformClick(); Assert(controller.Windows[0] == chosen, "UI move up");
                            FindButton(form, "置顶").PerformClick(); Assert(chosen.Band == Band.Top, "UI band switch");
                            form.ToggleLock(); Pump(300); Assert(controller.Running && Native.IsTop(chosen.Handle), "UI start lock");
                            PostMessage(form.Handle, 0x0312, new IntPtr(1), IntPtr.Zero); Pump(150); Assert(!controller.Running, "global-hotkey handler");
                            form.ToggleLock(); Pump(300); form.Close(); Assert(!form.Visible && controller.Running, "close-to-tray");
                            var tray = (NotifyIcon)fields.First(f => f.Name == "tray").GetValue(form); ((ToolStripMenuItem)tray.ContextMenuStrip.Items[0]).PerformClick();
                            Assert(form.Visible, "restore from tray"); ((ToolStripMenuItem)tray.ContextMenuStrip.Items[3]).PerformClick(); Pump(200);
                            Assert(!Native.IsTop(a.Handle) && !Native.IsTop(b.Handle), "exit must restore target flags");
                        }
                        finally
                        {
                            if (!form.IsDisposed)
                            {
                                typeof(MainForm).GetField("exiting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(form, true); form.Close();
                            }
                        }
                    }
                });
                Test("Smooth display preserves fractional alpha, outlined text and rounded transparent corners", delegate
                {
                    using(var bar=new CompactForm(Path.Combine(Path.GetDirectoryName(report),"compact-test-settings.xml"),true))
                    {
                        bar.Show();Native.ShowWindow(bar.Handle,4);Pump(100);bar.RefreshWindows();Drop(bar.Upper,a,0);
                        using(var bitmap=LayeredSurface.Compose(bar))
                        {
                            Assert(bitmap.GetPixel(0,0).A==0,"corner is opaque");
                            int fractional=0,white=0,black=0;
                            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
                            {var pixel=bitmap.GetPixel(x,y);if(pixel.A>0&&pixel.A<255)fractional++;if(y<32&&pixel.A>200){if(pixel.R>235&&pixel.G>235&&pixel.B>235)white++;if(pixel.R<25&&pixel.G<25&&pixel.B<25)black++;}}
                            Assert(fractional>50,"smooth alpha edges missing");Assert(white>10&&black>10,"outlined white text missing");
                            bitmap.Save(Path.Combine(Path.GetDirectoryName(report),"smooth-preview.png"));
                        }
                        bar.EnableSmoothSurface();Pump(100);bar.ToggleLock();Pump(100);Assert(bar.Engine.Running,"smooth surface blocked model");bar.Hide();Pump(100);bar.ShowBar();bar.ExitTool();
                    }
                });
                Test("Transparent button and grip rendering does not blend against purple", delegate
                {
                    using(var button=new SoftButton {Size=new Size(100,30),Text="锁定",BackColor=Color.FromArgb(0,113,96),ForeColor=Color.White})
                    using(var bitmap=new Bitmap(100,30))
                    {
                        button.DrawToBitmap(bitmap,new Rectangle(0,0,100,30));
                        for(int y=0;y<30;y++) for(int x=0;x<100;x++) {var pixel=bitmap.GetPixel(x,y);Assert(pixel.ToArgb()==CompactForm.ClearColor.ToArgb() || !(pixel.R>100 && pixel.B>100 && pixel.G<70),"purple fringe in button");}
                    }
                    using(var grip=new ClearGrip {Size=new Size(24,26)}) using(var bitmap=new Bitmap(24,26))
                    {grip.DrawToBitmap(bitmap,new Rectangle(0,0,24,26));Assert(bitmap.GetPixel(0,0).ToArgb()==CompactForm.ClearColor.ToArgb(),"grip hit mask extends beyond dot grid");Assert(bitmap.GetPixel(11,11).ToArgb()==Color.White.ToArgb(),"gaps between dots are not draggable");}
                });
                Test("Toolbar itself supports bottom, free, top and fullscreen recovery", delegate
                {
                    using(var bar=new CompactForm(Path.Combine(Path.GetDirectoryName(report),"compact-test-settings.xml"),true))
                    using(var fullscreen=new Form {Text="WL_Fullscreen_"+suffix,FormBorderStyle=FormBorderStyle.None,Bounds=Screen.PrimaryScreen.WorkingArea})
                    {
                        bar.Show();Native.ShowWindow(bar.Handle,4);Pump(100);bar.EnableSelfManagement();
                        var self=bar.Engine.Windows.First(w=>w.Handle==bar.Handle);
                        bar.SetBand(self,Band.Bottom);bar.ToggleLock();Settle(bar.Engine);Assert(!Native.IsTop(bar.Handle),"controller forcibly repinned itself");
                        bar.Transfer(self,false,0);Pump(150);Assert(!Native.IsTop(bar.Handle),"free toolbar stayed pinned");
                        bar.RevealBar();Assert(Native.IsTop(bar.Handle) && bar.Engine.Windows[0].Handle==bar.Handle,"reveal did not pin self first");
                        if(!bar.Engine.Running) bar.ToggleLock();
                        fullscreen.Show();Native.ShowWindow(fullscreen.Handle,4);Native.SetWindowPos(fullscreen.Handle,Native.Topmost,0,0,0,0,Native.Flags);Pump(800);
                        var order=Native.Order();Assert(order.IndexOf(bar.Handle)<order.IndexOf(fullscreen.Handle),"fullscreen overlay hid toolbar");
                        fullscreen.Close();bar.ExitTool();
                    }
                });
                Test("Compact toolbar drop events add and reorder icons without starting a paused lock", delegate
                {
                    using (var bar = new CompactForm(Path.Combine(Path.GetDirectoryName(report), "compact-test-settings.xml"), true))
                    {
                        bar.Show(); Native.ShowWindow(bar.Handle, 4); Pump(120); bar.RefreshWindows();
                        Drop(bar.Upper, a, 0); Drop(bar.Upper, b, 1); Drop(bar.Upper, c, 2);
                        Assert(bar.Engine.Windows.Select(w => w.Handle).SequenceEqual(new[] { a.Handle, b.Handle, c.Handle }), "add order");
                        Assert(!bar.Engine.Running, "drop must respect paused global toggle");
                        Assert(!bar.Lower.Items.Any(w => w.Handle == a.Handle || w.Handle == b.Handle || w.Handle == c.Handle), "managed icons leaked into free row");
                        Drop(bar.Upper, c, 0);
                        Assert(bar.Engine.Windows.Select(w => w.Handle).SequenceEqual(new[] { c.Handle, a.Handle, b.Handle }), "drop reorder");
                        Assert(bar.Upper.Controls.OfType<IconTile>().All(t => t.AccessibleName.Contains("置顶")), "new managed icons must default to topmost");
                        bar.ExitTool();
                    }
                });
                Test("Compact toolbar dragging a pinned icon down restores flags and stops the empty lock", delegate
                {
                    using (var bar = new CompactForm(Path.Combine(Path.GetDirectoryName(report), "compact-test-settings.xml"), true))
                    {
                        bar.Show(); Native.ShowWindow(bar.Handle, 4); Pump(100); bar.RefreshWindows();
                        Drop(bar.Upper, a, 0); var selected = bar.Engine.Windows[0]; bar.SetBand(selected, Band.Top);
                        Drop(bar.Upper, a, 0); Assert(selected.Band == Band.Top, "no-op drop changed band");
                        bar.ToggleLock(); Settle(bar.Engine); Assert(Native.IsTop(a.Handle), "pin did not apply");
                        Drop(bar.Lower, a, 0); Pump(200);
                        Assert(!Native.IsTop(a.Handle), "drag-out must restore original flag");
                        Assert(!bar.Engine.Running && bar.Engine.Windows.Count == 0, "last drag-out must release lock");
                        Assert(bar.Lower.Items.Any(w => w.Handle == a.Handle), "removed icon missing from free strip"); bar.ExitTool();
                    }
                });
                Test("Compact toolbar cross-band drops keep literal left-to-right priority", delegate
                {
                    using (var bar = new CompactForm(Path.Combine(Path.GetDirectoryName(report), "compact-test-settings.xml"), true))
                    {
                        bar.Show(); Native.ShowWindow(bar.Handle, 4); Pump(100); bar.RefreshWindows();
                        Drop(bar.Upper, a, 0); Drop(bar.Upper, b, 1); Drop(bar.Upper, c, 2);
                        var first = bar.Engine.Windows.First(w => w.Handle == a.Handle);
                        var last = bar.Engine.Windows.First(w => w.Handle == c.Handle);
                        bar.SetBand(first, Band.Top); bar.SetBand(last, Band.Bottom);
                        Drop(bar.Upper, last, 0);
                        Assert(bar.Engine.Windows[0] == last && last.Band == Band.Top, "drop must adopt destination band");
                        Assert(bar.Engine.Windows.Select(w => (int)w.Band).SequenceEqual(bar.Engine.Windows.Select(w => (int)w.Band).OrderBy(x => x)), "bands out of order");
                        bar.ExitTool();
                    }
                });
                Test("Compact toolbar profiles preserve icon order and load with the lock released", delegate
                {
                    string settings = Path.Combine(Path.GetDirectoryName(report), "compact-test-settings.xml");
                    try
                    {
                        using (var bar = new CompactForm(settings, true))
                        {
                            bar.Show(); Native.ShowWindow(bar.Handle, 4); Pump(100); bar.RefreshWindows(); Drop(bar.Upper, b, 0); Drop(bar.Upper, a, 1);
                            bar.SaveProfile("小图标方案"); bar.ToggleLock(); Settle(bar.Engine);
                            bar.LoadProfile(ProfileStore.Load(settings).Profiles[0]);
                            Assert(!bar.Engine.Running, "loaded profile must be paused");
                            Assert(bar.Engine.Windows.Select(w => w.Handle).SequenceEqual(new[] { b.Handle, a.Handle }), "profile icon order changed"); bar.ExitTool();
                        }
                    }
                    finally { if (File.Exists(settings)) File.Delete(settings); }
                });
                Test("Compact toolbar narrow layout stays two rows and free icons remain stable", delegate
                {
                    using (var bar = new CompactForm(Path.Combine(Path.GetDirectoryName(report), "compact-test-settings.xml"), true))
                    {
                        bar.Show(); Native.ShowWindow(bar.Handle, 4); Pump(100); bar.Width = 360; bar.RefreshWindows(); Pump(100);
                        Assert(bar.Height < 260 && bar.Width == 360, "compact dimensions");
                        Assert(Native.GetWindow(bar.Handle, 4) == IntPtr.Zero && Native.IsWindowVisible(bar.Handle), "toolbar must be visible and discoverable");
                        Assert(bar.Upper.PointToScreen(Point.Empty).Y < bar.Lower.PointToScreen(Point.Empty).Y, "strip order");
                        Assert(bar.Upper.Bottom <= bar.Upper.Parent.ClientSize.Height && bar.Lower.Bottom <= bar.Lower.Parent.ClientSize.Height, "strip overflows its row");
                        Assert(!bar.Lower.VerticalScroll.Visible, "unwanted vertical scrollbar");
                        if (bar.Lower.Controls.Count * 46 > bar.Lower.ClientSize.Width) Assert(bar.Lower.HorizontalScroll.Visible, "overflowed icons need horizontal scrolling");
                        var handles = bar.Lower.Items.Select(w => w.Handle).ToArray();
                        Native.SetWindowPos(u.Handle, a.Handle, 0, 0, 0, 0, Native.Flags); Pump(100); bar.RefreshWindows();
                        Assert(handles.SequenceEqual(bar.Lower.Items.Select(w => w.Handle)), "free row shifted on window activation"); bar.ExitTool();
                    }
                });
                Test("Compact toolbar hotkey handler toggles lock, close hides, exit restores flags", delegate
                {
                    using (var bar = new CompactForm(Path.Combine(Path.GetDirectoryName(report), "compact-test-settings.xml"), true))
                    {
                        bar.Show(); Native.ShowWindow(bar.Handle, 4); Pump(100); bar.RefreshWindows(); Drop(bar.Upper, a, 0); bar.SetBand(bar.Engine.Windows[0], Band.Top);
                        PostMessage(bar.Handle, 0x0312, new IntPtr(2), IntPtr.Zero); Pump(300); Assert(bar.Engine.Running, "start via hotkey handler");
                        bar.Close(); Assert(!bar.Visible && bar.Engine.Running, "close should hide to tray"); bar.ShowBar(); Assert(bar.Visible, "show after tray");
                        bar.ExitTool(); Pump(200); Assert(!Native.IsTop(a.Handle), "compact exit did not restore");
                    }
                });
                Test("Minimized windows stay minimized while the lock is active", delegate
                {
                    // WM_SYSCOMMAND / SC_MINIMIZE to the disposable probe.
                    SendMinimize(b.Handle); Pump(250); engine.Start(); Settle(engine); Assert(Native.IsIconic(b.Handle), "window was unexpectedly restored"); engine.Stop(); Pump(200);
                });
                Test("Modeless owned helpers and tooltips do not freeze other windows", delegate
                {
                    using (var owner = new Form { Text = "WL_Modeless_" + suffix })
                    using (var helper = new Form { Text = "WL_Helper_" + suffix, FormBorderStyle = FormBorderStyle.FixedToolWindow })
                    {
                        owner.Show(); Native.ShowWindow(owner.Handle, 4); helper.Show(owner); Native.ShowWindow(helper.Handle, 4); Pump(100);
                        var local = new LayerEngine(); var parent = Native.Read(owner.Handle); parent.Band = Band.Top;
                        var other = Native.Read(a.Handle); other.Band = Band.Top; local.Windows.Add(parent); local.Windows.Add(other);
                        try { local.Start(); Settle(local); Assert(Native.IsTop(parent.Handle) && Native.IsTop(a.Handle), "modeless helper incorrectly froze the group"); }
                        finally { local.Stop(); Pump(150); }
                    }
                });
                Test("Modal dialogs pause only their parent, while unrelated windows continue", delegate
                {
                    using (var owner = new Form { Text = "WL_ModalOwner_" + suffix, Size = new Size(300, 150) })
                    using (var dialog = new Form { Text = "WL_TestDialog_" + suffix, Size = new Size(220, 100) })
                    {
                        owner.Show(); Native.ShowWindow(owner.Handle, 4); dialog.Show(owner); Native.ShowWindow(dialog.Handle, 4); Pump(100);
                        owner.Enabled = false;
                        Assert(Native.IsWindowVisible(owner.Handle) && Native.IsWindowVisible(dialog.Handle), "test dialogs must be visible");
                        var local = new LayerEngine(); var w = Native.Read(owner.Handle); w.Band = Band.Bottom; local.Windows.Add(w);
                        var other = Native.Read(a.Handle); other.Band = Band.Top; local.Windows.Add(other);
                        try { local.Start(); Settle(local); Assert(local.Notice.Contains("弹窗"), "modal dialog not detected"); Assert(Native.IsTop(a.Handle), "unrelated window stalled behind modal parent"); }
                        finally { local.Stop(); Pump(150); owner.Enabled = true; dialog.Close(); owner.Close(); }
                    }
                });
                Test("Profile XML round trip preserves named groups and order", delegate
                {
                    string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "profile-test-" + suffix + ".xml");
                    try
                    {
                        var profile = new Profile { Name = "中文方案" };
                        profile.Windows.Add(new SavedWindow { ProcessName = a.ProcessName, WindowClass = a.WindowClass, Caption = a.Caption, Band = Band.Top });
                        profile.Windows.Add(new SavedWindow { ProcessName = c.ProcessName, WindowClass = c.WindowClass, Caption = c.Caption, Band = Band.Bottom });
                        var store = new ProfileStore(); store.Profiles.Add(profile); store.Save(path); store.Save(path);
                        var loaded = ProfileStore.Load(path); Assert(loaded.Profiles[0].Name == "中文方案", "Unicode name");
                        int missing; var matches = ProfileStore.Match(loaded.Profiles[0], new List<WindowRef> { c, a }, out missing);
                        Assert(missing == 0 && matches[0] == a && matches[1] == c && c.Band == Band.Bottom, "profile order or band");
                    }
                    finally { if (File.Exists(path)) File.Delete(path); }
                });
                Test("Ambiguous and changed-title profile matches are safely skipped", delegate
                {
                    var p = new Profile(); p.Windows.Add(new SavedWindow { ProcessName = a.ProcessName, WindowClass = a.WindowClass, Caption = a.Caption });
                    var duplicate = Native.Read(a.Handle); int missing;
                    Assert(ProfileStore.Match(p, new List<WindowRef> { a, duplicate }, out missing).Count == 0 && missing == 1, "ambiguous match accepted");
                    p.Windows[0].Caption += " changed"; Assert(ProfileStore.Match(p, new List<WindowRef> { a }, out missing).Count == 0 && missing == 1, "changed title accepted");
                });
                Test("Closed windows cannot be mistaken for live targets", delegate
                {
                    probes[3].CloseMainWindow(); Pump(350); Assert(!u.Valid, "closed identity is still valid");
                    engine.Windows.Add(u); engine.Start(); Settle(engine); engine.Stop();
                });
            }
            catch (Exception ex) { results.Add("FAIL  Test setup: " + ex); }
            finally
            {
                engine.Stop(); Pump(200);
                foreach (var process in probes)
                {
                    try { if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(1500)) process.Kill(); } process.Dispose(); } catch { }
                }
            }
            results.Insert(0, "Window Layers integration tests | " + DateTimeOffset.Now.ToString("O"));
            results.Add("Result: " + results.Count(x => x.StartsWith("PASS")) + " passed; " + results.Count(x => x.StartsWith("FAIL")) + " failed.");
            File.WriteAllLines(report, results, System.Text.Encoding.UTF8); return results.Any(x => x.StartsWith("FAIL")) ? 1 : 0;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        static Button FindButton(Control parent, string text)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Button && c.Text == text) return (Button)c;
                var found = FindButton(c, text); if (found != null) return found;
            }
            return null;
        }
        static void Drop(IconStrip strip, WindowRef window, int index)
        {
            var tiles = strip.Controls.OfType<IconTile>().ToList();
            int x = index >= tiles.Count ? (tiles.Count == 0 ? 5 : tiles.Last().Right + 5) : tiles[index].Left + 2;
            var point = strip.PointToScreen(new Point(x, 14)); var data = new DataObject(); data.SetData(typeof(WindowRef), window);
            strip.DropAt(new DragEventArgs(data, 0, point.X, point.Y, DragDropEffects.Move, DragDropEffects.Move)); Application.DoEvents();
        }
        static void SendMinimize(IntPtr h) { PostMessage(h, 0x0112, new IntPtr(0xF020), IntPtr.Zero); }
        public static void RunProbe(string caption, bool top)
        {
            var form = new Form { Text = caption, TopMost = top, Size = new Size(320, 180), StartPosition = FormStartPosition.Manual, Location = new Point(70, 70) };
            form.Controls.Add(new Label { Text = "Temporary window for Window Layers integration tests.\nThis window closes automatically.", Dock = DockStyle.Fill, Padding = new Padding(18) });
            form.Shown += delegate
            {
                // Explicitly establish fixture state after WinForms finishes first-window startup.
                Native.SetWindowPos(form.Handle, top ? Native.Topmost : Native.NotTopmost, 0, 0, 0, 0, Native.Flags);
            };
            var timer = new System.Windows.Forms.Timer { Interval = 90000 }; timer.Tick += delegate { form.Close(); }; timer.Start();
            Application.Run(form); timer.Dispose(); form.Dispose();
        }
    }
}

