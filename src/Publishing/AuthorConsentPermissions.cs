using System.Text.Json.Serialization;

namespace Mods.ModManager
{
    /// <summary>A server receipt for one immutable consent revision; never persisted by the client.</summary>
    public sealed class AuthorConsentPermissions
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }
        [JsonPropertyName("url")]
        public string Url { get; set; }
        [JsonPropertyName("accepted")]
        public bool Accepted { get; set; }
    }
}
