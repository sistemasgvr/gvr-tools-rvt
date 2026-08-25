using System;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using GvrTools.Licensing;
using GvrTools.Licensing.Activation;
using GvrTools.Revit.Infrastructure;
using GvrTools.Revit.Ribbon;
using GvrTools.UI.Icons;
using MediaColor = System.Windows.Media.Color;
using RvtDB = Autodesk.Revit.DB;

namespace GvrTools.App.Account
{
    /// <summary>
    /// Botón siempre visible: abrir Cuenta / Licencia (activar o desactivar PC).
    /// </summary>
    public sealed class AccountTool : RevitToolBase
    {
        public override string Id => "GvrAccountLicense";

        public override string Title => "Cuenta" + Environment.NewLine + "Licencia";

        public override string PanelName => "Licencia";

        public override int SortOrder => 1;

        public override Type CommandType => typeof(AccountCommand);

        public override string RequiredFeature => null;

        public override string Tooltip => "Ver el plan, la cuota restante y activar o liberar este PC.";

        public override ImageSource CreateIcon() => VectorIcon.Compose(
            VectorIcon.FilledRectangle(new Rect(6, 4, 20, 24), Colors.White, MediaColor.FromRgb(0x45, 0x5A, 0x64), 1.5, 2),
            VectorIcon.Rectangle(new Rect(10, 10, 12, 2), MediaColor.FromRgb(0x15, 0x65, 0xC0)),
            VectorIcon.Rectangle(new Rect(10, 15, 12, 2), MediaColor.FromRgb(0x15, 0x65, 0xC0)),
            VectorIcon.Rectangle(new Rect(10, 20, 8, 2), MediaColor.FromRgb(0x15, 0x65, 0xC0)));
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class AccountCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, RvtDB.ElementSet elements)
        {
            try
            {
                LicenseRuntime.EnsureInitialized();
                RevitRestart.PendingDocumentPath = commandData.Application.ActiveUIDocument?.Document?.PathName;
                var hwnd = commandData.Application.MainWindowHandle;
                LicenseUi.ShowAccount(LicenseRuntime.Client, hwnd);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
