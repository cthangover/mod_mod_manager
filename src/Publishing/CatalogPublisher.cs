namespace Mods.ModManager
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Providers;
    using Cthangover.Core.Utils;

    /// <summary>
    /// Publishes a private snapshot through the keyserver, re-reading its bearer
    /// on every request. Authentication never follows redirects. The server
    /// assigns ownership; manifest author credits confer no publishing rights.
    /// </summary>
    public static class CatalogPublisher
    {
        /// <summary>Loads the current key's identity and sections without saving them to the catalog cache.</summary>
        public static async Task<PublishingPermissions> GetPermissionsAsync(string catalogUrl, string tokenFile, CancellationToken cancellation)
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var answer = await SendAsync(client, new TransferRetry(PermissionFailures),
                () => CreateRequest(catalogUrl, tokenFile, "publishing"), null, 0, 0, cancellation);
            var permissions = answer.Body.ValueKind == JsonValueKind.Object ? answer.Body.Deserialize<PublishingPermissions>() : null;
            if (string.IsNullOrWhiteSpace(permissions?.AuthorId) || permissions.Sections == null
                || (permissions.SectionPatterns != null && permissions.SectionPatterns.Count > 32) || permissions.MaxArchiveBytes <= 0
                || permissions.AuthorConsent != null && (string.IsNullOrWhiteSpace(permissions.AuthorConsent.Version)
                    || !Uri.TryCreate(permissions.AuthorConsent.Url, UriKind.Absolute, out var consentUrl)
                    || consentUrl.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(consentUrl.Host) || !string.IsNullOrEmpty(consentUrl.UserInfo)))
                throw new InvalidOperationException("mod_manager/publish_failed");
            return permissions;
        }

        /// <summary>
        /// Records an explicit checkbox acceptance on the server before any upload.
        /// Repeats transient failures with the permission request policy; the server
        /// makes retries idempotent and remains the authority for the receipt.
        /// </summary>
        public static async Task AcceptAuthorConsentAsync(string catalogUrl, string tokenFile, string version, CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("mod_manager/publish_consent_required");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var acceptance = new AuthorConsentRequest(catalogUrl, tokenFile, version);
            var answer = await SendAsync(client, new TransferRetry(PermissionFailures), acceptance.Create,
                null, 0, 0, cancellation);
            if (answer.Body.ValueKind != JsonValueKind.Object || !answer.Body.TryGetProperty("accepted", out var accepted)
                || accepted.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("mod_manager/publish_consent_required");
        }

        /// <summary>Attempts of the permission check before the dialog reports a failure.</summary>
        private const int PermissionFailures = 4;

        /// <summary>One PATCH request; a broken one still keeps what reached the server.</summary>
        private const long ChunkBytes = 8L << 20;

        /// <summary>
        /// Packages a folder or copies an existing ZIP/7z, then sends it through
        /// the server's resumable upload session. Interruptions are normal: every
        /// failed request waits per <see cref="TransferRetry"/> and continues at
        /// the offset the server reports; slowness alone never aborts. Publishing
        /// the same unchanged mod again resumes the server's session. The private
        /// temporary snapshot is removed on every outcome. Cancellation stops the
        /// transfer; it also prevents upload after packaging.
        /// </summary>
        public static Task PublishAsync(ModInfo mod, string catalogUrl, string tokenFile, string section,
            CancellationToken cancellation, IProgress<PublishProgress> progress = null)
            => PublishEntityAsync(mod, null, catalogUrl, tokenFile, section, cancellation, progress);

        /// <summary>Uses the same resumable protocol, identity and consent policy for a collection package.</summary>
        public static Task PublishCollectionAsync(ModCollection collection, string catalogUrl, string tokenFile, string section,
            CancellationToken cancellation, IProgress<PublishProgress> progress = null)
            => PublishEntityAsync(null, collection, catalogUrl, tokenFile, section, cancellation, progress);

        private static async Task PublishEntityAsync(ModInfo mod, ModCollection collection, string catalogUrl, string tokenFile, string section,
            CancellationToken cancellation, IProgress<PublishProgress> progress)
        {
            var permissions = await GetPermissionsAsync(catalogUrl, tokenFile, cancellation);
            if (!permissions.IsAdmin && permissions.AuthorConsent is { Accepted: false })
                throw new InvalidOperationException("mod_manager/publish_consent_required");
            if (!permissions.AllowsSection(section))
                throw new InvalidOperationException("mod_manager/publish_denied");
            var directory = Path.Combine(Path.GetTempPath(), "cthangover-publish-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try
            {
                var prepared = await Task.Factory.StartNew(PrepareInBackground, (mod, collection, directory), cancellation,
                    TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                cancellation.ThrowIfCancellationRequested();
                var size = new FileInfo(prepared.Archive).Length;
                if (size > permissions.MaxArchiveBytes)
                    throw new InvalidOperationException("mod_manager/publish_too_large");
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                // No overall timeout: every request has its own stall timer instead.
                using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                var retry = new TransferRetry();
                using var entry = JsonDocument.Parse(prepared.Json);
                var opening = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["kind"] = collection == null ? "mod" : "collection", ["section"] = section, ["file_name"] = Path.GetFileName(prepared.Archive),
                    ["size"] = size, ["entry"] = entry.RootElement
                });
                // Opening the same release again resumes the server's session; a
                // session the server dropped (idle expiry) is simply opened anew.
                while (true)
                {
                    var session = await SendAsync(client, retry, () =>
                    {
                        var request = CreateRequest(catalogUrl, tokenFile, "uploads");
                        request.Method = HttpMethod.Post;
                        request.Content = new StringContent(opening, Encoding.UTF8, "application/json");
                        return request;
                    }, progress, size, 0, cancellation);
                    var uploadId = session.Body.TryGetProperty("upload_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                    if (uploadId is not { Length: 43 } || !uploadId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                        throw new InvalidOperationException("mod_manager/publish_failed");
                    var offset = Offset(session.Body) ?? 0;
                    while (offset >= 0)
                    {
                        offset = await SendBytesAsync(client, retry, catalogUrl, tokenFile, uploadId, prepared.Archive, offset, size, progress, cancellation);
                        if (offset < 0)
                            break;
                        var completion = await SendAsync(client, retry, () =>
                        {
                            var request = CreateRequest(catalogUrl, tokenFile, "uploads/" + uploadId + "/complete");
                            request.Method = HttpMethod.Post;
                            return request;
                        }, progress, size, size, cancellation);
                        if (completion.Status == HttpStatusCode.Created)
                        {
                            if (completion.Body.TryGetProperty("storage_saved", out var saved) && saved.ValueKind == JsonValueKind.False)
                                throw new InvalidOperationException("mod_manager/publish_storage_failed");
                            return;
                        }
                        // Missing bytes (409 with the server's offset) or a lost session (-1).
                        offset = completion.Status == HttpStatusCode.NotFound ? -1 : Offset(completion.Body) ?? 0;
                    }
                    if (!await retry.WaitAsync(null, cancellation))
                        throw new InvalidOperationException("mod_manager/publish_connection");
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        /// <summary>
        /// Sends the archive from <paramref name="offset"/> to its end; returns the
        /// server's final offset, or -1 when the server no longer knows the session.
        /// </summary>
        private static async Task<long> SendBytesAsync(HttpClient client, TransferRetry retry, string catalogUrl, string tokenFile,
            string uploadId, string archive, long offset, long size, IProgress<PublishProgress> progress, CancellationToken cancellation)
        {
            while (offset < size)
            {
                TimeSpan? retryAfter = null;
                try
                {
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    stall.CancelAfter(TransferRetry.StallTimeout);
                    using var request = CreateRequest(catalogUrl, tokenFile, "uploads/" + uploadId);
                    request.Method = HttpMethod.Patch;
                    request.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));
                    using var file = File.OpenRead(archive);
                    file.Position = offset;
                    var length = Math.Min(ChunkBytes, size - offset);
                    var sent = offset;
                    request.Content = new StreamContent(new StallWatchStream(file, length, read =>
                    {
                        stall.CancelAfter(TransferRetry.StallTimeout);
                        sent += read;
                        progress?.Report(new PublishProgress(sent, size, retry.Failures));
                    }));
                    request.Content.Headers.ContentLength = length;
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    using var response = await client.SendAsync(request, stall.Token);
                    var body = await ReadBodyAsync(response, stall.Token);
                    // A nickname refusal is final, even if a response also has
                    // an offset. Never interpret it as upload progress or retry it.
                    if (response.StatusCode == HttpStatusCode.Conflict && ErrorCode(body) == "author_name_taken")
                        ThrowRefusal(response, body);
                    var reported = Offset(body);
                    if (reported is { } next && next >= 0 && next <= size && (response.IsSuccessStatusCode
                        || response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.BadRequest))
                    {
                        // Success, a stale offset or a body broken mid-way: the
                        // server's offset is the truth either way.
                        if (next > offset)
                            retry.Progressed();
                        var moved = next != offset;
                        offset = next;
                        progress?.Report(new PublishProgress(offset, size, retry.Failures));
                        if (response.IsSuccessStatusCode || moved)
                            continue;
                    }
                    if (IsLostSession(response, body))
                        return -1;
                    if (!TransferRetry.IsTransient(response.StatusCode) && response.StatusCode != HttpStatusCode.Conflict)
                        ThrowRefusal(response, body);
                    retryAfter = TransferRetry.RetryAfter(response);
                }
                catch (Exception error) when (TransferRetry.IsTransient(error, cancellation))
                {
                    GameLogger.Log("MOD_MANAGER", $"Upload interrupted: {error.GetType().Name}; attempt {retry.Failures + 1}", LogLevel.Warning);
                }
                progress?.Report(new PublishProgress(offset, size, retry.Failures + 1));
                if (!await retry.WaitAsync(retryAfter, cancellation))
                    throw new InvalidOperationException("mod_manager/publish_connection");
            }
            return offset;
        }

        /// <summary>
        /// Sends a small request until it gets a final answer. Network failures,
        /// overload and a busy session are retried; a refusal throws its locale key.
        /// A completion that reports missing bytes (409 with an offset) is returned.
        /// </summary>
        private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpClient client, TransferRetry retry,
            Func<HttpRequestMessage> create, IProgress<PublishProgress> progress, long size, long sent, CancellationToken cancellation)
        {
            Exception lastFailure = null;
            while (true)
            {
                TimeSpan? retryAfter = null;
                try
                {
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    // Completion hashes up to 200 MiB on the server before it answers.
                    stall.CancelAfter(TransferRetry.StallTimeout * 2);
                    using var request = create();
                    using var response = await client.SendAsync(request, stall.Token);
                    var body = await ReadBodyAsync(response, stall.Token);
                    // A nickname refusal is final, even if a response also has
                    // an offset. Never interpret it as upload progress or retry it.
                    if (response.StatusCode == HttpStatusCode.Conflict && ErrorCode(body) == "author_name_taken")
                        ThrowRefusal(response, body);
                    var busy = response.StatusCode == HttpStatusCode.Conflict && ErrorCode(body) == "upload_busy";
                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict && Offset(body) != null
                        || IsLostSession(response, body))
                        return (response.StatusCode, body);
                    if (!busy && !TransferRetry.IsTransient(response.StatusCode))
                        ThrowRefusal(response, body);
                    lastFailure = new HttpRequestException($"HTTP {(int)response.StatusCode} ({response.StatusCode})");
                    retryAfter = TransferRetry.RetryAfter(response);
                }
                catch (Exception error) when (TransferRetry.IsTransient(error, cancellation))
                {
                    lastFailure = error;
                    GameLogger.Log("MOD_MANAGER", $"Publishing request interrupted: {error.GetType().Name}; attempt {retry.Failures + 1}", LogLevel.Warning);
                }
                progress?.Report(new PublishProgress(sent, size, retry.Failures + 1));
                if (!await retry.WaitAsync(retryAfter, cancellation))
                    throw new InvalidOperationException("mod_manager/publish_connection", lastFailure);
            }
        }

        private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellation)
        {
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : default;
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private static long? Offset(JsonElement body) =>
            body.ValueKind == JsonValueKind.Object && body.TryGetProperty("offset", out var offset)
            && offset.ValueKind == JsonValueKind.Number && offset.TryGetInt64(out var value) ? value : null;

        private static bool IsLostSession(HttpResponseMessage response, JsonElement body) =>
            response.StatusCode == HttpStatusCode.NotFound && ErrorCode(body) == "upload_not_found";

        private static string ErrorCode(JsonElement body) =>
            body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.String ? error.GetString() : null;

        /// <summary>Creates an empty section using the server's current administrator authorization.</summary>
        public static async Task CreateSectionAsync(string catalogUrl, string tokenFile, string section, CancellationToken cancellation)
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            using var request = CreateRequest(catalogUrl, tokenFile, "sections");
            request.Method = HttpMethod.Post;
            request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = section }), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, cancellation);
            await CheckResponseAsync(response, cancellation);
        }

        private static (string Archive, string Json) PrepareInBackground(object state)
        {
            var (mod, collection, directory) = ((ModInfo, ModCollection, string))state;
            return collection == null ? Prepare(mod, directory) : CollectionPackage.Export(collection, directory);
        }

        private static (string Archive, string Json) Prepare(ModInfo mod, string directory)
        {
            if (mod?.FileProvider?.RootPath == null || string.IsNullOrEmpty(mod.Id)
                || mod.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || mod.Id.Contains('/') || mod.Id.Contains('\\') || mod.Id is "." or "..")
                throw new InvalidOperationException("mod_manager/publish_failed");
            var manifest = mod.Manifest ?? throw new InvalidOperationException("mod_manager/publish_failed");
            var entry = new CatalogExportEntry
            {
                Id = mod.Id, Name = mod.Name, Author = mod.Author, Description = mod.Description,
                Version = mod.Version, Category = manifest.Category ?? CategoryType.Other,
                Depends = DependencyStrings(manifest.Depends), ConflictsWith = manifest.ConflictsWith,
                Downloads = new List<CatalogEntryDownload>()
            };
            string archive;
            string manifestJson;
            if (Directory.Exists(mod.FileProvider.RootPath))
            {
                ModCatalogExporter.ExportFromFolder(mod, entry, directory);
                archive = Path.Combine(directory, mod.Id + ".zip");
                using var package = new ZipModFileProvider(archive, mod.Id);
                manifestJson = package.ReadFileText("manifest.json");
            }
            else
            {
                // Keep the original package format and encryption. Snapshot first,
                // then calculate archive metadata from the bytes being uploaded.
                archive = Path.Combine(directory, mod.Id + Path.GetExtension(mod.FileProvider.RootPath));
                File.Copy(mod.FileProvider.RootPath, archive);
                ModCatalogExporter.ExportFromArchive(mod, entry, directory);
                if (entry.Downloads[0].ArchiveSha256 != ModHasher.ComputeArchiveSha256(archive))
                    throw new InvalidOperationException("mod_manager/publish_failed");
                manifestJson = mod.FileProvider.ReadFileText("manifest.json");
            }
            var packagedManifest = JsonSerializer.Deserialize<ModManifest>(manifestJson, JsonOptionsProvider.ReadOptions)
                ?? throw new InvalidOperationException("mod_manager/publish_failed");
            if (!string.IsNullOrEmpty(packagedManifest.Id) && packagedManifest.Id != mod.Id)
                throw new InvalidOperationException("mod_manager/publish_failed");
            entry.Name = packagedManifest.Name;
            entry.Author = packagedManifest.Author;
            entry.Description = packagedManifest.Description;
            entry.Version = packagedManifest.Version;
            entry.Category = packagedManifest.Category ?? CategoryType.Other;
            entry.Depends = DependencyStrings(packagedManifest.Depends);
            entry.ConflictsWith = packagedManifest.ConflictsWith;
            entry.Downloads[0].Version = entry.Version;
            return (archive, JsonSerializer.Serialize(entry, JsonOptionsProvider.WriteIndentedIgnoreNullOptions));
        }

        private static List<string> DependencyStrings(List<ModDependency> dependencies)
        {
            if (dependencies == null)
                return null;
            var values = new List<string>();
            foreach (var dependency in dependencies)
                values.Add(dependency.ToString());
            return values;
        }

        internal static HttpRequestMessage CreateRequest(string catalogUrl, string tokenFile, string operation)
        {
            var uri = new Uri(catalogUrl, UriKind.Absolute);
            if (uri.AbsolutePath != "/v1/catalog" || string.IsNullOrEmpty(tokenFile))
                throw new InvalidOperationException("mod_manager/publish_unsupported");
            var request = CatalogRequest.Create(catalogUrl, tokenFile);
            request.RequestUri = new Uri(uri, "/v1/" + operation);
            return request;
        }

        /// <summary>
        /// Maps a refusal to a locale key. A 403 is a denied key unless the server
        /// reports the author's mod limit (<c>mod_limit_reached</c>); the body is
        /// read only for its error code and is never logged.
        /// </summary>
        private static async Task CheckResponseAsync(HttpResponseMessage response, CancellationToken cancellation)
        {
            if (response.IsSuccessStatusCode)
                return;
            ThrowRefusal(response, await ReadBodyAsync(response, cancellation));
        }

        /// <summary>
        /// Throws a final publishing refusal as a stable locale key. Upload
        /// recovery calls this before accepting offsets for a taken nickname;
        /// the resulting exception is not a transient transport failure.
        /// </summary>
        public static void ThrowRefusal(HttpResponseMessage response, JsonElement body)
        {
            if (response.StatusCode == HttpStatusCode.Conflict && ErrorCode(body) == "author_name_taken")
                throw new InvalidOperationException("mod_manager/publish_author_name");
            if (response.StatusCode == HttpStatusCode.Forbidden && ErrorCode(body) == "author_consent_required"
                || response.StatusCode == HttpStatusCode.BadRequest && ErrorCode(body) == "invalid_consent_version")
                throw new InvalidOperationException("mod_manager/publish_consent_required");
            if (response.StatusCode == HttpStatusCode.Forbidden && ErrorCode(body) == "mod_limit_reached")
                throw new InvalidOperationException("mod_manager/publish_mod_limit");
            var message = response.StatusCode switch
            {
                HttpStatusCode.Forbidden => "mod_manager/publish_denied",
                HttpStatusCode.Conflict => "mod_manager/publish_conflict",
                HttpStatusCode.RequestEntityTooLarge => "mod_manager/publish_too_large",
                HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed => "mod_manager/publish_unsupported",
                HttpStatusCode.TooManyRequests => "mod_manager/publish_rate_limit",
                _ => "mod_manager/publish_failed"
            };
            throw new InvalidOperationException(message);
        }
    }
}
