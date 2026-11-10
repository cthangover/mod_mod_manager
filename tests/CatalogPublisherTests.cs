namespace Mods.ModManager.Tests
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Testing;

    /// <summary>Final server refusals retain their locale key and cannot enter transport retries.</summary>
    public class CatalogPublisherTests
    {
        [ModTest("publishing/author_name_refusal")]
        static void AuthorNameRefusal(ModTestContext context)
        {
            ExpectRefusal(HttpStatusCode.Conflict, "{\"error\":\"author_name_taken\"}",
                "mod_manager/publish_author_name");
            ExpectRefusal(HttpStatusCode.Conflict, "{\"error\":\"author_name_taken\",\"offset\":0}",
                "mod_manager/publish_author_name");
            ExpectRefusal(HttpStatusCode.Conflict, "{\"error\":\"author_name_taken\",\"offset\":128,\"message\":\"server detail\"}",
                "mod_manager/publish_author_name");
        }

        [ModTest("publishing/other_refusals")]
        static void OtherRefusals(ModTestContext context)
        {
            ExpectRefusal(HttpStatusCode.Conflict, "{\"error\":\"mod_id_reserved\"}",
                "mod_manager/publish_conflict");
            ExpectRefusal(HttpStatusCode.Conflict, "{}", "mod_manager/publish_conflict");
            ExpectRefusal(HttpStatusCode.Conflict, "null", "mod_manager/publish_conflict");
            ExpectRefusal(HttpStatusCode.Forbidden, "{\"error\":\"mod_limit_reached\"}",
                "mod_manager/publish_mod_limit");
            ExpectRefusal(HttpStatusCode.Forbidden, "{\"error\":\"author_name_taken\"}",
                "mod_manager/publish_denied");
        }

        [ModTest("publishing/consent_refusals")]
        static void ConsentRefusals(ModTestContext context)
        {
            ExpectRefusal(HttpStatusCode.Forbidden, "{\"error\":\"author_consent_required\"}", "mod_manager/publish_consent_required");
            ExpectRefusal(HttpStatusCode.BadRequest, "{\"error\":\"invalid_consent_version\"}", "mod_manager/publish_consent_required");
        }

        [ModTest("publishing/consent_http_retry")]
        static async Task ConsentHttpRetry(ModTestContext context)
        {
            using var server = new PublishingHttpFixture((503, "{}"), (200, "{\"accepted\":true}"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await CatalogPublisher.AcceptAuthorConsentAsync(server.CatalogUrl, server.TokenFile, "2026-11-09", timeout.Token);
            await server.Completion;
            ModAssert.Equal(2, server.Paths.Count, "one overload and one successful receipt");
            for (var i = 0; i < server.Paths.Count; i++)
            {
                ModAssert.Equal("POST /v1/author-consent", server.Paths[i], "separate consent endpoint");
                using var body = JsonDocument.Parse(server.Bodies[i]);
                ModAssert.Equal("2026-11-09", body.RootElement.GetProperty("version").GetString(), "exact accepted revision");
            }
            ModAssert.Equal("Bearer " + new string('A', 1024), server.Bearers[0], "first key");
            ModAssert.Equal("Bearer " + new string('B', 1024), server.Bearers[1], "retry rereads renewed key");
        }

        [ModTest("publishing/consent_http_refusals")]
        static async Task ConsentHttpRefusals(ModTestContext context)
        {
            foreach (var response in new[] { (200, "{\"accepted\":false}"), (200, "{}"), (403, "{\"error\":\"author_consent_required\"}"), (400, "{\"error\":\"invalid_consent_version\"}") })
            {
                using var server = new PublishingHttpFixture(response);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                InvalidOperationException failure = null;
                try { await CatalogPublisher.AcceptAuthorConsentAsync(server.CatalogUrl, server.TokenFile, "2026-11-09", timeout.Token); }
                catch (InvalidOperationException error) { failure = error; }
                await server.Completion;
                ModAssert.NotNull(failure, "a receipt is required before uploading");
                ModAssert.Equal("mod_manager/publish_consent_required", failure.Message, "consent refusal key");
                ModAssert.Equal(1, server.Paths.Count, "final refusals never retry");
            }
        }

        [ModTest("publishing/consent_http_redirect")]
        static async Task ConsentHttpRedirect(ModTestContext context)
        {
            using var server = new PublishingHttpFixture((302, "{}"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            InvalidOperationException failure = null;
            try { await CatalogPublisher.AcceptAuthorConsentAsync(server.CatalogUrl, server.TokenFile, "2026-11-09", timeout.Token); }
            catch (InvalidOperationException error) { failure = error; }
            await server.Completion;
            ModAssert.NotNull(failure, "redirect cannot carry the author key");
            ModAssert.Equal(1, server.Paths.Count, "redirect is not followed");
        }

        private static void ExpectRefusal(HttpStatusCode status, string json, string expected)
        {
            using var response = new HttpResponseMessage(status);
            using var body = JsonDocument.Parse(json);
            InvalidOperationException failure = null;
            try
            {
                CatalogPublisher.ThrowRefusal(response, body.RootElement);
            }
            catch (InvalidOperationException error)
            {
                failure = error;
            }
            ModAssert.NotNull(failure, "server refusal must throw");
            ModAssert.Equal(expected, failure.Message, "publishing refusal locale key");
            ModAssert.False(TransferRetry.IsTransient(failure, CancellationToken.None), "final refusal is not retried");
        }
    }
}
