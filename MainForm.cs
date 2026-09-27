using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace RandomChipRNG
{
    public class MainForm : Form
    {
        private readonly Random _rng = new Random();
        private int _min = 0, _max = 100;
        private string _value = "88";

        // Drag + click detection. Positions are tracked in screen coordinates:
        // the window follows the cursor while dragging, so client coordinates
        // barely change and can't be used to tell a drag from a click.
        private bool _mouseDown = false;
        private bool _dragging = false;
        private Point _downScreenPos;
        private Point _downFormPos;
        private const int ClickMoveThreshold = 6;

        private ContextMenuStrip? _menu;
        private readonly Dictionary<ChipSize, ToolStripMenuItem> _sizeItems = new();

        // Segoe UI / Consolas ship with Windows but are often missing elsewhere
        // (e.g. under Wine), so fall back to whatever is installed.
        private static readonly FontFamily NumberFontFamily = PickFont("Segoe UI", "Arial", "Liberation Sans", "DejaVu Sans");
        private static readonly FontFamily BrandFontFamily  = PickFont("Consolas", "Courier New", "Liberation Mono", "DejaVu Sans Mono");

        private static FontFamily PickFont(params string[] names)
        {
            foreach (var name in names)
            {
                var match = FontFamily.Families.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return FontFamily.GenericSansSerif;
        }

        private enum ChipSize { Small, Medium, Large }
        private ChipSize _size = ChipSize.Small; // default small

        public MainForm()
        {
            // Window/taskbar icon. Ask for the largest frame: WinForms derives the
            // small title-bar icon from the same data, and docks (incl. Linux docks
            // under Wine) otherwise upscale a blurry 32px image.
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("RandomChipRNG.icon.ico");
                if (stream != null)
                    Icon = new Icon(stream, 256, 256);
                else if (Environment.ProcessPath is string exe)
                    Icon = Icon.ExtractAssociatedIcon(exe);
            }
            catch { /* Failed to load icon, proceed without it. */ }

            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            BackColor = Color.Black;
            StartPosition = FormStartPosition.Manual;

            ApplySizeDimensions();
            var wa = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(wa.Right - Width - 10, wa.Top + 10);

            BuildMenu();
            Roll();
        }

        private void ApplySizeDimensions()
        {
            int px = _size switch { ChipSize.Small => 180, ChipSize.Medium => 220, _ => 260 };
            // keep even to keep the region symmetric
            if ((px & 1) == 1) px++;
            Width = px; Height = px;
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.Items.Add("Set Range…", null, (_, __) =>
            {
                using var dlg = new RangeDialog(_min, _max);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _min = dlg.MinValue;
                    _max = dlg.MaxValue;
                    Roll(); Invalidate();
                }
            });
            _menu.Items.Add("Copy Number", null, (_, __) =>
            {
                // The clipboard can be locked by another process; don't crash over it.
                try { Clipboard.SetText(_value); }
                catch (System.Runtime.InteropServices.ExternalException) { }
            });
            _menu.Items.Add(new ToolStripSeparator());

            var sizeMenu = new ToolStripMenuItem("Size");
            _sizeItems[ChipSize.Small]  = new ToolStripMenuItem("Small (180px)",  null, (_, __) => SetSize(ChipSize.Small));
            _sizeItems[ChipSize.Medium] = new ToolStripMenuItem("Medium (220px)", null, (_, __) => SetSize(ChipSize.Medium));
            _sizeItems[ChipSize.Large]  = new ToolStripMenuItem("Large (260px)",  null, (_, __) => SetSize(ChipSize.Large));
            sizeMenu.DropDownItems.AddRange(_sizeItems.Values.ToArray());
            _menu.Items.Add(sizeMenu);

            _menu.Items.Add(new ToolStripSeparator());
            var topItem = new ToolStripMenuItem("Always on Top") { Checked = TopMost };
            topItem.Click += (_, __) => { TopMost = !TopMost; topItem.Checked = TopMost; };
            _menu.Items.Add(topItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Exit", null, (_, __) => Application.Exit());

            ContextMenuStrip = _menu;
            UpdateSizeChecks();
        }

        private void SetSize(ChipSize size)
        {
            _size = size;
            var center = new Point(Location.X + Width/2, Location.Y + Height/2);
            ApplySizeDimensions();
            Location = new Point(center.X - Width/2, center.Y - Height/2);
            KeepOnScreen();
            UpdateCircularRegion();
            UpdateSizeChecks();
            Invalidate();
        }

        private void UpdateSizeChecks()
        {
            foreach (var (size, item) in _sizeItems)
                item.Checked = size == _size;
        }

        // Pull the chip back inside the working area of whichever screen it's on
        // (e.g. after growing it near an edge, or dropping it partly off-screen).
        private void KeepOnScreen()
        {
            var wa = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width));
            int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
            Location = new Point(x, y);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateCircularRegion();
            Invalidate();
        }

        private void UpdateCircularRegion()
        {
            int s = Math.Min(Width, Height);
            // Region that matches the drawn outer circle
            var circleRect = new Rectangle((Width - s)/2, (Height - s)/2, s, s);
            using var gp = new GraphicsPath();
            gp.AddEllipse(circleRect);
            var old = Region;
            Region = new Region(gp);
            old?.Dispose();
        }

        // --- Drawing helpers ---
        private static void FillDonut(Graphics g, PointF c, float outerR, float innerR, Brush brush)
        {
            using var gp = new GraphicsPath(FillMode.Alternate);
            var outer = new RectangleF(c.X - outerR, c.Y - outerR, outerR * 2f, outerR * 2f);
            var inner = new RectangleF(c.X - innerR, c.Y - innerR, innerR * 2f, innerR * 2f);
            gp.AddEllipse(outer);
            gp.AddEllipse(inner);
            g.FillPath(brush, gp);
        }

        private static void FillAnnularSector(Graphics g, PointF c, float rInner, float rOuter, float centerDeg, float widthDeg, Brush brush)
        {
            using var gp = new GraphicsPath();
            var outerRect = new RectangleF(c.X - rOuter, c.Y - rOuter, rOuter * 2f, rOuter * 2f);
            var innerRect = new RectangleF(c.X - rInner, c.Y - rInner, rInner * 2f, rInner * 2f);

            float startDeg = centerDeg - widthDeg / 2f;
            float sweepDeg = widthDeg;

            gp.AddArc(outerRect, startDeg, sweepDeg);
            gp.AddArc(innerRect, startDeg + sweepDeg, -sweepDeg);
            gp.CloseFigure();

            g.FillPath(brush, gp);
        }

        // --- Mouse handlers (drag to move, click to roll) ---
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _mouseDown = true;
                _dragging = false;
                _downScreenPos = MousePosition;
                _downFormPos = Location;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_mouseDown) return;

            var screen = MousePosition;
            int dx = screen.X - _downScreenPos.X;
            int dy = screen.Y - _downScreenPos.Y;

            // Don't move until the cursor has clearly left the click zone, so a
            // plain click doesn't jitter the chip around.
            if (!_dragging && (Math.Abs(dx) > ClickMoveThreshold || Math.Abs(dy) > ClickMoveThreshold))
                _dragging = true;

            if (_dragging)
                Location = new Point(_downFormPos.X + dx, _downFormPos.Y + dy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _mouseDown)
            {
                bool wasDrag = _dragging;
                _mouseDown = false;
                _dragging = false;
                Capture = false;

                if (wasDrag) KeepOnScreen();
                else { Roll(); Invalidate(); }
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // Capture lost mid-drag (alt-tab, context menu, etc.): cancel cleanly.
            if (!Capture) { _mouseDown = false; _dragging = false; }
        }

        private void Roll()
        {
            int n = _rng.Next(_min, _max + 1);
            _value = n.ToString();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half; // aligns curves to pixels nicely
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int s = Math.Min(w, h);

            var circleRect = new RectangleF((w - s) / 2f, (h - s) / 2f, s, s);
            var center = new PointF(circleRect.X + circleRect.Width / 2f, circleRect.Y + circleRect.Height / 2f);
            float R = circleRect.Width / 2f;

            // Design constants
            const float OUTER_RIM_W = 4f;
            const float INNER_RING_W = 2f;
            const float INNER_RING_RATIO = 0.72f;
            const float GAP = 3f;
            const int NOTCHES = 8;

            Color red = Color.FromArgb(240, 20, 20);
            using var brushBlack = new SolidBrush(Color.Black);
            using var brushRed = new SolidBrush(red);

            // Base chip and rim (filled shapes)
            g.FillEllipse(brushBlack, circleRect);
            FillDonut(g, center, R, R - OUTER_RIM_W, brushRed);

            // Radii
            float rimInnerRadius  = R - OUTER_RIM_W;
            float ringOuterRadius = R * INNER_RING_RATIO;
            float ringInnerRadius = ringOuterRadius - INNER_RING_W;

            // Notches as annular sectors (exact circular inner edge)
            float notchInner = ringOuterRadius + GAP;
            float notchOuter = rimInnerRadius;
            float desiredLinearWidth = Math.Max(8f, R * 0.085f);
            float notchWidthDeg = 2f * (float)(Math.Asin(Math.Min(0.999f, desiredLinearWidth / (2f * notchOuter))) * 180.0 / Math.PI);

            float startAngle = -90f;
            for (int i = 0; i < NOTCHES; i++)
            {
                float centerDeg = startAngle + i * (360f / NOTCHES);
                FillAnnularSector(g, center, notchInner, notchOuter, centerDeg, notchWidthDeg, brushRed);
            }

            // Inner ring
            FillDonut(g, center, ringOuterRadius, ringInnerRadius, brushRed);

            // Number & brand. Sizes are in pixels (not points) so the text scales
            // with the chip rather than with the Windows DPI setting, and the number
            // shrinks to fit inside the inner ring (e.g. "-100000").
            float innerDiameter = ringInnerRadius * 2f;
            float maxTextWidth = innerDiameter * 0.86f;

            using var fmt = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            };

            float numberPx = R * 0.5f;
            SizeF numSize;
            Font numberFont;
            while (true)
            {
                numberFont = new Font(NumberFontFamily, numberPx, FontStyle.Bold, GraphicsUnit.Pixel);
                numSize = g.MeasureString(_value, numberFont, PointF.Empty, fmt);
                if (numSize.Width <= maxTextWidth || numberPx <= 8f) break;
                numberFont.Dispose();
                numberPx = Math.Max(8f, numberPx * maxTextWidth / numSize.Width * 0.98f);
            }

            const string brand = "WAYOFLONO";
            using var brandFont = new Font(BrandFontFamily, R * 0.16f, FontStyle.Bold, GraphicsUnit.Pixel);
            var brandSize = g.MeasureString(brand, brandFont, PointF.Empty, fmt);
            if (brandSize.Width > maxTextWidth) brandSize = SizeF.Empty; // doesn't fit: skip it

            using (numberFont)
            {
                float gapPx = R * 0.04f;
                float blockH = numSize.Height + (brandSize.IsEmpty ? 0 : gapPx + brandSize.Height);
                float top = center.Y - blockH / 2f;
                g.DrawString(_value, numberFont, brushRed, new PointF(center.X, top + numSize.Height / 2f), fmt);
                if (!brandSize.IsEmpty)
                    g.DrawString(brand, brandFont, brushRed, new PointF(center.X, top + numSize.Height + gapPx + brandSize.Height / 2f), fmt);
            }
        }
    }
}
