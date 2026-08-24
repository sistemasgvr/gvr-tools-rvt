using System;
using System.Collections.Generic;
using System.IO;

namespace GvrTools.Core.Naming
{
    /// <summary>
    /// Hands out collision-free names inside one folder, appending " (2)", " (3)" and so on.
    ///
    /// Unlike a bare File.Exists check it also remembers what it already handed out during the
    /// current run. That matters because some export APIs write their file only after the call
    /// returns, so two sheets whose names sanitise to the same string would otherwise both be
    /// considered free and one file would silently overwrite the other.
    /// </summary>
    public sealed class UniqueNameResolver
    {
        private readonly HashSet<string> _reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _folder;
        private readonly Func<string, bool> _exists;

        /// <param name="folder">Folder the names live in.</param>
        /// <param name="exists">Existence probe; defaults to File.Exists and is swapped in tests.</param>
        public UniqueNameResolver(string folder, Func<string, bool> exists = null)
        {
            _folder = folder ?? string.Empty;
            _exists = exists ?? File.Exists;
        }

        /// <summary>
        /// Reserves and returns a base name (no extension) that is free both on disk and within
        /// this run. Use it for export APIs that append the extension themselves.
        /// </summary>
        public string ReserveBaseName(string baseName, string extension)
        {
            string ext = NormalizeExtension(extension);
            string candidate = baseName;

            for (int suffix = 2; !TryReserve(candidate, ext); suffix++)
                candidate = baseName + " (" + suffix.ToString() + ")";

            return candidate;
        }

        /// <summary>Reserves a name and returns the full path it maps to.</summary>
        public string ReservePath(string baseName, string extension) =>
            Path.Combine(_folder, ReserveBaseName(baseName, extension) + NormalizeExtension(extension));

        private bool TryReserve(string candidate, string extension)
        {
            string fileName = candidate + extension;

            if (_reserved.Contains(fileName)) return false;
            if (_exists(Path.Combine(_folder, fileName))) return false;

            _reserved.Add(fileName);
            return true;
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return string.Empty;

            return extension[0] == '.' ? extension : "." + extension;
        }
    }
}
