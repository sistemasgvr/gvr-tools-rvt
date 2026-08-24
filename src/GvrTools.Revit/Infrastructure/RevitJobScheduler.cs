using System;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using GvrTools.Core.Diagnostics;

namespace GvrTools.Revit.Infrastructure
{
    /// <summary>
    /// Runs an <see cref="IRevitStepJob"/> one step at a time, always inside a valid Revit API
    /// context, and always giving the message loop a turn in between.
    ///
    /// Why this exists. The Revit API may only be touched from Revit's own thread, so the obvious
    /// implementation is a plain for-loop inside the command. That is exactly what makes a batch
    /// export feel like the application has hung: Revit cannot repaint, the tool window cannot
    /// update, Cancel cannot be clicked, and Windows eventually paints the whole thing white.
    /// Pumping the dispatcher by hand inside such a loop only half-fixes it and invites reentrancy.
    ///
    /// The pattern used here instead is the one Revit actually supports for this: an
    /// <see cref="ExternalEvent"/> is raised, its handler performs a single step and returns, and
    /// the next raise is posted back through the WPF dispatcher at Background priority. Between two
    /// steps Revit is idle and completely responsive, progress paints normally, and cancelling
    /// takes effect on the very next step.
    /// </summary>
    public sealed class RevitJobScheduler : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly StepPump _pump;
        private readonly ExternalEvent _externalEvent;
        private readonly ILog _log;
        private bool _disposed;

        /// <summary>
        /// Must be constructed from a valid Revit API context (typically inside
        /// <see cref="IExternalCommand.Execute"/>), because that is what
        /// <see cref="ExternalEvent.Create"/> requires.
        /// </summary>
        public RevitJobScheduler(ILog log = null)
        {
            _log = log ?? NullLog.Instance;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _pump = new StepPump(this);
            _externalEvent = ExternalEvent.Create(_pump);
        }

        public bool IsRunning => _pump.Job != null;

        /// <summary>Queues <paramref name="job"/>. Returns immediately; the job runs step by step.</summary>
        public void Start(IRevitStepJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (_disposed) throw new ObjectDisposedException(nameof(RevitJobScheduler));
            if (IsRunning) throw new InvalidOperationException("Ya hay una operación en curso en este programador de tareas.");

            _log.Info($"Iniciando trabajo '{job.Name}' ({job.StepCount} paso(s)).");
            _pump.Reset(job);
            _externalEvent.Raise();
        }

        /// <summary>
        /// Asks the running job to stop. Takes effect before the next step, so the current step
        /// always finishes cleanly rather than being torn down half-way.
        /// </summary>
        public void RequestCancel() => _pump.CancelRequested = true;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _externalEvent.Dispose();
        }

        private void ScheduleNextStep()
        {
            // Raising the event from inside its own handler is not a supported way to continue, and
            // going through the dispatcher is precisely what hands control back to Revit so it can
            // repaint and process input before the next step.
            if (_dispatcher != null && !_dispatcher.HasShutdownStarted)
                _dispatcher.BeginInvoke(new Action(RaiseSafely), DispatcherPriority.Background);
            else
                RaiseSafely();
        }

        private void RaiseSafely()
        {
            if (_disposed) return;

            try
            {
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                _log.Error("No se pudo continuar la operación por pasos.", ex);
                _pump.Abandon(ex);
            }
        }

        /// <summary>The external event handler: advances the job by exactly one step per call.</summary>
        private sealed class StepPump : IExternalEventHandler
        {
            private readonly RevitJobScheduler _owner;
            private int _nextStep;
            private bool _begun;

            internal StepPump(RevitJobScheduler owner)
            {
                _owner = owner;
            }

            internal IRevitStepJob Job { get; private set; }

            internal bool CancelRequested { get; set; }

            internal void Reset(IRevitStepJob job)
            {
                Job = job;
                _nextStep = 0;
                _begun = false;
                CancelRequested = false;
            }

            public string GetName() => "GVR Tools - programador de tareas";

            public void Execute(UIApplication application)
            {
                IRevitStepJob job = Job;
                if (job == null) return;

                try
                {
                    if (!_begun)
                    {
                        _begun = true;
                        job.Begin(application);
                    }

                    if (!CancelRequested && _nextStep < job.StepCount)
                    {
                        job.ExecuteStep(application, _nextStep);
                        _nextStep++;
                    }

                    if (CancelRequested || _nextStep >= job.StepCount)
                        Finish(application, job, CancelRequested, null);
                    else
                        _owner.ScheduleNextStep();
                }
                catch (Exception ex)
                {
                    _owner._log.Error($"El trabajo '{job.Name}' se interrumpió por un error.", ex);
                    Finish(application, job, CancelRequested, ex);
                }
            }

            /// <summary>Ends the job without a Revit context available (only used when raising fails).</summary>
            internal void Abandon(Exception failure) => Finish(null, Job, CancelRequested, failure);

            private void Finish(UIApplication application, IRevitStepJob job, bool cancelled, Exception failure)
            {
                Job = null;

                if (job == null) return;

                try
                {
                    job.End(application, cancelled, failure);
                }
                catch (Exception ex)
                {
                    _owner._log.Error($"El cierre del trabajo '{job.Name}' falló.", ex);
                }
            }
        }
    }
}
