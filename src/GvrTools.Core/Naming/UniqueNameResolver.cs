using System;
using System.Collections.Generic;
using System.IO;

namespace GvrTools.Core.Naming
{
    /// <summary>
    /// Hands out collision-free names inside one folder, appending "_2", "_3" and so on.
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
        /// <param name="exists">Existence probe; defaults to a locked-file-aware check and is swapped in tests.</param>
        public UniqueNameResolver(string folder, Func<string, bool> exists = null)
        {
            _folder = folder ?? string.Empty;
            _exists = exists ?? IsPathTaken;
        }

        /// <summary>
        /// Reserves and returns a base name (no extension) that is free both on disk and within
        /// this run. Use it for export APIs that append the extension themselves.
        /// </summary>
        public string ReserveBaseName(string baseName, string extension) =>
            ReserveBaseName(baseName, extension, null);

        /// <summary>
        /// Same as <see cref="ReserveBaseName(string,string)"/> but also avoids Revit DWG sibling
        /// files named <c>{baseName}-{view}.ext</c> when views are exported separately.
        /// </summary>
        public string ReserveBaseName(string baseName, string extension, IReadOnlyList<string> viewSuffixes)
        {
            string ext = NormalizeExtension(extension);
            string candidate = baseName;

            for (int suffix = 2; !TryReserve(candidate, ext, viewSuffixes); suffix++)
                candidate = baseName + "_" + suffix.ToString();

            return candidate;
        }

        /// <summary>Reserves a name and returns the full path it maps to.</summary>
        public string ReservePath(string baseName, string extension) =>
            Path.Combine(_folder, ReserveBaseName(baseName, extension) + NormalizeExtension(extension));

        private bool TryReserve(string candidate, string extension, IReadOnlyList<string> viewSuffixes)
        {
            if (!TryReserveFileName(candidate + extension))
                return false;

            if (viewSuffixes == null || viewSuffixes.Count == 0)
                return true;

            foreach (string viewSuffix in viewSuffixes)
            {
                if (string.IsNullOrWhiteSpace(viewSuffix))
                    continue;

                if (!TryReserveFileName(candidate + "-" + viewSuffix.Trim() + extension))
                    return false;
            }

            return true;
        }

        private bool TryReserveFileName(string fileName)
        {
            if (_reserved.Contains(fileName))
                return false;

            string fullPath = Path.Combine(_folder, fileName);
            if (_exists(fullPath))
                return false;

            // Revit DWG may emit {baseName}-{view}.ext even when only the main file was checked.
            if (Directory.Exists(_folder))
            {
                string stem = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                try
                {
                    foreach (string sibling in Directory.GetFiles(_folder, stem + "-*" + ext))
                    {
                        if (_exists(sibling))
                            return false;
                    }
                }
                catch (IOException)
                {
                    return false;
                }
            }

            _reserved.Add(fileName);
            return true;
        }

        private bool IsPathTaken(string fullPath)
        {
            if (!File.Exists(fullPath))
                return false;

            try
            {
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                return true;
            }
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return string.Empty;

            return extension[0] == '.' ? extension : "." + extension;
        }
    }
}
