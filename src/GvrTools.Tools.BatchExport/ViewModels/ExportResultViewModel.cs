using GvrTools.Core.Batch;
using GvrTools.UI.Mvvm;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>
    /// One row of the results list.
    ///
    /// Results are shown live in the window instead of being collected into a message box at the
    /// end: the user can see which sheet is failing while the batch is still running, and nothing
    /// pops up in front of whatever they are doing.
    /// </summary>
    public sealed class ExportResultViewModel : ObservableObject
    {
        public ExportResultViewModel(BatchItemResult result, string formatTag = null)
        {
            Label = formatTag != null ? $"{result.Label} ({formatTag})" : result.Label;
            Succeeded = result.Succeeded;
            Detail = result.Succeeded ? result.OutputPath : result.Message;
        }

        public string Label { get; }

        public bool Succeeded { get; }

        public string Detail { get; }

        public string StatusText => Succeeded ? "OK" : "Error";
    }
}
