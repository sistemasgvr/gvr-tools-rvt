using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GvrTools.Licensing.Http.Dto;

namespace GvrTools.Licensing.Storage
{
    /// <summary>
    /// Cola de UsageEvent pendientes de enviar (consumo offline). Misma carpeta que license.dat.
    /// </summary>
    public sealed class FileUsageQueueStore
    {
        private readonly string _path;
        private readonly object _gate = new object();

        public FileUsageQueueStore(string path = null)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GVR", "GvrTools");
            Directory.CreateDirectory(dir);
            _path = path ?? Path.Combine(dir, "usage-queue.json");
        }

        public void Enqueue(UsageEventDto item)
        {
            lock (_gate)
            {
                var list = Load();
                list.Add(item);
                Save(list);
            }
        }

        public List<UsageEventDto> PeekAll()
        {
            lock (_gate)
            {
                return Load();
            }
        }

        public void ReplaceAll(List<UsageEventDto> remaining)
        {
            lock (_gate)
            {
                Save(remaining ?? new List<UsageEventDto>());
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
        }

        private List<UsageEventDto> Load()
        {
            if (!File.Exists(_path))
                return new List<UsageEventDto>();

            try
            {
                var bytes = File.ReadAllBytes(_path);
                using (var stream = new MemoryStream(bytes))
                {
                    var serializer = new DataContractJsonSerializer(typeof(UsageQueueFile));
                    var file = (UsageQueueFile)serializer.ReadObject(stream);
                    return file?.Items ?? new List<UsageEventDto>();
                }
            }
            catch
            {
                return new List<UsageEventDto>();
            }
        }

        private void Save(List<UsageEventDto> items)
        {
            var file = new UsageQueueFile { Items = items };
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(UsageQueueFile));
                serializer.WriteObject(stream, file);
                File.WriteAllBytes(_path, stream.ToArray());
            }
        }

        [DataContract]
        private sealed class UsageQueueFile
        {
            [DataMember]
            public List<UsageEventDto> Items { get; set; }
        }
    }
}
