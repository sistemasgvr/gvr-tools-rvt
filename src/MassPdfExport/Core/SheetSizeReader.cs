using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>Measures a sheet's physical size in inches from its title block, so each PDF page can match its real paper size.</summary>
    public static class SheetSizeReader
    {
        private const double FeetToInches = 12.0;

        public static (double WidthIn, double HeightIn) GetSheetSizeInches(Document doc, ViewSheet sheet)
        {
            Element titleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            if (titleBlock == null) return (0, 0);

            BoundingBoxXYZ bbox = titleBlock.get_BoundingBox(sheet);
            if (bbox == null) return (0, 0);

            double width = Math.Abs(bbox.Max.X - bbox.Min.X) * FeetToInches;
            double height = Math.Abs(bbox.Max.Y - bbox.Min.Y) * FeetToInches;
            return (width, height);
        }
    }
}
