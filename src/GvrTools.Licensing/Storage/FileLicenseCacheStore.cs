using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GvrTools.Licensing.Storage
{
    /// <summary>
    /// Persiste AccessToken + blob firmado en %APPDATA%\GVR\GvrTools\license.dat
    /// (envoltorio local no firmado; la verificación ECDSA corre solo sobre EntitlementJson).
    /// </summary>
    public sealed class FileLicenseCacheStore : ILicenseCacheStore
    {
        private readonly string _path;

        public FileLicenseCacheStore(string path = null)
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GVR", "GvrTools");
            Directory.CreateDirectory(dir);
            _path = path ?? System.IO.Path.Combine(dir, "license.dat");
        }

        public string FilePath => _path;

        public bool TryLoad(out string rawJson, out byte[] signature)
        {
            rawJson = null;
            signature = null;

            if (!TryLoadEnvelope(out var envelope))
                return false;

            if (string.IsNullOrEmpty(envelope.EntitlementJson) ||
                string.IsNullOrEmpty(envelope.EntitlementSignatureBase64))
                return false;

            try
            {
                rawJson = envelope.EntitlementJson;
                signature = Convert.FromBase64String(envelope.EntitlementSignatureBase64);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool TryLoadEnvelope(out LicenseCacheEnvelope envelope)
        {
            envelope = null;
            if (!File.Exists(_path)) return false;

            try
            {
                var bytes = File.ReadAllBytes(_path);
                using (var stream = new MemoryStream(bytes))
                {
                    var serializer = new DataContractJsonSerializer(typeof(LicenseCacheEnvelope));
                    envelope = (LicenseCacheEnvelope)serializer.ReadObject(stream);
                }

                return envelope != null && !string.IsNullOrEmpty(envelope.EntitlementJson);
            }
            catch
            {
                envelope = null;
                return false;
            }
        }

        public void Save(string rawJson, byte[] signature)
        {
            if (!TryLoadEnvelope(out var existing))
                existing = new LicenseCacheEnvelope();

            existing.EntitlementJson = rawJson;
            existing.EntitlementSignatureBase64 = Convert.ToBase64String(signature);
            WriteEnvelope(existing);
        }

        public void SaveEnvelope(LicenseCacheEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            WriteEnvelope(envelope);
        }

        public void Clear()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }

        private void WriteEnvelope(LicenseCacheEnvelope envelope)
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(LicenseCacheEnvelope));
                serializer.WriteObject(stream, envelope);
                File.WriteAllBytes(_path, stream.ToArray());
            }
        }
    }

    [DataContract]
    public sealed class LicenseCacheEnvelope
    {
        [DataMember]
        public string AccessToken { get; set; }

        [DataMember]
        public string EntitlementJson { get; set; }

        [DataMember]
        public string EntitlementSignatureBase64 { get; set; }
    }
}
