using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GvrTools.Licensing.Http.Dto;

namespace GvrTools.Licensing.Activation
{
    /// <summary>Helpers para abrir las ventanas de licencia desde comandos Revit (owner HWND).</summary>
    public static class LicenseUi
    {
        /// <summary>
        /// Opcional: lo registra el host Revit para cerrar/reiniciar la aplicación tras activar.
        /// </summary>
        public static Action RequestApplicationClose { get; set; }

        public static bool? ShowActivate(
            LicenseClient client,
            System.IntPtr ownerHwnd = default,
            string initialMessage = null)
        {
            var vm = new ActivateLicenseViewModel(client ?? LicenseRuntime.Client, initialMessage);
            var window = new ActivateLicenseWindow(vm);
            AttachOwner(window, ownerHwnd);
            bool? accepted = window.ShowDialog();
            if (accepted == true)
                PromptRestartAfterActivation();
            return accepted;
        }

        public static void ShowAccount(LicenseClient client = null, System.IntPtr ownerHwnd = default)
        {
            var vm = new AccountLicenseViewModel(client ?? LicenseRuntime.Client);
            var window = new AccountLicenseWindow(vm);
            AttachOwner(window, ownerHwnd);
            window.ShowDialog();
            // Si se activó desde Cuenta, la ventana ya pidió reinicio al cerrarse.
        }

        /// <summary>
        /// Aviso al usuario y cierre diferido de Revit (cuando el dispatcher esté idle).
        /// Debe llamarse solo cuando ya no hay ShowDialog de licencia abiertos.
        /// </summary>
        public static void PromptRestartAfterActivation()
        {
            MessageBox.Show(
                "Licencia activada correctamente.\n\n" +
                "Al pulsar Aceptar, Revit se cerrará y se volverá a abrir solo para cargar todas las herramientas de tu plan.\n\n" +
                "Si tenías un proyecto guardado abierto, se reabrirá automáticamente.\n" +
                "Si Revit pregunta si guardar, confirma o descarta según corresponda.",
                "GVR Tools · Reiniciar Revit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ScheduleApplicationClose();
        }

        /// <summary>
        /// Encola el cierre cuando WPF/Revit ya no están dentro de un diálogo modal.
        /// </summary>
        public static void ScheduleApplicationClose()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                RequestApplicationClose?.Invoke();
                return;
            }

            dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => RequestApplicationClose?.Invoke()));
        }

        /// <summary>
        /// Aviso no modal: el usuario puede seguir trabajando en Revit y descargar cuando quiera.
        /// </summary>
        public static void ShowUpdateAvailable(
            UpdateCheckResponse update,
            string currentVersion,
            LicenseClient client = null,
            System.IntPtr ownerHwnd = default)
        {
            if (update == null || !update.UpdateAvailable) return;

            var vm = new UpdateAvailableViewModel(client ?? LicenseRuntime.Client, update, currentVersion);
            var window = new UpdateAvailableWindow(vm);
            AttachOwner(window, ownerHwnd);
            window.Show();
        }

        private static void AttachOwner(Window window, System.IntPtr ownerHwnd)
        {
            if (ownerHwnd != System.IntPtr.Zero)
                new WindowInteropHelper(window).Owner = ownerHwnd;
        }
    }
}
