using System.Text.Json.Serialization;

namespace Chromely.ViewModels
{
    public class OsBuildViewModel
    {
        #region Properties

        [JsonPropertyName("versions")]
        public BuildViewModel[] Versions { get; set; }

        #endregion
    }
}