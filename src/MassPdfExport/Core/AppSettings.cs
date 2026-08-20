using System;
using System.Collections.Generic;
using System.IO;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>
    /// Remembers the user's last choices (output folder, printer, print options) between Revit
    /// sessions. Stored as a plain key=value text file under %APPDATA% — no JSON/XML dependency
    /// needed for half a dozen simple string/bool fields.
    /// </summary>
    public sealed class AppSettings
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GVR", "MassPdfExport", "settings.txt");

        public string OutputFolder { get; set; } = string.Empty;
        public string NamingPattern { get; set; } = FileNaming.DefaultPattern;
        public string PrinterName { get; set; } = string.Empty;
        public bool NoMargin { get; set; } = true;
        public bool FitToPage { get; set; } = true;
        public bool MatchSheetSize { get; set; } = true;
        public bool OpenFolderWhenDone { get; set; } = true;

        public static AppSettings Load()
        {
            var settings = new AppSettings();

            try
            {
                if (!File.Exists(FilePath)) return settings;

                var values = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    values[line.Substring(0, i)] = line.Substring(i + 1);
                }

                settings.OutputFolder = Get(values, nameof(OutputFolder), settings.OutputFolder);
                settings.NamingPattern = Get(values, nameof(NamingPattern), settings.NamingPattern);
                settings.PrinterName = Get(values, nameof(PrinterName), settings.PrinterName);
                settings.NoMargin = GetBool(values, nameof(NoMargin), settings.NoMargin);
                settings.FitToPage = GetBool(values, nameof(FitToPage), settings.FitToPage);
                settings.MatchSheetSize = GetBool(values, nameof(MatchSheetSize), settings.MatchSheetSize);
                settings.OpenFolderWhenDone = GetBool(values, nameof(OpenFolderWhenDone), settings.OpenFolderWhenDone);
            }
            catch
            {
                // A corrupt or unreadable settings file just means starting from defaults.
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");

                string[] lines =
                {
                    $"{nameof(OutputFolder)}={OutputFolder}",
                    $"{nameof(NamingPattern)}={NamingPattern}",
                    $"{nameof(PrinterName)}={PrinterName}",
                    $"{nameof(NoMargin)}={NoMargin}",
                    $"{nameof(FitToPage)}={FitToPage}",
                    $"{nameof(MatchSheetSize)}={MatchSheetSize}",
                    $"{nameof(OpenFolderWhenDone)}={OpenFolderWhenDone}"
                };

                File.WriteAllLines(FilePath, lines);
            }
            catch
            {
                // Saving preferences is a convenience; never let it block or crash the add-in.
            }
        }

        private static string Get(Dictionary<string, string> values, string key, string fallback) =>
            values.TryGetValue(key, out string v) ? v : fallback;

        private static bool GetBool(Dictionary<string, string> values, string key, bool fallback) =>
            values.TryGetValue(key, out string v) && bool.TryParse(v, out bool parsed) ? parsed : fallback;
    }
}
