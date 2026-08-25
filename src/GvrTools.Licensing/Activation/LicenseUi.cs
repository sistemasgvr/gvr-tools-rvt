using System.Windows;
using System.Windows.Interop;

namespace GvrTools.Licensing.Activation
{
    /// <summary>Helpers para abrir las ventanas de licencia desde comandos Revit (owner HWND).</summary>
    public static class LicenseUi
    {
        public static bool? ShowActivate(LicenseClient client, System.IntPtr ownerHwnd = default)
        {
            var vm = new ActivateLicenseViewModel(client ?? LicenseRuntime.Client);
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

        private static void AttachOwner(Window window, System.IntPtr ownerHwnd)
        {
            if (ownerHwnd != System.IntPtr.Zero)
                new WindowInteropHelper(window).Owner = ownerHwnd;
        }
    }
}
