using System;
using Autodesk.Revit.UI;

namespace GvrTools.Revit.Infrastructure
{
    /// <summary>
    /// A long operation expressed as a sequence of small steps, each of which needs a valid Revit
    /// API context.
    ///
    /// Splitting the work this way is what keeps Revit and the tool window usable while the job
    /// runs: <see cref="RevitJobScheduler"/> executes exactly one step per trip through Revit's
    /// message loop, instead of holding the API thread hostage for the whole batch.
    /// </summary>
    public interface IRevitStepJob
    {
        /// <summary>Short name used in log entries.</summary>
        string Name { get; }

        /// <summary>Number of steps, known before the job starts.</summary>
        int StepCount { get; }

        /// <summary>
        /// One-time setup. Throwing here aborts the job and the exception is handed to
        /// <see cref="End"/>, which is the intended way to report a precondition failure.
        /// </summary>
        void Begin(UIApplication application);

        /// <summary>Runs step <paramref name="stepIndex"/>. Should not throw for per-item failures.</summary>
        void ExecuteStep(UIApplication application, int stepIndex);

        /// <summary>
        /// Always called exactly once, whether the job completed, was cancelled or failed. The
        /// place to release resources and publish results.
        /// </summary>
        void End(UIApplication application, bool cancelled, Exception failure);
    }
}
