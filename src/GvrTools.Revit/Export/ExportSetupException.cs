using System;

namespace GvrTools.Revit.Export
{
    /// <summary>
    /// A precondition of the run is not met (no writable destination, a printer that cannot export
    /// unattended, ...).
    ///
    /// The distinction matters: the <see cref="Exception.Message"/> of this type is written for the
    /// end user and is shown verbatim, and it means nothing was exported at all — as opposed to a
    /// per-sheet failure, which is reported as a result row and lets the rest of the batch finish.
    /// </summary>
    public sealed class ExportSetupException : Exception
    {
        public ExportSetupException(string message)
            : base(message)
        {
        }

        public ExportSetupException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
