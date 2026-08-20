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
    /// SubmitPrint() blocks until that dialog is dismissed, so this runs on a background thread
    /// started just before SubmitPrint():
    ///
    ///  1. It watches for a new top-level dialog window (by window class, not title text, so it
    ///     isn't tied to the Windows/Revit display language).
    ///  2. The instant it appears, the window is hidden, then the file name is written directly to
    ///     its filename field and Save is triggered via window messages (WM_SETTEXT / BM_CLICK on
    ///     the standard Explorer-dialog control IDs) - none of that needs the window to be visible
    ///     or focused, so nothing flashes on screen.
    ///  3. If those specific controls can't be found (e.g. a different dialog layout), it falls back
    ///     to showing the window and typing into it via SendKeys - visible, but still unattended -
    ///     so a control-ID mismatch degrades gracefully instead of hanging the export.
    ///  4. If even that doesn't close the dialog within the overall timeout, it is force-closed so
    ///     one stuck sheet can never hang the whole batch.
    /// </summary>
    internal static class SaveDialogAutomator
    {
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const string DialogClass = "#32770"; // standard Windows dialog box class
        private const int SwHide = 0;
        private const int SwShow = 5;
        private const int WmSetText = 0x000C;
        private const int WmClose = 0x0010;
        private const int BmClick = 0x00F5;
        private const int IdFileNameEdit = 0x47C; // standard Explorer-dialog filename combo/edit control id
        private const int IdOk = 0x1;              // standard Save/OK button id

        /// <summary>Starts the background watcher. Call BEFORE PrintManager.SubmitPrint().</summary>
        public static void WatchAndFillIn(string filePath, TimeSpan timeout)
        {
            HashSet<IntPtr> before = SnapshotDialogWindows();

            var thread = new Thread(() => Run(filePath, before, timeout)) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private static void Run(string filePath, HashSet<IntPtr> before, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);

            IntPtr dialog = WaitForNewDialog(before, deadline);
            if (dialog == IntPtr.Zero) return; // no dialog appeared - driver saved silently, or timed out

            ShowWindow(dialog, SwHide);

            if (!TryFillViaControlIds(dialog, filePath))
                FallBackToVisibleSendKeys(dialog, filePath);

            // Independent of how long we waited for the dialog to appear - always give it a few
            // more seconds to close on its own before forcing it, so one stuck sheet can't hang
            // SubmitPrint() (and the whole batch) forever.
            ForceCloseIfStillOpen(dialog, DateTime.UtcNow.AddSeconds(5));
        }

        private static IntPtr WaitForNewDialog(HashSet<IntPtr> before, DateTime deadline)
        {
            while (DateTime.UtcNow < deadline)
            {
                IntPtr candidate = SnapshotDialogWindows().FirstOrDefault(h => !before.Contains(h));
                if (candidate != IntPtr.Zero) return candidate;
                Thread.Sleep(80);
            }
            return IntPtr.Zero;
        }

        private static bool TryFillViaControlIds(IntPtr dialog, string filePath)
        {
            IntPtr fileNameControl = GetDlgItem(dialog, IdFileNameEdit);
            IntPtr saveButton = GetDlgItem(dialog, IdOk);
            if (fileNameControl == IntPtr.Zero || saveButton == IntPtr.Zero) return false;

            SendMessage(fileNameControl, WmSetText, IntPtr.Zero, filePath);
            Thread.Sleep(80);
            SendMessage(saveButton, BmClick, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(300);

            return !IsWindow(dialog);
        }

        private static void FallBackToVisibleSendKeys(IntPtr dialog, string filePath)
        {
            if (!IsWindow(dialog)) return;

            ShowWindow(dialog, SwShow);
            SetForegroundWindow(dialog);
            Thread.Sleep(200);

            SendKeys.SendWait("^a");
            Thread.Sleep(50);
            SendKeys.SendWait(EscapeForSendKeys(filePath));
            Thread.Sleep(120);
            SendKeys.SendWait("{ENTER}");
        }

        private static void ForceCloseIfStillOpen(IntPtr dialog, DateTime deadline)
        {
            while (IsWindow(dialog) && DateTime.UtcNow < deadline)
                Thread.Sleep(150);

            if (IsWindow(dialog))
                PostMessage(dialog, WmClose, IntPtr.Zero, IntPtr.Zero);
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

        private static string EscapeForSendKeys(string text)
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
