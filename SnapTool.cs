using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SnapTool
{
    internal static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;

        public const int HOTKEY_ID = 0x5A31;
    }

    internal static class HotkeyText
    {
        public static string Describe(uint mods, uint vk)
        {
            StringBuilder sb = new StringBuilder();
            if ((mods & Native.MOD_CONTROL) != 0) sb.Append("Ctrl + ");
            if ((mods & Native.MOD_ALT) != 0) sb.Append("Alt + ");
            if ((mods & Native.MOD_SHIFT) != 0) sb.Append("Shift + ");
            if ((mods & Native.MOD_WIN) != 0) sb.Append("Win + ");
            sb.Append(KeyName(vk));
            return sb.ToString();
        }

        public static string KeyName(uint vk)
        {
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            switch (vk)
            {
                case 0x2C: return "PrintScreen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x20: return "Space";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x13: return "Pause";
                case 0x91: return "ScrollLock";
            }
            try { return ((Keys)(int)vk).ToString(); }
            catch { return "VK" + vk; }
        }

        public static bool IsModifierKey(Keys k)
        {
            return k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu
                || k == Keys.LControlKey || k == Keys.RControlKey
                || k == Keys.LShiftKey || k == Keys.RShiftKey
                || k == Keys.LMenu || k == Keys.RMenu
                || k == Keys.LWin || k == Keys.RWin;
        }
    }

    internal static class Config
    {
        public static bool Stay = false;
        public static uint HotMods = Native.MOD_CONTROL | Native.MOD_ALT;
        public static uint HotVk = 0x51;

        private static string ConfigPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapTool");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "config.ini");
        }

        public static void Load()
        {
            try
            {
                string p = ConfigPath();
                if (!File.Exists(p)) return;
                string[] lines = File.ReadAllLines(p);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("stay="))
                    {
                        Stay = line.Substring(5).Trim() == "1";
                    }
                    else if (line.StartsWith("hotkeyMods="))
                    {
                        uint v = 0;
                        if (uint.TryParse(line.Substring(11).Trim(), out v)) HotMods = v;
                    }
                    else if (line.StartsWith("hotkeyVk="))
                    {
                        uint v = 0;
                        if (uint.TryParse(line.Substring(9).Trim(), out v)) HotVk = v;
                    }
                    else if (line.StartsWith("direct="))
                    {
                        Stay = line.Substring(7).Trim() == "0";
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(ConfigPath(),
                    "stay=" + (Stay ? "1" : "0") + Environment.NewLine +
                    "hotkeyMods=" + HotMods + Environment.NewLine +
                    "hotkeyVk=" + HotVk + Environment.NewLine);
            }
            catch { }
        }
    }

    internal static class ClipboardUtil
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CloseClipboard();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint CF_DIB = 8;

        public static void SetBitmap(Bitmap bmp)
        {
            byte[] dib = ToDibBytes(bmp);
            Exception last = null;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                IntPtr hDib = IntPtr.Zero;
                try
                {
                    if (OpenClipboard(IntPtr.Zero) == IntPtr.Zero)
                    {
                        Thread.Sleep(90);
                        continue;
                    }

                    EmptyClipboard();

                    hDib = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)dib.Length);
                    if (hDib == IntPtr.Zero) throw new Exception("GlobalAlloc 失败");

                    IntPtr ptr = GlobalLock(hDib);
                    if (ptr == IntPtr.Zero) throw new Exception("GlobalLock 失败");
                    Marshal.Copy(dib, 0, ptr, dib.Length);
                    GlobalUnlock(hDib);

                    if (SetClipboardData(CF_DIB, hDib) == IntPtr.Zero)
                    {
                        throw new Exception("SetClipboardData 失败");
                    }

                    hDib = IntPtr.Zero;
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                finally
                {
                    if (hDib != IntPtr.Zero) GlobalFree(hDib);
                    CloseClipboard();
                }
                Thread.Sleep(90);
            }

            try
            {
                Clipboard.SetDataObject(bmp, true);
                return;
            }
            catch { }

            throw new Exception("剪贴板写入失败" + (last != null ? "：" + last.Message : ""));
        }

        private static byte[] ToDibBytes(Bitmap src)
        {
            using (Bitmap b24 = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(b24))
                {
                    g.Clear(Color.White);
                    g.DrawImage(src, 0, 0, src.Width, src.Height);
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    b24.Save(ms, ImageFormat.Bmp);
                    byte[] all = ms.ToArray();
                    byte[] dib = new byte[all.Length - 14];
                    Array.Copy(all, 14, dib, 0, dib.Length);
                    return dib;
                }
            }
        }
    }

    internal class RegionSelectForm : Form
    {
        private Bitmap _shot;
        private Bitmap _dim;
        private Point _start;
        private Rectangle _sel = Rectangle.Empty;
        private bool _dragging;

        public Rectangle Result = Rectangle.Empty;

        public RegionSelectForm(Bitmap shot)
        {
            _shot = shot;
            _dim = MakeDimCopy(shot);

            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = SystemInformation.VirtualScreen;
            this.TopMost = true;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;
            this.KeyPreview = true;
            this.ShowInTaskbar = false;

            this.MouseDown += OnDown;
            this.MouseMove += OnMove;
            this.MouseUp += OnUp;
            this.MouseDoubleClick += OnDouble;
            this.KeyDown += OnKey;
        }

        private static Bitmap MakeDimCopy(Bitmap src)
        {
            Bitmap b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.DrawImage(src, 0, 0);
                using (SolidBrush br = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
                {
                    g.FillRectangle(br, 0, 0, src.Width, src.Height);
                }
            }
            return b;
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            int w = Math.Abs(a.X - b.X);
            int h = Math.Abs(a.Y - b.Y);
            return new Rectangle(x, y, w, h);
        }

        private void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Result = Rectangle.Empty;
                this.Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _start = new Point(e.X, e.Y);
            _sel = Rectangle.Empty;
            _dragging = true;
            Invalidate();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _sel = Normalize(_start, new Point(e.X, e.Y));
            Invalidate();
        }

        private void OnUp(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_sel.Width < 4 || _sel.Height < 4)
            {
                _sel = Rectangle.Empty;
                Invalidate();
                return;
            }
            Result = _sel;
            this.Close();
        }

        private void OnDouble(object sender, MouseEventArgs e)
        {
            Result = new Rectangle(0, 0, this.ClientSize.Width, this.ClientSize.Height);
            this.Close();
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Result = Rectangle.Empty;
                this.Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawImage(_dim, 0, 0);

            if (_sel.Width > 0 && _sel.Height > 0)
            {
                e.Graphics.DrawImage(_shot, _sel, _sel, GraphicsUnit.Pixel);

                using (Pen pen = new Pen(Color.FromArgb(0, 160, 255), 2))
                {
                    e.Graphics.DrawRectangle(pen, _sel);
                }

                string text = _sel.Width + " × " + _sel.Height;
                Font f = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                Size sz = TextRenderer.MeasureText(text, f);
                int bw = sz.Width + 14;
                int bh = sz.Height + 8;
                int bx = _sel.X;
                int by = _sel.Y - bh - 6;
                if (by < 0) by = _sel.Bottom + 6;
                if (bx + bw > this.ClientSize.Width) bx = this.ClientSize.Width - bw - 2;

                using (SolidBrush bg = new SolidBrush(Color.FromArgb(210, 0, 0, 0)))
                {
                    e.Graphics.FillRectangle(bg, bx, by, bw, bh);
                }
                TextRenderer.DrawText(e.Graphics, text, f, new Point(bx + 7, by + 4), Color.White);
                f.Dispose();
            }
            else
            {
                string tip = "拖拽选择区域　·　双击截全屏　·　Esc 取消";
                Font f = new Font("Microsoft YaHei UI", 11F);
                Size sz = TextRenderer.MeasureText(tip, f);
                int bx = (this.ClientSize.Width - sz.Width) / 2;
                int by = (this.ClientSize.Height - sz.Height) / 2;
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                {
                    e.Graphics.FillRectangle(bg, bx - 16, by - 10, sz.Width + 32, sz.Height + 20);
                }
                TextRenderer.DrawText(e.Graphics, tip, f, new Point(bx, by), Color.White);
                f.Dispose();
            }
        }
    }

    internal class Stroke
    {
        public List<PointF> Points = new List<PointF>();
        public Color PenColor = Color.Red;
        public int PenWidth = 4;
        public RectangleF Bounds = RectangleF.Empty;
    }

    internal class AnnotateForm : Form
    {
        private Bitmap _img;
        private readonly List<Stroke> _strokes = new List<Stroke>();
        private Stroke _current;
        private Color _penColor = Color.Red;
        private const int PenW = 4;
        private readonly Panel _bar;
        private bool _closed;
        private double _scale = 1.0;
        private int _canvasW;
        private int _canvasH;

        public Bitmap ResultImage;

        public AnnotateForm(Bitmap image)
        {
            _img = image;

            Rectangle wa = SystemInformation.WorkingArea;
            int maxW = (int)(wa.Width * 0.86);
            int maxH = (int)((wa.Height - 80) * 0.86);
            double s = 1.0;
            if (image.Width > maxW) s = Math.Min(s, (double)maxW / image.Width);
            if (image.Height > maxH) s = Math.Min(s, (double)maxH / image.Height);
            _scale = s;
            _canvasW = Math.Max(60, (int)Math.Round(image.Width * s));
            _canvasH = Math.Max(40, (int)Math.Round(image.Height * s));

            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Text = "标注  " + image.Width + " × " + image.Height;
            this.BackColor = Color.FromArgb(45, 45, 45);
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;
            this.KeyPreview = true;

            _bar = BuildToolbar();
            this.Controls.Add(_bar);
            this.ClientSize = new Size(_canvasW, _canvasH + _bar.Height);

            this.MouseDown += OnDown;
            this.MouseMove += OnMove;
            this.MouseUp += OnUp;
            this.KeyDown += OnKey;
        }

        private Panel BuildToolbar()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 50;
            bar.BackColor = Color.FromArgb(32, 32, 32);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = false;
            flow.Padding = new Padding(8);
            bar.Controls.Add(flow);

            Color[] palette = new Color[] { Color.Red, Color.Yellow, Color.Lime, Color.DeepSkyBlue, Color.White, Color.Black };
            for (int i = 0; i < palette.Length; i++)
            {
                Color c = palette[i];
                Button b = new Button();
                b.Width = 28;
                b.Height = 28;
                b.BackColor = c;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Color.Gray;
                b.Margin = new Padding(2, 3, 2, 3);
                b.TabStop = false;
                b.Click += delegate { _penColor = c; };
                flow.Controls.Add(b);
            }

            flow.Controls.Add(MakeSep());

            Button undo = MakeTextButton("撤销");
            undo.Width = 70;
            undo.Click += delegate
            {
                if (_strokes.Count > 0) { _strokes.RemoveAt(_strokes.Count - 1); Invalidate(); }
            };
            flow.Controls.Add(undo);

            Button clear = MakeTextButton("清空");
            clear.Width = 70;
            clear.Click += delegate { _strokes.Clear(); Invalidate(); };
            flow.Controls.Add(clear);

            Button save = MakeTextButton("保存");
            save.Width = 70;
            save.Click += delegate { SaveToFile(); };
            flow.Controls.Add(save);

            flow.Controls.Add(MakeSep());

            Button ok = MakeTextButton("复制 (Enter)");
            ok.Width = 112;
            ok.BackColor = Color.FromArgb(0, 120, 215);
            ok.Click += delegate { Finish(); };
            flow.Controls.Add(ok);

            Button cancel = MakeTextButton("取消 (Esc)");
            cancel.Width = 100;
            cancel.Click += delegate { CancelAll(); };
            flow.Controls.Add(cancel);

            return bar;
        }

        private static Control MakeSep()
        {
            Label l = new Label();
            l.Text = "|";
            l.ForeColor = Color.FromArgb(90, 90, 90);
            l.AutoSize = true;
            l.Margin = new Padding(8, 10, 8, 4);
            return l;
        }

        private static Button MakeTextButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = false;
            b.Width = 90;
            b.Height = 28;
            b.FlatStyle = FlatStyle.Flat;
            b.ForeColor = Color.White;
            b.BackColor = Color.FromArgb(58, 58, 58);
            b.Margin = new Padding(3);
            b.TabStop = false;
            return b;
        }

        private PointF ToImageF(Point p)
        {
            if (_scale <= 0) return new PointF(p.X, p.Y);
            return new PointF((float)(p.X / _scale), (float)(p.Y / _scale));
        }

        private static RectangleF RectAround(PointF p, float r)
        {
            return new RectangleF(p.X - r, p.Y - r, r * 2, r * 2);
        }

        private Rectangle CanvasRect(RectangleF img)
        {
            float pad = (float)(PenW * _scale) + 6f;
            return Rectangle.FromLTRB(
                (int)Math.Floor(img.Left * _scale - pad),
                (int)Math.Floor(img.Top * _scale - pad),
                (int)Math.Ceiling(img.Right * _scale + pad),
                (int)Math.Ceiling(img.Bottom * _scale + pad));
        }

        internal static void DrawStroke(Graphics g, Stroke s, float scale)
        {
            if (s.Points.Count == 0) return;

            float pw = Math.Max(1f, s.PenWidth * scale);
            List<PointF> pts = new List<PointF>(s.Points.Count);
            for (int i = 0; i < s.Points.Count; i++)
            {
                PointF p = new PointF(s.Points[i].X * scale, s.Points[i].Y * scale);
                if (pts.Count > 0)
                {
                    PointF q = pts[pts.Count - 1];
                    if (Math.Abs(p.X - q.X) < 0.05f && Math.Abs(p.Y - q.Y) < 0.05f) continue;
                }
                pts.Add(p);
            }
            if (pts.Count == 0) return;

            if (pts.Count == 1)
            {
                float r = pw / 2f;
                using (SolidBrush br = new SolidBrush(s.PenColor))
                {
                    g.FillEllipse(br, pts[0].X - r, pts[0].Y - r, pw, pw);
                }
                return;
            }

            if (pts.Count == 2)
            {
                using (Pen p2 = new Pen(s.PenColor, pw))
                {
                    p2.StartCap = LineCap.Round;
                    p2.EndCap = LineCap.Round;
                    g.DrawLine(p2, pts[0], pts[1]);
                }
                return;
            }

            int iterations = pts.Count > 1500 ? 1 : 2;
            List<PointF> smooth = Chaikin(pts, iterations);

            using (Pen pen = new Pen(s.PenColor, pw))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLines(pen, smooth.ToArray());
            }
        }

        private static List<PointF> Chaikin(List<PointF> src, int iterations)
        {
            List<PointF> pts = src;
            for (int it = 0; it < iterations && pts.Count > 2; it++)
            {
                List<PointF> dst = new List<PointF>(pts.Count * 2);
                dst.Add(pts[0]);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    PointF a = pts[i];
                    PointF b = pts[i + 1];
                    dst.Add(new PointF(0.75f * a.X + 0.25f * b.X, 0.75f * a.Y + 0.25f * b.Y));
                    dst.Add(new PointF(0.25f * a.X + 0.75f * b.X, 0.25f * a.Y + 0.75f * b.Y));
                }
                dst.Add(pts[pts.Count - 1]);
                pts = dst;
            }
            return pts;
        }

        private void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                CancelAll();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (e.Y >= _canvasH) return;

            _current = new Stroke();
            _current.PenColor = _penColor;
            _current.PenWidth = PenW;
            PointF p0 = ToImageF(new Point(e.X, e.Y));
            _current.Points.Add(p0);
            _current.Bounds = RectAround(p0, (float)PenW / 2f);
            _strokes.Add(_current);
            Invalidate(CanvasRect(_current.Bounds));
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_current == null) return;
            if (e.Button != MouseButtons.Left) return;
            if (e.Y >= _canvasH) return;

            PointF target = ToImageF(new Point(e.X, e.Y));
            PointF last = _current.Points[_current.Points.Count - 1];
            float dx = target.X - last.X;
            float dy = target.Y - last.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist < 0.6f) return;

            int steps = (int)Math.Min(80, Math.Max(1, Math.Ceiling(dist / 2.0)));
            RectangleF bounds = _current.Bounds;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                PointF p = new PointF(last.X + dx * t, last.Y + dy * t);
                _current.Points.Add(p);
                bounds = RectangleF.Union(bounds, RectAround(p, (float)PenW / 2f));
            }
            _current.Bounds = bounds;
            Invalidate(CanvasRect(bounds));
        }

        private void OnUp(object sender, MouseEventArgs e)
        {
            _current = null;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { CancelAll(); }
            else if (e.KeyCode == Keys.Enter) { Finish(); }
            else if (e.Control && e.KeyCode == Keys.Z)
            {
                if (_strokes.Count > 0) { _strokes.RemoveAt(_strokes.Count - 1); Invalidate(); }
            }
            else if (e.Control && e.KeyCode == Keys.S) { SaveToFile(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_img != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(_img, 0, 0, _canvasW, _canvasH);
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            for (int i = 0; i < _strokes.Count; i++)
            {
                DrawStroke(e.Graphics, _strokes[i], (float)_scale);
            }
        }

        private Bitmap Compose()
        {
            Bitmap result = new Bitmap(_img.Width, _img.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(_img, 0, 0);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                for (int i = 0; i < _strokes.Count; i++)
                {
                    DrawStroke(g, _strokes[i], 1.0f);
                }
            }
            return result;
        }

        private void SaveToFile()
        {
            Bitmap img = Compose();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            string full = Path.Combine(dir, "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            try
            {
                img.Save(full, ImageFormat.Png);
                MessageBox.Show("已保存到：\r\n" + full, "截图工具", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败: " + ex.Message, "截图工具");
            }
            finally
            {
                img.Dispose();
            }
        }

        public void Finish()
        {
            if (_closed) return;
            _closed = true;
            ResultImage = Compose();
            this.Close();
        }

        private void CancelAll()
        {
            if (_closed) return;
            _closed = true;
            ResultImage = null;
            this.Close();
        }
    }

    internal class MainForm : Form
    {
        private NotifyIcon _tray;
        private Button _btnHotkey;
        private CheckBox _chkStay;
        private CheckBox _chkAutoStart;
        private Label _lblStatus;
        private bool _busy;
        private bool _recording;

        public MainForm()
        {
            this.Text = "截图工具";
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(478, 322);
            this.BackColor = Color.FromArgb(246, 246, 246);
            this.Font = new Font("Microsoft YaHei UI", 9F);
            this.KeyPreview = true;

            BuildUi();
            BuildTray();

            Config.Load();
            _chkStay.Checked = Config.Stay;
            _chkAutoStart.Checked = AutoStart.IsEnabled();

            RegisterCurrentHotkey();
            RefreshHotkeyButton();
            UpdateModeStatus();

            this.KeyDown += OnMainKeyDown;
        }

        private void BuildUi()
        {
            Label title = new Label();
            title.Text = "截图工具";
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            title.Location = new Point(20, 14);
            title.AutoSize = true;
            this.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "按热键 → 拖拽框选区域 → 结果进剪贴板，Ctrl+V 粘贴";
            sub.ForeColor = Color.FromArgb(110, 110, 110);
            sub.Location = new Point(22, 48);
            sub.AutoSize = true;
            this.Controls.Add(sub);

            GroupBox gbKey = new GroupBox();
            gbKey.Text = "截屏按键";
            gbKey.Location = new Point(20, 80);
            gbKey.Size = new Size(438, 76);
            this.Controls.Add(gbKey);

            _btnHotkey = new Button();
            _btnHotkey.Location = new Point(14, 26);
            _btnHotkey.Size = new Size(210, 34);
            _btnHotkey.FlatStyle = FlatStyle.Flat;
            _btnHotkey.BackColor = Color.White;
            _btnHotkey.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            _btnHotkey.Text = "Ctrl + Alt + Q";
            _btnHotkey.Click += delegate { StartRecording(); };
            gbKey.Controls.Add(_btnHotkey);

            Label tipKey = new Label();
            tipKey.Text = "点一下，再按你想用的键";
            tipKey.Font = new Font("Microsoft YaHei UI", 8.5F);
            tipKey.ForeColor = Color.FromArgb(130, 130, 130);
            tipKey.Location = new Point(236, 36);
            tipKey.AutoSize = true;
            gbKey.Controls.Add(tipKey);

            GroupBox gbMode = new GroupBox();
            gbMode.Text = "截图完成后";
            gbMode.Location = new Point(20, 166);
            gbMode.Size = new Size(438, 82);
            this.Controls.Add(gbMode);

            _chkStay = new CheckBox();
            _chkStay.Text = "停留在编辑窗口（可以画笔标注）";
            _chkStay.Location = new Point(14, 26);
            _chkStay.AutoSize = true;
            _chkStay.CheckedChanged += delegate
            {
                Config.Stay = _chkStay.Checked;
                Config.Save();
                UpdateModeStatus();
            };
            gbMode.Controls.Add(_chkStay);

            Label tipMode = new Label();
            tipMode.Text = "不勾（默认）= 截完直接进剪贴板，不弹窗";
            tipMode.Font = new Font("Microsoft YaHei UI", 8.5F);
            tipMode.ForeColor = Color.FromArgb(130, 130, 130);
            tipMode.Location = new Point(34, 52);
            tipMode.AutoSize = true;
            gbMode.Controls.Add(tipMode);

            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "开机自动启动";
            _chkAutoStart.Location = new Point(24, 262);
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.CheckedChanged += delegate
            {
                try
                {
                    AutoStart.Set(_chkAutoStart.Checked);
                    _lblStatus.Text = _chkAutoStart.Checked ? "已设置开机自启动" : "已取消开机自启动";
                }
                catch (Exception ex)
                {
                    _chkAutoStart.Checked = AutoStart.IsEnabled();
                    MessageBox.Show("设置开机自启失败: " + ex.Message, "截图工具");
                }
            };
            this.Controls.Add(_chkAutoStart);

            Button btnShot = new Button();
            btnShot.Text = "立即截一张";
            btnShot.Location = new Point(334, 256);
            btnShot.Size = new Size(124, 32);
            btnShot.FlatStyle = FlatStyle.Flat;
            btnShot.BackColor = Color.FromArgb(0, 120, 215);
            btnShot.ForeColor = Color.White;
            btnShot.Click += delegate { TriggerCapture(); };
            this.Controls.Add(btnShot);

            _lblStatus = new Label();
            _lblStatus.Location = new Point(24, 296);
            _lblStatus.AutoSize = true;
            _lblStatus.ForeColor = Color.FromArgb(90, 90, 90);
            this.Controls.Add(_lblStatus);
        }

        private static Icon ExtractAppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return SystemIcons.Application; }
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = ExtractAppIcon();
            _tray.Text = "截图工具";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowMain(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开设置", null, delegate { ShowMain(); });
            menu.Items.Add("立即截图", null, delegate { TriggerCapture(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate
            {
                _tray.Visible = false;
                try { Native.UnregisterHotKey(this.Handle, Native.HOTKEY_ID); } catch { }
                Application.Exit();
            });
            _tray.ContextMenuStrip = menu;
        }

        private void ShowMain()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        private void UpdateModeStatus()
        {
            if (_lblStatus == null) return;
            if (_recording) return;
            _lblStatus.Text = Config.Stay ? "当前：停留编辑模式" : "当前：直接进剪贴板（不弹窗）";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                _tray.ShowBalloonTip(1500, "截图工具", "已缩到托盘，热键仍然可用", ToolTipIcon.Info);
                return;
            }
            base.OnFormClosing(e);
        }

        private void RefreshHotkeyButton()
        {
            _btnHotkey.Text = HotkeyText.Describe(Config.HotMods, Config.HotVk);
        }

        private void StartRecording()
        {
            _recording = true;
            _btnHotkey.Text = "请按下组合键…";
            _lblStatus.Text = "按 Esc 取消录入";
            _btnHotkey.Focus();
        }

        private void OnMainKeyDown(object sender, KeyEventArgs e)
        {
            if (!_recording) return;

            if (e.KeyCode == Keys.Escape)
            {
                _recording = false;
                RefreshHotkeyButton();
                UpdateModeStatus();
                return;
            }

            if (HotkeyText.IsModifierKey(e.KeyCode))
            {
                return;
            }

            uint mods = 0;
            Keys mk = Control.ModifierKeys;
            if ((mk & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
            if ((mk & Keys.Alt) != 0) mods |= Native.MOD_ALT;
            if ((mk & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
            uint vk = (uint)e.KeyCode;

            if (!IsAcceptableHotkey(mods, vk))
            {
                _lblStatus.Text = "无 Ctrl/Alt/Shift 时只能用 F1-F12、PrintScreen 等，请重按";
                return;
            }

            uint oldMods = Config.HotMods;
            uint oldVk = Config.HotVk;
            Config.HotMods = mods;
            Config.HotVk = vk;

            bool ok = TryRegister(mods, vk);
            if (!ok)
            {
                Config.HotMods = oldMods;
                Config.HotVk = oldVk;
                TryRegister(oldMods, oldVk);
                _recording = false;
                RefreshHotkeyButton();
                _lblStatus.Text = "这个组合键被别的程序占用了，换一个";
                return;
            }

            Config.Save();
            _recording = false;
            RefreshHotkeyButton();
            _lblStatus.Text = "热键已生效：" + HotkeyText.Describe(Config.HotMods, Config.HotVk);
        }

        private static bool IsAcceptableHotkey(uint mods, uint vk)
        {
            if (mods != 0) return true;
            if (vk >= 0x70 && vk <= 0x87) return true;
            if (vk == 0x2C || vk == 0x13 || vk == 0x91) return true;
            return false;
        }

        private bool TryRegister(uint mods, uint vk)
        {
            try { Native.UnregisterHotKey(this.Handle, Native.HOTKEY_ID); } catch { }
            return Native.RegisterHotKey(this.Handle, Native.HOTKEY_ID, mods, vk);
        }

        private void RegisterCurrentHotkey()
        {
            bool ok = TryRegister(Config.HotMods, Config.HotVk);
            if (_lblStatus != null)
            {
                _lblStatus.Text = ok
                    ? ("热键已生效：" + HotkeyText.Describe(Config.HotMods, Config.HotVk))
                    : "热键注册失败，请重新设置";
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == Native.HOTKEY_ID)
            {
                TriggerCapture();
                return;
            }
            base.WndProc(ref m);
        }

        private void TriggerCapture()
        {
            if (_busy) return;
            _busy = true;
            bool mainWasVisible = this.Visible;
            try
            {
                if (mainWasVisible) this.Hide();
                Thread.Sleep(120);

                Rectangle vs = SystemInformation.VirtualScreen;
                Bitmap shot = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(shot))
                {
                    g.CopyFromScreen(vs.X, vs.Y, 0, 0, shot.Size, CopyPixelOperation.SourceCopy);
                }

                Rectangle region = Rectangle.Empty;
                using (RegionSelectForm rf = new RegionSelectForm(shot))
                {
                    rf.ShowDialog();
                    region = rf.Result;
                }

                if (region.Width < 4 || region.Height < 4)
                {
                    shot.Dispose();
                    return;
                }

                Bitmap crop = shot.Clone(region, PixelFormat.Format32bppArgb);
                shot.Dispose();

                if (!Config.Stay)
                {
                    DeliverToClipboard(crop, crop.Width + " × " + crop.Height + " 已复制，Ctrl+V 粘贴");
                    crop.Dispose();
                }
                else
                {
                    using (AnnotateForm af = new AnnotateForm(crop))
                    {
                        af.ShowDialog();
                        if (af.ResultImage != null)
                        {
                            DeliverToClipboard(af.ResultImage, "标注结果已复制到剪贴板");
                        }
                    }
                    crop.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("截图失败：" + ex.Message, "截图工具");
            }
            finally
            {
                if (mainWasVisible) ShowMain();
                _busy = false;
            }
        }

        private void DeliverToClipboard(Bitmap bmp, string statusText)
        {
            try
            {
                ClipboardUtil.SetBitmap(bmp);
                _lblStatus.Text = statusText;
                _tray.ShowBalloonTip(1200, "截图完成", statusText, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show("写入剪贴板失败：" + ex.Message, "截图工具");
            }
        }
    }

    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SnapTool";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(ValueName);
                    return v != null && v.ToString().Length > 0;
                }
            }
            catch { return false; }
        }

        public static void Set(bool enable)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) throw new Exception("无法打开注册表启动项");
                if (enable)
                {
                    k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                }
                else
                {
                    k.DeleteValue(ValueName, false);
                }
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            try { Application.OleRequired(); } catch { }

            if (args.Length > 0 && args[0] == "--selftest")
            {
                RunSelfTest();
                return;
            }

            if (args.Length > 0 && args[0] == "--annotatedemo")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                RunAnnotateDemo();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static void RunAnnotateDemo()
        {
            Bitmap demo = new Bitmap(760, 420, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(demo))
            {
                g.Clear(Color.WhiteSmoke);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Stroke s1 = new Stroke();
                s1.PenColor = Color.FromArgb(220, 30, 30);
                s1.PenWidth = 4;
                for (int i = 0; i <= 320; i++)
                {
                    float t = i / 320f;
                    s1.Points.Add(new PointF(60 + i * 2f, 210 + (float)(Math.Sin(t * 6.28f * 2.0f) * 80)));
                }
                AnnotateForm.DrawStroke(g, s1, 1f);

                Stroke s2 = new Stroke();
                s2.PenColor = Color.DeepSkyBlue;
                s2.PenWidth = 4;
                for (int i = 0; i <= 300; i++)
                {
                    float t = i / 300f;
                    s2.Points.Add(new PointF(80 + i * 2f, 330 - (float)(Math.Sin(t * 6.28f) * 30)));
                }
                AnnotateForm.DrawStroke(g, s2, 1f);
            }

            Application.Run(new AnnotateForm(demo));
        }

        private static void RunSelfTest()
        {
            string log = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "selftest.log");
            List<string> lines = new List<string>();
            try
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                lines.Add("VirtualScreen=" + vs.X + "," + vs.Y + "," + vs.Width + "," + vs.Height);

                Bitmap shot = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(shot))
                {
                    g.CopyFromScreen(vs.X, vs.Y, 0, 0, shot.Size, CopyPixelOperation.SourceCopy);
                }
                lines.Add("Captured=" + shot.Width + "x" + shot.Height);

                Rectangle region = new Rectangle(vs.Width / 4, vs.Height / 4, vs.Width / 2, vs.Height / 2);
                Bitmap crop = shot.Clone(region, PixelFormat.Format32bppArgb);
                lines.Add("Crop=" + crop.Width + "x" + crop.Height);

                Bitmap composed = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(composed))
                {
                    g.DrawImage(crop, 0, 0);
                    using (Pen pen = new Pen(Color.Red, 6))
                    {
                        g.DrawLine(pen, 10, 10, crop.Width / 2, crop.Height / 2);
                    }
                }
                lines.Add("Compose=OK");

                ClipboardUtil.SetBitmap(composed);
                lines.Add("Clipboard=OK");

                string outFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "selftest_capture.png");
                composed.Save(outFile, ImageFormat.Png);
                lines.Add("SavedPng=" + outFile);

                lines.Add("HotkeyDefault=" + HotkeyText.Describe(Config.HotMods, Config.HotVk));

                Bitmap ink = new Bitmap(420, 220, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(ink))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Stroke sk = new Stroke();
                    sk.PenColor = Color.FromArgb(220, 30, 30);
                    sk.PenWidth = 4;
                    for (int i = 0; i <= 12; i++)
                    {
                        float t = i / 12f;
                        sk.Points.Add(new PointF(20 + t * 380, 110 + (float)(Math.Sin(t * 6.28f * 1.5f) * 70)));
                    }
                    AnnotateForm.DrawStroke(g, sk, 1f);
                }
                string inkFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "selftest_ink.png");
                ink.Save(inkFile, ImageFormat.Png);
                ink.Dispose();
                lines.Add("InkSample=" + inkFile);

                lines.Add("RESULT=PASS");

                shot.Dispose();
                crop.Dispose();
                composed.Dispose();
            }
            catch (Exception ex)
            {
                lines.Add("RESULT=FAIL");
                lines.Add("Error=" + ex.Message);
            }
            try { File.WriteAllLines(log, lines.ToArray()); } catch { }
        }
    }
}
