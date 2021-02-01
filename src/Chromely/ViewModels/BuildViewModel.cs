using System.Text.Json.Serialization;

namespace Chromely.ViewModels
{
    public class BuildViewModel
    {
        #region Properties

        [JsonPropertyName("cef_version")]
        public string Version { get; set; }

        [JsonPropertyName("channel")]
        public string Channel { get; set; }

        [JsonPropertyName("chromium_version")]
        public string ChromiumVersion { get; set; }

        [JsonPropertyName("files")]
        public BuiltBinaryViewModel[] Binaries { get; set; }

        #endregion
    }
}