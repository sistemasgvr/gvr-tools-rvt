using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace GvrTools.MassPdfExport.Resources
{
    /// <summary>
    /// Draws a small "PDF export" glyph as a vector DrawingImage, so the ribbon button has an
    /// icon without shipping a binary image asset in the repository.
    /// </summary>
    public static class RibbonIconFactory
    {
        public static DrawingImage CreateExportIcon()
        {
            var group = new DrawingGroup();

            var page = new RectangleGeometry(new Rect(6, 3, 20, 26), 2, 2);
            group.Children.Add(new GeometryDrawing(
                Brushes.White,
                new Pen(new SolidColorBrush(Color.FromRgb(0x45, 0x5A, 0x64)), 1.5),
                page));

            Geometry fold = FigureGeometry(new Point(18, 3), new Point(26, 3), new Point(26, 11));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC)), null, fold));

            var band = new RectangleGeometry(new Rect(6, 20, 20, 9));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)), null, band));

            var arrow = new GeometryGroup { FillRule = FillRule.Nonzero };
            arrow.Children.Add(new RectangleGeometry(new Rect(14, 6, 4, 7)));
            arrow.Children.Add(FigureGeometry(new Point(9, 13), new Point(23, 13), new Point(16, 19)));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)), null, arrow));

            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        private static StreamGeometry FigureGeometry(params Point[] points)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], true, true);
                ctx.PolyLineTo(points.Skip(1).ToList(), true, false);
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
