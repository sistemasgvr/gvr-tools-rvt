using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class UpdateDownloadResponse
    {
        [DataMember]
        public string Location { get; set; }
    }
}
