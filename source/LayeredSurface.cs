using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowLayers
{
    // An input-transparent owned surface supplies real per-pixel alpha; the original
    // controls remain the input/accessibility layer, so drag/drop and keyboard paths stay intact.
    public sealed class LayeredSurface : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct PointI { public int X,Y; public PointI(int x,int y){X=x;Y=y;} }
        [StructLayout(LayoutKind.Sequential)] struct SizeI { public int Width,Height; public SizeI(int w,int h){Width=w;Height=h;} }
        [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend { public byte Op,Flags,Alpha,Format; }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h,IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dest,ref PointI pos,ref SizeI size,IntPtr source,ref PointI zero,int key,ref Blend blend,int flags);
        [DllImport("user32.dll")] static extern bool ValidateRect(IntPtr h,IntPtr rect);
        readonly CompactForm owner;
        string stamp;
        public LayeredSurface(CompactForm form)
        {
            owner=form;
            CreateHandle(new CreateParams { Caption="",Parent=form.Handle,Style=unchecked((int)0x80000000),ExStyle=0x80000|0x80|0x20|0x08000000,Width=form.Width,Height=form.Height });
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}
            if(m.Msg==0x21){m.Result=new IntPtr(3);return;}
            if(m.Msg==0x14){m.Result=new IntPtr(1);return;}
            if(m.Msg==0xF){ValidateRect(Handle,IntPtr.Zero);m.Result=IntPtr.Zero;return;}
            base.WndProc(ref m);
        }
        public static Bitmap Compose(CompactForm owner)
        {
            var bitmap=new Bitmap(owner.ClientSize.Width,owner.ClientSize.Height,PixelFormat.Format32bppPArgb);
            using(var g=Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                using(var path=Surface.Round(new Rectangle(1,1,bitmap.Width-3,bitmap.Height-3),8))
                using(var pen=new Pen(Color.FromArgb(185,130,140,150),1)) g.DrawPath(pen,path);
                PaintChildren(owner,owner,g,Point.Empty);
            }
            return bitmap;
        }
        static void PaintChildren(CompactForm owner,Control parent,Graphics g,Point offset)
        {
            foreach(Control child in parent.Controls)
            {
                if(!child.Visible) continue;
                var point=new Point(offset.X+child.Left,offset.Y+child.Top);
                var saved=g.Save();g.SetClip(new Rectangle(point,child.ClientSize),CombineMode.Intersect);
                var strip=child as IconStrip;
                if(strip!=null && owner.Dragging) using(var brush=new SolidBrush(Color.FromArgb(35,255,255,255))) g.FillRectangle(brush,new Rectangle(point,child.Size));
                var tile=child as IconTile;
                if(tile!=null) tile.PaintSmooth(g,point);
                var button=child as SoftButton;
                if(button!=null) button.PaintSmooth(g,point);
                if(child is ClearGrip)
                {
                    using(var pen=new Pen(Color.FromArgb(210,25,30,35),1)) using(var brush=new SolidBrush(Color.White))
                    for(int y=7;y<=19;y+=6) for(int x=(child.Width-9)/2;x<=(child.Width-9)/2+6;x+=6){g.FillEllipse(brush,point.X+x,point.Y+y,3,3);g.DrawEllipse(pen,point.X+x,point.Y+y,3,3);}
                }
                PaintChildren(owner,child,g,point);g.Restore(saved);
            }
        }
        public void Refresh(bool force)
        {
            if(!owner.Visible){Native.ShowWindow(Handle,0);stamp=null;return;}
            Native.ShowWindow(Handle,4);
            bool top=Native.IsTop(owner.Handle);
            if(Native.IsTop(Handle)!=top) Native.SetWindowPos(Handle,top?Native.Topmost:Native.NotTopmost,0,0,0,0,Native.Flags);
            var above=Native.GetWindow(owner.Handle,3);
            if(above!=Handle)
            {
                var insert=above==IntPtr.Zero || (!top && Native.IsTop(above)) ? (top?Native.Topmost:Native.Top) : above;
                Native.SetWindowPos(Handle,insert,0,0,0,0,Native.Flags);
            }
            var current=owner.Bounds.ToString()+owner.Engine.Running+owner.Dragging+
                string.Join("|",owner.Upper.Controls.OfType<IconTile>().Concat(owner.Lower.Controls.OfType<IconTile>()).Select(t=>t.Window.Handle+":"+t.Bounds+":"+t.Focused))+owner.HoverStamp;
            if(!force && current==stamp) return;
            using(var bitmap=Compose(owner))
            {
                IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),dib=IntPtr.Zero,previous=IntPtr.Zero;
                try
                {
                    dib=bitmap.GetHbitmap(Color.FromArgb(0));previous=SelectObject(dc,dib);
                    var pos=new PointI(owner.Left,owner.Top);var size=new SizeI(bitmap.Width,bitmap.Height);var zero=new PointI(0,0);var blend=new Blend {Alpha=255,Format=1};
                    if(!UpdateLayeredWindow(Handle,screen,ref pos,ref size,dc,ref zero,0,ref blend,2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    Native.ShowWindow(Handle,4);stamp=current;
                }
                finally {if(previous!=IntPtr.Zero)SelectObject(dc,previous);if(dib!=IntPtr.Zero)DeleteObject(dib);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
            }
        }
        public void Dispose(){if(Handle!=IntPtr.Zero)DestroyHandle();}
    }
}
