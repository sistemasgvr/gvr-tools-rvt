using System;
using System.IO;
using GvrTools.Core.IO;
using Xunit;

namespace GvrTools.Core.Tests
{
    public sealed class ExportPathHelperTests
    {
        [Fact]
        public void ResolveWritableFolder_uses_first_writable_candidate()
        {
            string temp = Path.Combine(Path.GetTempPath(), "gvr-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            try
            {
                string resolved = ExportPathHelper.ResolveWritableFolder(
                    @"C:\Program Files\Autodesk\Revit 2021\Samples\blocked",
                    temp);

                Assert.Equal(temp, resolved);
            }
            finally
            {
                Directory.Delete(temp, recursive: true);
            }
        }

        [Fact]
        public void TryEnsureWritable_succeeds_for_temp_folder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "gvr-export-" + Guid.NewGuid().ToString("N"));

            try
            {
                Assert.True(ExportPathHelper.TryEnsureWritable(temp, out string error), error);
                Assert.True(Directory.Exists(temp));
            }
            finally
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, recursive: true);
            }
        }

        [Fact]
        public void IsWritableDirectory_fails_for_program_files()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string blocked = Path.Combine(programFiles, "GvrToolsWriteProbe-" + Guid.NewGuid().ToString("N"));

            Assert.False(ExportPathHelper.IsWritableDirectory(blocked, out string error));
            Assert.Contains("permiso", error, StringComparison.OrdinalIgnoreCase);
        }
    }
}
