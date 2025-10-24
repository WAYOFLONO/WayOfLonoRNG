using System;
using System.Diagnostics;
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

        // Drag + click detection (robust)
        private bool _dragging = false;
        private Point _dragOffset;
        private Stopwatch _downTimer = new Stopwatch();
        private const int ClickMoveThreshold = 6;
        private const int ClickTimeThresholdMs = 250;

        private ContextMenuStrip? _menu;

        private enum ChipSize { Small, Medium, Large }
        private ChipSize _size = ChipSize.Small; // default small

        public MainForm()
        {
            // Load the icon from embedded resources for a true single-file app.
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("RandomChipRNG.icon.ico");
                if (stream != null) this.Icon = new Icon(stream);
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
            _menu.Items.Add("Copy Number", null, (_, __) => Clipboard.SetText(_value));
            _menu.Items.Add(new ToolStripSeparator());

            var sizeMenu = new ToolStripMenuItem("Size");
            sizeMenu.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("Small (180px)",  null, (_, __) => SetSize(ChipSize.Small)),
                new ToolStripMenuItem("Medium (220px)", null, (_, __) => SetSize(ChipSize.Medium)),
                new ToolStripMenuItem("Large (260px)",  null, (_, __) => SetSize(ChipSize.Large)),
            });
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
            UpdateCircularRegion();
            UpdateSizeChecks();
            Invalidate();
        }

        private void UpdateSizeChecks()
        {
            if (_menu == null) return;
            foreach (ToolStripItem item in _menu.Items)
            {
                if (item is ToolStripMenuItem mi && mi.Text == "Size")
                {
                    foreach (ToolStripItem sub in mi.DropDownItems)
                    {
                        if (sub is ToolStripMenuItem si)
                        {
                            si.Checked = (si.Text.StartsWith("Small")  && _size == ChipSize.Small)
                                      || (si.Text.StartsWith("Medium") && _size == ChipSize.Medium)
                                      || (si.Text.StartsWith("Large")  && _size == ChipSize.Large);
                        }
                    }
                }
            }
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
            Region = new Region(gp);
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

        // --- Mouse handlers (robust drag + click) ---
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOffset = new Point(e.X, e.Y);
                _downTimer.Restart();
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                // If the mouse has moved beyond the threshold, it's definitely a drag, not a click.
                if (Math.Abs(e.X - _dragOffset.X) > ClickMoveThreshold || Math.Abs(e.Y - _dragOffset.Y) > ClickMoveThreshold)
                {
                    _downTimer.Stop(); // Stop the timer to indicate it's not a click
                }

                var screen = MousePosition;
                Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                Capture = false;

                // A click is when the mouse is released quickly and hasn't moved far.
                bool isClick = _downTimer.IsRunning && _downTimer.ElapsedMilliseconds <= ClickTimeThresholdMs;
                if (isClick) { Roll(); Invalidate(); }
            }
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

            // Number & brand
            using var numberFont = new Font("Segoe UI", _size == ChipSize.Small ? 34 : _size == ChipSize.Medium ? 40 : 46, FontStyle.Bold);
            var numSize = g.MeasureString(_value, numberFont);
            float numberY = center.Y - numSize.Height * 0.62f;
            g.DrawString(_value, numberFont, brushRed, center.X - numSize.Width / 2f, numberY);

            const string brand = "WAYOFLONO";
            using var brandFont = new Font("Consolas", _size == ChipSize.Small ? 11 : _size == ChipSize.Medium ? 13 : 14, FontStyle.Bold);
            var brandSize = g.MeasureString(brand, brandFont);
            float brandY = numberY + numSize.Height - brandSize.Height * 0.2f;
            g.DrawString(brand, brandFont, brushRed, center.X - brandSize.Width / 2f, brandY);
        }
    }
}
