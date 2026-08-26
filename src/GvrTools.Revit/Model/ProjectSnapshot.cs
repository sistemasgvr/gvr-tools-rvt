using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using GvrTools.Core.Naming;

namespace GvrTools.Revit.Model
{
    /// <summary>Document-level information: the output subfolder name and the project-wide tokens.</summary>
    public sealed class ProjectSnapshot
    {
        private ProjectSnapshot(string title, string number, string name, string clientName, string localFolder, string projectKey)
        {
            Title = title;
            Number = number;
            Name = name;
            ClientName = clientName;
            LocalFolder = localFolder;
            ProjectKey = projectKey;
        }

        /// <summary>Revit file title, used as the name of the folder each run writes into.</summary>
        public string Title { get; }

        public string Number { get; }

        public string Name { get; }

        public string ClientName { get; }

        /// <summary>Folder the model lives in, or null for a cloud model. Used as a default destination.</summary>
        public string LocalFolder { get; }

        /// <summary>
        /// Stable, filename-safe identifier for this specific model file, used to key the
        /// per-project export-history store: a short hash of the full path (so two projects with
        /// the same title in different folders never collide) plus a readable, sanitised prefix
        /// (so the stored file is still recognisable when browsing <c>%APPDATA%</c>).
        /// </summary>
        public string ProjectKey { get; }

        public static ProjectSnapshot Read(Document document)
        {
            string title = string.IsNullOrWhiteSpace(document.Title) ? "Proyecto" : document.Title;
            ProjectInfo info = null;

            try
            {
                info = document.ProjectInformation;
            }
            catch (Exception)
            {
                // Some template-derived or cloud documents refuse this; the tokens just stay empty.
            }

            return new ProjectSnapshot(
                title,
                ReadParameter(info, BuiltInParameter.PROJECT_NUMBER),
                ReadParameter(info, BuiltInParameter.PROJECT_NAME),
                ReadParameter(info, BuiltInParameter.CLIENT_NAME),
                ReadLocalFolder(document),
                ComputeProjectKey(document, title));
        }

        /// <summary>Values for the project-scoped file-name tokens, shared by every sheet in a run.</summary>
        public IReadOnlyDictionary<string, string> ToTokens() => new Dictionary<string, string>
        {
            ["ProjectTitle"] = Title,
            ["ProjectNumber"] = Number,
            ["ProjectName"] = Name,
            ["ClientName"] = ClientName,
            ["Date"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        private static string ReadParameter(Element element, BuiltInParameter parameter)
        {
            if (element == null) return string.Empty;

            try
            {
                Parameter found = element.get_Parameter(parameter);
                return found != null && found.HasValue ? found.AsString() ?? string.Empty : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string ReadLocalFolder(Document document)
        {
            try
            {
                string path = document.PathName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return Path.GetDirectoryName(path);
            }
            catch (Exception)
            {
                // Cloud-hosted (BIM 360 / ACC) models have no usable local path.
            }

            return null;
        }

        /// <summary>
        /// Prefers the full file path (stable across sessions, distinguishes same-titled projects
        /// in different folders); falls back to the title for cloud-hosted models with no local
        /// path. Either way the identity source is hashed rather than used raw, since a raw path
        /// sanitised to a filename can exceed Windows' path-length limit or collide after
        /// sanitisation (e.g. two paths that only differ in stripped characters).
        /// </summary>
        private static string ComputeProjectKey(Document document, string title)
        {
            string identitySource;

            try
            {
                identitySource = string.IsNullOrWhiteSpace(document.PathName) ? title : document.PathName;
            }
            catch (Exception)
            {
                identitySource = title;
            }

            string hash;
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(identitySource));
                var sb = new StringBuilder(8);
                for (int i = 0; i < 4; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                hash = sb.ToString();
            }

            return PathSanitizer.SanitizeFileName(title) + "-" + hash;
        }
    }
}
