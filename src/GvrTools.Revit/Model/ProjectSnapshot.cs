using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.Revit.DB;

namespace GvrTools.Revit.Model
{
    /// <summary>Document-level information: the output subfolder name and the project-wide tokens.</summary>
    public sealed class ProjectSnapshot
    {
        private ProjectSnapshot(string title, string number, string name, string clientName, string localFolder)
        {
            Title = title;
            Number = number;
            Name = name;
            ClientName = clientName;
            LocalFolder = localFolder;
        }

        /// <summary>Revit file title, used as the name of the folder each run writes into.</summary>
        public string Title { get; }

        public string Number { get; }

        public string Name { get; }

        public string ClientName { get; }

        /// <summary>Folder the model lives in, or null for a cloud model. Used as a default destination.</summary>
        public string LocalFolder { get; }

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
                ReadLocalFolder(document));
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
    }
}
