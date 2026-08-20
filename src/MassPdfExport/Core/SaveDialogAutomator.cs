using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>
    /// "Microsoft Print to PDF" ignores PrintManager.PrintToFileName and pops its own native
    /// "Save Print Output As" dialog for every single print job, defeating unattended batch export.
    /// SubmitPrint() blocks until that dialog is dismissed, so this runs on a separate thread
    /// started just before SubmitPrint(): it watches for a new top-level dialog window to appear,
    /// then types the full path into whatever field currently has focus (the filename field has
    /// focus by default when the dialog opens) and confirms it — the same thing a user would do by
    /// hand, just without a language-specific window title or control lookup, so it isn't tied to
    /// the Windows/Revit display language.
    /// </summary>
    internal static class SaveDialogAutomator
    {
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const string DialogClass = "#32770"; // standard Windows dialog box class

        /// <summary>Starts a background watcher for the save dialog. Call BEFORE PrintManager.SubmitPrint(); safe to ignore the returned thread if not needed.</summary>
        public static System.Threading.Thread WatchAndFillIn(string filePath, TimeSpan timeout)
        {
            HashSet<IntPtr> before = SnapshotDialogWindows();

            var thread = new System.Threading.Thread(() => Run(filePath, before, timeout))
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        }

        private static void Run(string filePath, HashSet<IntPtr> before, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            IntPtr dialog = IntPtr.Zero;

            while (DateTime.UtcNow < deadline)
            {
                IntPtr candidate = SnapshotDialogWindows().FirstOrDefault(h => !before.Contains(h));
                if (candidate != IntPtr.Zero)
                {
                    dialog = candidate;
                    break;
                }
                System.Threading.Thread.Sleep(100);
            }

            if (dialog == IntPtr.Zero) return; // no dialog appeared (driver saved silently, or timed out) - nothing to do

            SetForegroundWindow(dialog);
            System.Threading.Thread.Sleep(250);

            SendKeys.SendWait("^a");
            System.Threading.Thread.Sleep(50);
            SendKeys.SendWait(Escape(filePath));
            System.Threading.Thread.Sleep(150);
            SendKeys.SendWait("{ENTER}");
        }

        private static HashSet<IntPtr> SnapshotDialogWindows()
        {
            var result = new HashSet<IntPtr>();
            EnumWindows((hWnd, _) =>
            {
                if (IsWindowVisible(hWnd) && GetClassOf(hWnd) == DialogClass)
                    result.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static string GetClassOf(IntPtr hWnd)
        {
            var sb = new StringBuilder(64);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if ("+^%~(){}[]".IndexOf(c) >= 0)
                    sb.Append('{').Append(c).Append('}');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
