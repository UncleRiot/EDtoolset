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
        private readonly List<string> starLines = new List<string>();

        private const int MaxStarsShown = 5;

        private readonly string eliteFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            @"Saved Games\Frontier Developments\Elite Dangerous");

        private readonly string navRoutePath;

        private string currentSystem = "";
        private long? currentSystemAddress = null;

        private string currentJournalFile = "";
        private long currentJournalPosition = 0;

        private DateTime lastNavRouteWriteUtc = DateTime.MinValue;
        private readonly List<RouteEntry> routeEntries = new List<RouteEntry>();

        private sealed class RouteEntry
        {
            public string StarSystem { get; set; } = "";
            public long? SystemAddress { get; set; }
            public string StarClass { get; set; } = "?";
        }

        public Overlay()
        {
            navRoutePath = Path.Combine(eliteFolderPath, "NavRoute.json");

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
            timer.Interval = 1000;
            timer.Tick += (s, e) => RefreshOverlay();
            timer.Start();

            InitializeJournalReader();
            LoadNavRouteIfChanged(force: true);
            RefreshOverlay();
        }

        private void RefreshOverlay()
        {
            ReadJournalIncremental();
            LoadNavRouteIfChanged(force: false);
            UpdateOverlayText();
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
            return sc.StartsWith("K") ||
                   sc.StartsWith("G") ||
                   sc.StartsWith("B") ||
                   sc.StartsWith("F") ||
                   sc.StartsWith("O") ||
                   sc.StartsWith("A") ||
                   sc.StartsWith("M");
        }

        private void InitializeJournalReader()
        {
            try
            {
                if (!Directory.Exists(eliteFolderPath))
                    return;

                string[] journalFiles = Directory.GetFiles(eliteFolderPath, "Journal.*.log");
                if (journalFiles.Length == 0)
                    return;

                Array.Sort(journalFiles, StringComparer.Ordinal);
                currentJournalFile = journalFiles[journalFiles.Length - 1];
                currentJournalPosition = 0;

                ReadJournalFull(currentJournalFile);
                currentJournalPosition = new FileInfo(currentJournalFile).Length;
            }
            catch
            {
                currentJournalFile = "";
                currentJournalPosition = 0;
            }
        }

        private void ReadJournalIncremental()
        {
            try
            {
                if (!Directory.Exists(eliteFolderPath))
                    return;

                string[] journalFiles = Directory.GetFiles(eliteFolderPath, "Journal.*.log");
                if (journalFiles.Length == 0)
                    return;

                Array.Sort(journalFiles, StringComparer.Ordinal);
                string newestJournal = journalFiles[journalFiles.Length - 1];

                if (!string.Equals(newestJournal, currentJournalFile, StringComparison.OrdinalIgnoreCase))
                {
                    currentJournalFile = newestJournal;
                    currentJournalPosition = 0;
                }

                if (!File.Exists(currentJournalFile))
                    return;

                using FileStream fs = new FileStream(
                    currentJournalFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);

                if (currentJournalPosition > fs.Length)
                    currentJournalPosition = 0;

                fs.Seek(currentJournalPosition, SeekOrigin.Begin);

                using StreamReader reader = new StreamReader(fs);
                while (!reader.EndOfStream)
                {
                    string? line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        ProcessJournalLine(line);
                }

                currentJournalPosition = fs.Position;
            }
            catch
            {
                // ignorieren
            }
        }

        private void ReadJournalFull(string journalFile)
        {
            try
            {
                using FileStream fs = new FileStream(
                    journalFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);

                using StreamReader reader = new StreamReader(fs);
                while (!reader.EndOfStream)
                {
                    string? line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        ProcessJournalLine(line);
                }
            }
            catch
            {
                // ignorieren
            }
        }

        private void ProcessJournalLine(string line)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("event", out JsonElement eventElement))
                    return;

                string? eventName = eventElement.GetString();
                if (string.IsNullOrWhiteSpace(eventName))
                    return;

                if (eventName == "Location" || eventName == "FSDJump")
                {
                    if (root.TryGetProperty("StarSystem", out JsonElement systemElement))
                        currentSystem = systemElement.GetString() ?? "";
                    else
                        currentSystem = "";

                    if (root.TryGetProperty("SystemAddress", out JsonElement addressElement) &&
                        addressElement.ValueKind == JsonValueKind.Number)
                        currentSystemAddress = addressElement.GetInt64();
                    else
                        currentSystemAddress = null;
                }
                else if (eventName == "NavRoute")
                {
                    LoadNavRouteIfChanged(force: true);
                }
            }
            catch
            {
                // defekte Zeile ignorieren
            }
        }

        private void LoadNavRouteIfChanged(bool force)
        {
            try
            {
                if (!File.Exists(navRoutePath))
                {
                    routeEntries.Clear();
                    lastNavRouteWriteUtc = DateTime.MinValue;
                    return;
                }

                DateTime writeUtc = File.GetLastWriteTimeUtc(navRoutePath);
                if (!force && writeUtc == lastNavRouteWriteUtc)
                    return;

                string json = File.ReadAllText(navRoutePath);
                using JsonDocument doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("Route", out JsonElement route) ||
                    route.ValueKind != JsonValueKind.Array)
                {
                    routeEntries.Clear();
                    lastNavRouteWriteUtc = writeUtc;
                    return;
                }

                routeEntries.Clear();

                foreach (JsonElement entry in route.EnumerateArray())
                {
                    RouteEntry routeEntry = new RouteEntry();

                    if (entry.TryGetProperty("StarSystem", out JsonElement systemElement))
                        routeEntry.StarSystem = systemElement.GetString() ?? "";

                    if (entry.TryGetProperty("SystemAddress", out JsonElement addressElement) &&
                        addressElement.ValueKind == JsonValueKind.Number)
                        routeEntry.SystemAddress = addressElement.GetInt64();

                    if (entry.TryGetProperty("StarClass", out JsonElement starClassElement))
                        routeEntry.StarClass = starClassElement.GetString() ?? "?";

                    routeEntries.Add(routeEntry);
                }

                lastNavRouteWriteUtc = writeUtc;
            }
            catch
            {
                routeEntries.Clear();
            }
        }

        private int FindCurrentRouteIndex()
        {
            if (routeEntries.Count == 0)
                return -1;

            if (currentSystemAddress.HasValue)
            {
                for (int i = 0; i < routeEntries.Count; i++)
                {
                    if (routeEntries[i].SystemAddress.HasValue &&
                        routeEntries[i].SystemAddress.Value == currentSystemAddress.Value)
                        return i;
                }
            }

            if (!string.IsNullOrWhiteSpace(currentSystem))
            {
                for (int i = 0; i < routeEntries.Count; i++)
                {
                    if (string.Equals(routeEntries[i].StarSystem, currentSystem, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            return -1;
        }

        private void UpdateOverlayText()
        {
            try
            {
                starLines.Clear();

                if (routeEntries.Count == 0)
                {
                    jumpsText = "Jumps: -";
                    Height = 40;
                    Invalidate();
                    return;
                }

                int currentIndex = FindCurrentRouteIndex();

                if (currentIndex < 0)
                {
                    jumpsText = "Jumps: ?";
                    int previewCount = Math.Min(routeEntries.Count, MaxStarsShown);

                    for (int i = 0; i < previewCount; i++)
                    {
                        string marker = IsScoopable(routeEntries[i].StarClass) ? " •" : "";
                        starLines.Add($"{i + 1}. {routeEntries[i].StarClass}{marker}");
                    }

                    Height = Math.Max(40, 28 + ((starLines.Count + 3) * 16) + 8);
                    Invalidate();
                    return;
                }

                int jumpsRemaining = Math.Max(0, routeEntries.Count - currentIndex - 1);
                jumpsText = $"Jumps: {jumpsRemaining}";

                int firstNextJumpIndex = currentIndex + 1;
                int lastIndexExclusive = Math.Min(routeEntries.Count, firstNextJumpIndex + MaxStarsShown);

                for (int i = firstNextJumpIndex; i < lastIndexExclusive; i++)
                {
                    string marker = IsScoopable(routeEntries[i].StarClass) ? " •" : "";
                    int displayNumber = i - firstNextJumpIndex + 1;
                    starLines.Add($"{displayNumber}. {routeEntries[i].StarClass}{marker}");
                }

                Height = Math.Max(40, 28 + ((starLines.Count + 3) * 16) + 8);
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

        private string GetRouteAddressText()
        {
            try
            {
                if (routeEntries.Count == 0 || !routeEntries[0].SystemAddress.HasValue)
                    return "-";

                return routeEntries[0].SystemAddress.Value.ToString();
            }
            catch
            {
                return "err";
            }
        }
    }
}