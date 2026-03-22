using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using System.Reflection;

namespace EDtoolset
{
    public partial class Overlay : Form
    {
        private readonly System.Windows.Forms.Timer timer;
        private bool dragging = false;
        private Point dragCursorPoint;
        private Point dragFormPoint;

        private string jumpsText = "Jumps: -";
        private readonly List<string> starLines = new List<string>();

        private const int MaxStarsShown = 5;
        private const int MinFontSize = 10;
        private const int MaxFontSize = 24;

        private readonly string eliteFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            @"Saved Games\Frontier Developments\Elite Dangerous");

        private readonly string navRoutePath;
        private readonly string settingsFilePath = Path.Combine(AppContext.BaseDirectory, "overlay.config.json");

        private string currentSystem = "";
        private long? currentSystemAddress = null;

        private string currentJournalFile = "";
        private long currentJournalPosition = 0;

        private DateTime lastNavRouteWriteUtc = DateTime.MinValue;
        private readonly List<RouteEntry> routeEntries = new List<RouteEntry>();

        private readonly ContextMenuStrip overlayMenu;

        private int fontSize = 11;
        private int backgroundOpacityPercent = 100; // 0 = transparent, 100 = voll deckend

        private readonly PrivateFontCollection privateFonts = new PrivateFontCollection();
        private FontFamily? dosisRegularFamily;
        private FontFamily? dosisBoldFamily;

        private sealed class RouteEntry
        {
            public string StarSystem { get; set; } = "";
            public long? SystemAddress { get; set; }
            public string StarClass { get; set; } = "?";
        }

        private sealed class OverlaySettings
        {
            public int FontSize { get; set; } = 11;
            public int BackgroundOpacityPercent { get; set; } = 100;
            public int LocationX { get; set; } = 50;
            public int LocationY { get; set; } = 50;
            public string LayoutMode { get; set; } = "Classic";
            public int ModernSquareSize { get; set; } = 36;
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

            BackColor = Color.Black;
            TransparencyKey = Color.Empty;

            LoadPrivateFonts();
            LoadSettings();

            overlayMenu = new ContextMenuStrip();
            overlayMenu.Items.Add("Config", null, OpenConfig);
            overlayMenu.Items.Add("Exit", null, CloseOverlay);

            MouseDown += StartDrag;
            MouseMove += DoDrag;
            MouseUp += EndDragOrMenu;
            DoubleClick += CloseOverlay;
            FormClosing += Overlay_FormClosing;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += (s, e) => RefreshOverlay();
            timer.Start();

            InitializeJournalReader();
            LoadNavRouteIfChanged(force: true);
            RefreshOverlay();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RenderLayered();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            RenderLayered();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RenderLayered();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Zeichnen erfolgt komplett über UpdateLayeredWindow
        }

        private void Overlay_FormClosing(object? sender, FormClosingEventArgs e)
        {
            SaveSettingsSafe();
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
            if (e.Button != MouseButtons.Left)
                return;

            dragging = true;
            dragCursorPoint = Cursor.Position;
            dragFormPoint = Location;
        }

        private void DoDrag(object? sender, MouseEventArgs e)
        {
            if (!dragging)
                return;

            Point diff = Point.Subtract(Cursor.Position, new Size(dragCursorPoint));
            Location = Point.Add(dragFormPoint, new Size(diff));
        }

        private void EndDragOrMenu(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = false;
                SaveSettingsSafe();
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                overlayMenu.Show(this, e.Location);
            }
        }

        private void OpenConfig(object? sender, EventArgs e)
        {
            int originalFontSize = fontSize;
            int originalBackgroundOpacityPercent = backgroundOpacityPercent;
            OverlayLayoutMode originalLayoutMode = overlayLayoutMode;
            int originalModernSquareSize = modernSquareSize;
            Point originalLocation = Location;

            using Form configForm = new Form();
            configForm.Text = "Config";
            configForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            configForm.StartPosition = FormStartPosition.Manual;
            configForm.ClientSize = new Size(300, 240);
            configForm.MaximizeBox = false;
            configForm.MinimizeBox = false;
            configForm.ShowInTaskbar = false;
            configForm.AutoScaleMode = AutoScaleMode.Font;

            int configX = Right + 12;
            int configY = Top;
            configForm.Location = new Point(configX, configY);

            Label fontLabel = new Label
            {
                Text = "Font Size",
                Left = 12,
                Top = 16,
                Width = 120
            };

            NumericUpDown fontUpDown = new NumericUpDown
            {
                Left = 150,
                Top = 13,
                Width = 120,
                Minimum = MinFontSize,
                Maximum = MaxFontSize,
                Value = fontSize
            };

            fontUpDown.ValueChanged += (s, args) =>
            {
                fontSize = (int)fontUpDown.Value;
                UpdateOverlayText();
            };

            Label opacityLabel = new Label
            {
                Text = "Opacity (%)",
                Left = 12,
                Top = 54,
                Width = 120
            };

            TrackBar opacityTrackBar = new TrackBar
            {
                Left = 150,
                Top = 48,
                Width = 90,
                Height = 24,
                AutoSize = false,
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 1,
                LargeChange = 10,
                Value = Math.Max(0, Math.Min(100, backgroundOpacityPercent))
            };

            Label opacityValueLabel = new Label
            {
                Text = $"{opacityTrackBar.Value}%",
                Left = 245,
                Top = 54,
                Width = 35
            };

            opacityTrackBar.ValueChanged += (s, args) =>
            {
                backgroundOpacityPercent = opacityTrackBar.Value;
                opacityValueLabel.Text = $"{opacityTrackBar.Value}%";
                RenderLayered();
            };

            Label layoutLabel = new Label
            {
                Text = "Layout",
                Left = 12,
                Top = 92,
                Width = 120
            };

            ComboBox layoutComboBox = new ComboBox
            {
                Left = 150,
                Top = 89,
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            layoutComboBox.Items.Add("Classic");
            layoutComboBox.Items.Add("Modern");
            layoutComboBox.SelectedItem = GetOverlayLayoutModeText();

            Label boxSizeLabel = new Label
            {
                Text = "Box-Size",
                Left = 12,
                Top = 130,
                Width = 120
            };

            NumericUpDown boxSizeUpDown = new NumericUpDown
            {
                Left = 150,
                Top = 127,
                Width = 120,
                Minimum = MinModernSquareSize,
                Maximum = MaxModernSquareSize,
                Value = modernSquareSize
            };

            boxSizeUpDown.ValueChanged += (s, args) =>
            {
                modernSquareSize = NormalizeModernSquareSize((int)boxSizeUpDown.Value);
                UpdateOverlayText();
            };

            void UpdateModernControlsEnabled()
            {
                bool isModern = ParseOverlayLayoutMode(layoutComboBox.SelectedItem?.ToString()) == OverlayLayoutMode.Modern;
                boxSizeLabel.Enabled = isModern;
                boxSizeUpDown.Enabled = isModern;
            }

            layoutComboBox.SelectedIndexChanged += (s, args) =>
            {
                OverlayLayoutMode selectedLayoutMode = ParseOverlayLayoutMode(layoutComboBox.SelectedItem?.ToString());
                bool switchedToModern = overlayLayoutMode != selectedLayoutMode &&
                                        selectedLayoutMode == OverlayLayoutMode.Modern;

                overlayLayoutMode = selectedLayoutMode;

                if (switchedToModern)
                    CenterModernOverlayTop();

                UpdateModernControlsEnabled();
                UpdateOverlayText();
            };

            Button okButton = new Button
            {
                Text = "OK",
                Left = 120,
                Top = 180,
                Width = 75,
                Height = 26,
                DialogResult = DialogResult.OK
            };

            Button cancelButton = new Button
            {
                Text = "Cancel",
                Left = 200,
                Top = 180,
                Width = 75,
                Height = 26,
                DialogResult = DialogResult.Cancel
            };

            configForm.Controls.Add(fontLabel);
            configForm.Controls.Add(fontUpDown);
            configForm.Controls.Add(opacityLabel);
            configForm.Controls.Add(opacityTrackBar);
            configForm.Controls.Add(opacityValueLabel);
            configForm.Controls.Add(layoutLabel);
            configForm.Controls.Add(layoutComboBox);
            configForm.Controls.Add(boxSizeLabel);
            configForm.Controls.Add(boxSizeUpDown);
            configForm.Controls.Add(okButton);
            configForm.Controls.Add(cancelButton);

            configForm.AcceptButton = okButton;
            configForm.CancelButton = cancelButton;

            UpdateModernControlsEnabled();

            DialogResult result = configForm.ShowDialog();

            if (result == DialogResult.OK)
            {
                fontSize = (int)fontUpDown.Value;
                backgroundOpacityPercent = opacityTrackBar.Value;
                overlayLayoutMode = ParseOverlayLayoutMode(layoutComboBox.SelectedItem?.ToString());
                modernSquareSize = NormalizeModernSquareSize((int)boxSizeUpDown.Value);
                SaveSettingsSafe();
                UpdateOverlayText();
            }
            else
            {
                fontSize = originalFontSize;
                backgroundOpacityPercent = originalBackgroundOpacityPercent;
                overlayLayoutMode = originalLayoutMode;
                modernSquareSize = originalModernSquareSize;
                Location = originalLocation;
                UpdateOverlayText();
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsFilePath))
                    return;

                string json = File.ReadAllText(settingsFilePath);
                OverlaySettings? settings = JsonSerializer.Deserialize<OverlaySettings>(json);

                if (settings == null)
                    return;

                fontSize = Math.Max(MinFontSize, Math.Min(MaxFontSize, settings.FontSize));
                backgroundOpacityPercent = Math.Max(0, Math.Min(100, settings.BackgroundOpacityPercent));
                overlayLayoutMode = ParseOverlayLayoutMode(settings.LayoutMode);
                modernSquareSize = NormalizeModernSquareSize(settings.ModernSquareSize);
                Location = new Point(settings.LocationX, settings.LocationY);
                UpdateOverlayBounds();
            }
            catch
            {
                fontSize = 11;
                backgroundOpacityPercent = 0;
                overlayLayoutMode = OverlayLayoutMode.Classic;
                modernSquareSize = 36;
                Location = new Point(50, 50);
                UpdateOverlayBounds();
            }
        }

        private void SaveSettingsSafe()
        {
            try
            {
                SaveSettings();
            }
            catch
            {
                // absichtlich ignorieren
            }
        }

        private void SaveSettings()
        {
            OverlaySettings settings = new OverlaySettings
            {
                FontSize = fontSize,
                BackgroundOpacityPercent = backgroundOpacityPercent,
                LocationX = Left,
                LocationY = Top,
                LayoutMode = GetOverlayLayoutModeText(),
                ModernSquareSize = modernSquareSize
            };

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(settingsFilePath, json);
        }

        private void LoadPrivateFonts()
        {
            LoadFontFromResource("EDtoolset.Dosis-Regular.ttf", out dosisRegularFamily);
            LoadFontFromResource("EDtoolset.Dosis-Bold.ttf", out dosisBoldFamily);
        }

        private void LoadFontFromResource(string resourceName, out FontFamily family)
        {
            using Stream? fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (fontStream == null)
                throw new Exception($"Embedded font not found: {resourceName}");

            byte[] fontData = new byte[fontStream.Length];
            fontStream.Read(fontData, 0, fontData.Length);

            IntPtr fontPtr = Marshal.AllocCoTaskMem(fontData.Length);
            Marshal.Copy(fontData, 0, fontPtr, fontData.Length);

            privateFonts.AddMemoryFont(fontPtr, fontData.Length);
            Marshal.FreeCoTaskMem(fontPtr);

            family = privateFonts.Families[privateFonts.Families.Length - 1];
        }

        private Font CreateJumpsFont()
        {
            if (dosisBoldFamily == null)
                throw new InvalidOperationException("Dosis-Bold.ttf wurde nicht geladen.");

            return new Font(dosisBoldFamily, fontSize, FontStyle.Bold);
        }

        private Font CreateListFont()
        {
            if (dosisRegularFamily == null)
                throw new InvalidOperationException("Dosis-Regular.ttf wurde nicht geladen.");

            return new Font(dosisRegularFamily, Math.Max(8, fontSize - 2), FontStyle.Regular);
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

        private int GetLineHeight()
        {
            return Math.Max(15, fontSize + 4);
        }

        private void UpdateOverlayText()
        {
            try
            {
                starLines.Clear();
                modernJumpSquares.Clear();

                if (routeEntries.Count == 0)
                {
                    jumpsText = "Jumps: -";
                    UpdateOverlayBounds();
                    RenderLayered();
                    return;
                }

                int currentIndex = FindCurrentRouteIndex();

                if (overlayLayoutMode == OverlayLayoutMode.Modern)
                {
                    if (currentIndex < 0)
                    {
                        jumpsText = "Jumps: ?";
                        BuildModernJumpSquares(-1);
                    }
                    else
                    {
                        int jumpsRemaining = Math.Max(0, routeEntries.Count - currentIndex - 1);
                        jumpsText = $"Jumps: {jumpsRemaining}";
                        BuildModernJumpSquares(currentIndex);
                    }

                    UpdateOverlayBounds();
                    RenderLayered();
                    return;
                }

                if (currentIndex < 0)
                {
                    jumpsText = "Jumps: ?";
                    int previewCount = Math.Min(routeEntries.Count, MaxStarsShown);

                    for (int i = 0; i < previewCount; i++)
                    {
                        string marker = IsScoopable(routeEntries[i].StarClass) ? " ⬥" : "";
                        starLines.Add($"{i + 1}. {routeEntries[i].StarClass}{marker}");
                    }

                    UpdateOverlayBounds();
                    RenderLayered();
                    return;
                }

                int classicJumpsRemaining = Math.Max(0, routeEntries.Count - currentIndex - 1);
                jumpsText = $"Jumps: {classicJumpsRemaining}";

                int firstNextJumpIndex = currentIndex + 1;
                int lastIndexExclusive = Math.Min(routeEntries.Count, firstNextJumpIndex + MaxStarsShown);

                for (int i = firstNextJumpIndex; i < lastIndexExclusive; i++)
                {
                    string marker = IsScoopable(routeEntries[i].StarClass) ? " ⬥" : "";
                    int displayNumber = i - firstNextJumpIndex + 1;
                    starLines.Add($"{displayNumber}. {routeEntries[i].StarClass}{marker}");
                }

                UpdateOverlayBounds();
            }
            catch
            {
                jumpsText = "Jumps: -";
                starLines.Clear();
                modernJumpSquares.Clear();
                UpdateOverlayBounds();
            }

            RenderLayered();
        }

        private void RenderLayered()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
                return;

            using Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bitmap);

            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            int backgroundAlpha = (int)Math.Round(255.0 * backgroundOpacityPercent / 100.0);

            if (backgroundAlpha > 0)
            {
                using Brush backgroundBrush = new SolidBrush(Color.FromArgb(backgroundAlpha, 0, 0, 0));
                g.FillRectangle(backgroundBrush, ClientRectangle);
            }

            if (overlayLayoutMode == OverlayLayoutMode.Modern)
                RenderModernLayout(g);
            else
                RenderClassicLayout(g);

            ApplyBitmap(bitmap);
        }

        private void ApplyBitmap(Bitmap bitmap)
        {
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                NativeMethods.SIZE size = new NativeMethods.SIZE(bitmap.Width, bitmap.Height);
                NativeMethods.POINT sourcePoint = new NativeMethods.POINT(0, 0);
                NativeMethods.POINT topPos = new NativeMethods.POINT(Left, Top);

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };

                NativeMethods.UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    ref topPos,
                    ref size,
                    memDc,
                    ref sourcePoint,
                    0,
                    ref blend,
                    NativeMethods.ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero)
                    NativeMethods.SelectObject(memDc, oldBitmap);

                if (hBitmap != IntPtr.Zero)
                    NativeMethods.DeleteObject(hBitmap);

                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
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

        private static class NativeMethods
        {
            public const int WS_EX_LAYERED = 0x00080000;
            public const int ULW_ALPHA = 0x00000002;
            public const byte AC_SRC_OVER = 0x00;
            public const byte AC_SRC_ALPHA = 0x01;

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int X;
                public int Y;

                public POINT(int x, int y)
                {
                    X = x;
                    Y = y;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct SIZE
            {
                public int cx;
                public int cy;

                public SIZE(int cx, int cy)
                {
                    this.cx = cx;
                    this.cy = cy;
                }
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct BLENDFUNCTION
            {
                public byte BlendOp;
                public byte BlendFlags;
                public byte SourceConstantAlpha;
                public byte AlphaFormat;
            }

            [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll", ExactSpelling = true)]
            public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

            [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern IntPtr CreateCompatibleDC(IntPtr hDc);

            [DllImport("gdi32.dll", ExactSpelling = true)]
            public static extern bool DeleteDC(IntPtr hDc);

            [DllImport("gdi32.dll", ExactSpelling = true)]
            public static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

            [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern bool DeleteObject(IntPtr hObject);

            [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern bool UpdateLayeredWindow(
                IntPtr hwnd,
                IntPtr hdcDst,
                ref POINT pptDst,
                ref SIZE psize,
                IntPtr hdcSrc,
                ref POINT pprSrc,
                int crKey,
                ref BLENDFUNCTION pblend,
                int dwFlags);
        }
    }
}