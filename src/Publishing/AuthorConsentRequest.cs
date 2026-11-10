using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mods.ModManager
{
    /// <summary>Creates a fresh authenticated request for each acceptance retry, rereading the key file.</summary>
    internal sealed class AuthorConsentRequest
    {
        private readonly string catalogUrl;
        private readonly string tokenFile;
        private readonly string json;

        internal AuthorConsentRequest(string catalogUrl, string tokenFile, string version)
        {
            this.catalogUrl = catalogUrl;
            this.tokenFile = tokenFile;
            json = JsonSerializer.Serialize(new Dictionary<string, string> { ["version"] = version });
        }

        internal HttpRequestMessage Create()
        {
            var request = CatalogPublisher.CreateRequest(catalogUrl, tokenFile, "author-consent");
            request.Method = HttpMethod.Post;
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return request;
        }
    }
}
