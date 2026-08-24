namespace GvrTools.Licensing.Http.Dto
{
    /// <summary>GET /v1/updates/check?version=&amp;revit=</summary>
    public sealed class UpdateCheckResponse
    {
        public bool UpdateAvailable { get; set; }
        public string LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
    }
}
