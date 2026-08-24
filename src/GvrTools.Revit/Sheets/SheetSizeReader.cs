using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace GvrTools.Revit.Sheets
{
    /// <summary>
    /// Measures a sheet's physical size in inches from the bounding box of its title block, so a
    /// plotted page can be matched to the paper size the sheet was actually designed for.
    /// </summary>
    public static class SheetSizeReader
    {
        private const double FeetToInches = 12.0;

        /// <summary>Returns (0, 0) when the sheet has no title block to measure.</summary>
        public static SheetSize Read(Document document, ViewSheet sheet)
        {
            try
            {
                Element titleBlock = new FilteredElementCollector(document, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault();

                if (titleBlock == null) return SheetSize.Unknown;

                BoundingBoxXYZ box = titleBlock.get_BoundingBox(sheet);
                if (box == null) return SheetSize.Unknown;

                return new SheetSize(
                    Math.Abs(box.Max.X - box.Min.X) * FeetToInches,
                    Math.Abs(box.Max.Y - box.Min.Y) * FeetToInches);
            }
            catch (Exception)
            {
                return SheetSize.Unknown;
            }
        }
    }

    /// <summary>A sheet's width and height in inches.</summary>
    public struct SheetSize
    {
        public static readonly SheetSize Unknown = new SheetSize(0, 0);

        public SheetSize(double widthInches, double heightInches)
        {
            WidthInches = widthInches;
            HeightInches = heightInches;
        }

        public double WidthInches { get; }

        public double HeightInches { get; }

        public bool IsKnown => WidthInches > 0 && HeightInches > 0;

        public bool IsLandscape => WidthInches >= HeightInches;

        public override string ToString() => IsKnown
            ? $"{WidthInches:0.0} x {HeightInches:0.0} in"
            : "tamaño desconocido";
    }
}
