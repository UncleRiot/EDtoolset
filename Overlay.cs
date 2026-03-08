using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace EDtoolset
{
    public class Overlay : Form
    {
        private readonly System.Windows.Forms.Timer timer;
        private bool dragging = false;
        private Point dragCursorPoint;
        private Point dragFormPoint;

        private string jumpsText = "Jumps: -";
        private List<string> starLines = new List<string>();

        private readonly string navRoutePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            @"Saved Games\Frontier Developments\Elite Dangerous\NavRoute.json");

        public Overlay()
        {
            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(50, 50);
            Width = 190;
            Height = 110;
            DoubleBuffered = true;

            ShowInTaskbar = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            BackColor = Color.Lime;
            TransparencyKey = Color.Lime;

            MouseDown += StartDrag;
            MouseMove += DoDrag;
            MouseUp += EndDrag;
            DoubleClick += CloseOverlay;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 2000;
            timer.Tick += (s, e) => UpdateRouteInfo();
            timer.Start();

            UpdateRouteInfo();
        }

        private void CloseOverlay(object? sender, EventArgs e)
        {
            Close();
        }

        private void StartDrag(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            dragging = true;
            dragCursorPoint = Cursor.Position;
            dragFormPoint = Location;
        }

        private void DoDrag(object? sender, MouseEventArgs e)
        {
            if (!dragging) return;

            Point diff = Point.Subtract(Cursor.Position, new Size(dragCursorPoint));
            Location = Point.Add(dragFormPoint, new Size(diff));
        }

        private void EndDrag(object? sender, MouseEventArgs e)
        {
            dragging = false;
        }

        private bool IsScoopable(string starClass)
        {
            string sc = (starClass ?? "").ToUpperInvariant();
            return sc.StartsWith("O") ||
                   sc.StartsWith("B") ||
                   sc.StartsWith("A") ||
                   sc.StartsWith("F") ||
                   sc.StartsWith("G") ||
                   sc.StartsWith("K") ||
                   sc.StartsWith("M");
        }

        private void UpdateRouteInfo()
        {
            try
            {
                if (!File.Exists(navRoutePath))
                {
                    jumpsText = "Jumps: -";
                    starLines.Clear();
                    Height = 40;
                    Invalidate();
                    return;
                }

                string json = File.ReadAllText(navRoutePath);
                using JsonDocument doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("Route", out JsonElement route) ||
                    route.ValueKind != JsonValueKind.Array)
                {
                    jumpsText = "Jumps: -";
                    starLines.Clear();
                    Height = 40;
                    Invalidate();
                    return;
                }

                int routeCount = route.GetArrayLength();
                int jumpsRemaining = Math.Max(0, routeCount - 1);
                jumpsText = $"Jumps: {jumpsRemaining}";

                starLines.Clear();

                // Hier weird die Anzahl an Sternen zur Anzeige festgelegt
                // Alle
                // for (int i = 1; i < routeCount; i++) 
                for (int i = 1; i < Math.Min(routeCount, 6); i++)
                {
                    JsonElement entry = route[i];
                    string starClass = "?";

                    if (entry.TryGetProperty("StarClass", out JsonElement starClassElement))
                    {
                        starClass = starClassElement.GetString() ?? "?";
                    }

                    string marker = IsScoopable(starClass) ? " •" : "";
                    starLines.Add($"{i}. {starClass}{marker}");
                }

                Height = Math.Max(40, 28 + (starLines.Count * 16) + 8);
            }
            catch
            {
                jumpsText = "Jumps: -";
                starLines.Clear();
                Height = 40;
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            using Brush brush = new SolidBrush(Color.FromArgb(220, 110, 0));
            using Font jumpsFont = new Font("Segoe UI", 11, FontStyle.Bold);
            using Font listFont = new Font("Segoe UI", 9, FontStyle.Regular);

            float y = 2f;
            e.Graphics.DrawString(jumpsText, jumpsFont, brush, 4f, y);

            y += 18f;

            foreach (string line in starLines)
            {
                e.Graphics.DrawString(line, listFont, brush, 4f, y);
                y += 15f;
            }
        }
    }
}