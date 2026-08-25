using System.Windows;
using System.Windows.Interop;
using GvrTools.Licensing.Http.Dto;

namespace GvrTools.Licensing.Activation
{
    /// <summary>Helpers para abrir las ventanas de licencia desde comandos Revit (owner HWND).</summary>
    public static class LicenseUi
    {
        public static bool? ShowActivate(
            LicenseClient client,
            System.IntPtr ownerHwnd = default,
            string initialMessage = null)
        {
            var vm = new ActivateLicenseViewModel(client ?? LicenseRuntime.Client, initialMessage);
            var window = new ActivateLicenseWindow(vm);
            AttachOwner(window, ownerHwnd);
            return window.ShowDialog();
        }

        public static void ShowAccount(LicenseClient client = null, System.IntPtr ownerHwnd = default)
        {
            var vm = new AccountLicenseViewModel(client ?? LicenseRuntime.Client);
            var window = new AccountLicenseWindow(vm);
            AttachOwner(window, ownerHwnd);
            window.ShowDialog();
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
