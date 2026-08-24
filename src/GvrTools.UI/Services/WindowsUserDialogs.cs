using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace GvrTools.UI.Services
{
    /// <summary>Windows implementation of <see cref="IUserDialogs"/>.</summary>
    public sealed class WindowsUserDialogs : IUserDialogs
    {
        private readonly Window _owner;

        public WindowsUserDialogs(Window owner = null)
        {
            _owner = owner;
        }

        public string PickFolder(string description, string initialPath)
        {
            using (var dialog = new Forms.FolderBrowserDialog
            {
                Description = description,
                ShowNewFolderButton = true
            })
            {
                if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                    dialog.SelectedPath = initialPath;

                return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        public void ShowInfo(string title, string message) =>
            Show(title, message, MessageBoxImage.Information);

        public void ShowWarning(string title, string message) =>
            Show(title, message, MessageBoxImage.Warning);

        public void ShowError(string title, string message) =>
            Show(title, message, MessageBoxImage.Error);

        public bool Confirm(string title, string message)
        {
            MessageBoxResult result = _owner == null
                ? MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
                : MessageBox.Show(_owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);

            return result == MessageBoxResult.OK;
        }

        public void Reveal(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;

                if (File.Exists(path))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path))
                    Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception)
            {
                // Opening Explorer is a convenience; a failure here must not surface as an error.
            }
        }

        private void Show(string title, string message, MessageBoxImage icon)
        {
            if (_owner == null)
                MessageBox.Show(message, title, MessageBoxButton.OK, icon);
            else
                MessageBox.Show(_owner, message, title, MessageBoxButton.OK, icon);
        }
    }
}
