using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace EDtoolset
{
    public partial class Overlay
    {
        private enum OverlayLayoutMode
        {
            Classic,
            Modern
        }

        private sealed class ModernJumpSquare
        {
            public string Text { get; set; } = "";
            public bool IsScoopable { get; set; }
        }

        private const int ClassicOverlayWidth = 190;
        private const int ClassicMinOverlayHeight = 40;

        private const int ModernSquareSpacing = 4;
        private const int ModernTopMargin = 8;
        private const int ModernBottomMargin = 8;
        private const int ModernSideMargin = 8;

        private const int MinModernSquareSize = 10;
        private const int MaxModernSquareSize = 120;

        private readonly List<ModernJumpSquare> modernJumpSquares = new List<ModernJumpSquare>();
        private OverlayLayoutMode overlayLayoutMode = OverlayLayoutMode.Classic;
        private int modernSquareSize = 36;

        private int NormalizeModernSquareSize(int value)
        {
            return Math.Max(MinModernSquareSize, Math.Min(MaxModernSquareSize, value));
        }

        private void UpdateOverlayBounds()
        {
            if (overlayLayoutMode == OverlayLayoutMode.Modern)
            {
                int visibleSquareCount = Math.Max(1, modernJumpSquares.Count);
                Width = GetModernOverlayWidth(visibleSquareCount);
                Height = ModernTopMargin + modernSquareSize + ModernBottomMargin;
                return;
            }

            Width = ClassicOverlayWidth;
            Height = Math.Max(ClassicMinOverlayHeight, 28 + ((starLines.Count + 2) * GetLineHeight()));
        }

        private int GetModernOverlayWidth(int squareCount)
        {
            return ModernSideMargin +
                   (squareCount * modernSquareSize) +
                   (Math.Max(0, squareCount - 1) * ModernSquareSpacing) +
                   ModernSideMargin;
        }

        private void CenterModernOverlayTop()
        {
            int visibleSquareCount = Math.Max(1, modernJumpSquares.Count);
            int overlayWidth = GetModernOverlayWidth(visibleSquareCount);
            Rectangle virtualScreen = SystemInformation.VirtualScreen;

            int x = virtualScreen.Left + ((virtualScreen.Width - overlayWidth) / 2);
            int y = virtualScreen.Top + ModernTopMargin;

            Location = new Point(x, y);
        }

        private OverlayLayoutMode ParseOverlayLayoutMode(string? layoutMode)
        {
            return string.Equals(layoutMode, "Modern", StringComparison.OrdinalIgnoreCase)
                ? OverlayLayoutMode.Modern
                : OverlayLayoutMode.Classic;
        }

        private string GetOverlayLayoutModeText()
        {
            return overlayLayoutMode == OverlayLayoutMode.Modern ? "Modern" : "Classic";
        }

        private void BuildModernJumpSquares(int currentIndex)
        {
            modernJumpSquares.Clear();

            if (routeEntries.Count == 0)
                return;

            int firstNextJumpIndex = currentIndex >= 0 ? currentIndex + 1 : 0;
            int lastIndexExclusive = Math.Min(routeEntries.Count, firstNextJumpIndex + MaxStarsShown);

            int jumpsRemaining = currentIndex >= 0
                ? Math.Max(0, routeEntries.Count - currentIndex - 1)
                : routeEntries.Count;

            for (int i = firstNextJumpIndex; i < lastIndexExclusive; i++)
            {
                int displayNumber = Math.Max(1, jumpsRemaining - (i - firstNextJumpIndex));

                modernJumpSquares.Add(new ModernJumpSquare
                {
                    Text = displayNumber.ToString(),
                    IsScoopable = IsScoopable(routeEntries[i].StarClass)
                });
            }
        }

        private Font CreateModernSquareFont()
        {
            if (dosisBoldFamily == null)
                throw new InvalidOperationException("Dosis-Bold.ttf wurde nicht geladen.");

            return new Font(dosisBoldFamily, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        private void RenderClassicLayout(Graphics g)
        {
            using Brush textBrush = new SolidBrush(Color.FromArgb(255, 220, 110, 0));
            using Brush markerBrush = new SolidBrush(Color.FromArgb(255, 70, 140, 255));
            using Font jumpsFont = CreateJumpsFont();
            using Font listFont = CreateListFont();

            float y = 2f;
            g.DrawString(jumpsText, jumpsFont, textBrush, 4f, y);

            y += fontSize + 16f;

            foreach (string line in starLines)
            {
                if (line.EndsWith("⬥"))
                {
                    string text = line.Replace(" ⬥", "");

                    g.DrawString(text, listFont, textBrush, 4f, y);

                    SizeF size = g.MeasureString(text, listFont);
                    g.DrawString(" ⬥", listFont, markerBrush, 4f + size.Width, y);
                }
                else
                {
                    g.DrawString(line, listFont, textBrush, 4f, y);
                }

                y += GetLineHeight();
            }
        }

        private void RenderModernLayout(Graphics g)
        {
            using Font squareFont = CreateModernSquareFont();

            int alpha = (int)Math.Round(255.0 * backgroundOpacityPercent / 100.0);

            using Brush scoopableBrush = new SolidBrush(Color.FromArgb(alpha, 70, 140, 255));
            using Brush nonScoopableBrush = new SolidBrush(Color.FromArgb(alpha, 220, 110, 0));
            using Brush textBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
            using Pen borderPen = new Pen(Color.FromArgb(alpha, 0, 0, 0));
            using StringFormat stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            int squareSize = NormalizeModernSquareSize(modernSquareSize);

            for (int i = 0; i < modernJumpSquares.Count; i++)
            {
                int x = ModernSideMargin + (i * (squareSize + ModernSquareSpacing));
                Rectangle rect = new Rectangle(x, ModernTopMargin, squareSize, squareSize);
                ModernJumpSquare square = modernJumpSquares[i];

                g.FillRectangle(square.IsScoopable ? scoopableBrush : nonScoopableBrush, rect);
                g.DrawRectangle(borderPen, rect);
                g.DrawString(square.Text, squareFont, textBrush, rect, stringFormat);
            }
        }
    }
}