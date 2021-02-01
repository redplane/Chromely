using System;
using System.Text.Json.Serialization;

namespace Chromely.ViewModels
{
    public class BuiltBinaryViewModel
    {
        #region Properties

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("sha1")]
        public string Hash { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("last_modified")]
        public DateTime LastModifiedTime { get; set; }

        #endregion
    }
}