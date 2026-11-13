using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cthangover.Core.Mods;

namespace Mods.ModManager
{
    /// <summary>Checks one configured destination without hiding it from the form. Failure keys are localizable; details never include a response body or bearer.</summary>
    public sealed class PublishAvailability
    {
        public PublishingPermissions Permissions { get; private init; }
        public string ErrorKey { get; private init; }
        public string ErrorDetail { get; private init; }

        /// <summary>Only explicit user/lifetime cancellation escapes; unavailable and reader-only servers remain inspectable choices.</summary>
        public static async Task<PublishAvailability> CheckAsync(CatalogServer server,
            Func<CatalogServer, CancellationToken, Task<PublishingPermissions>> load, CancellationToken cancellation)
        {
            if (!server.Enabled) return new PublishAvailability { ErrorKey = "mod_manager/publish_server_disabled" };
            if (string.IsNullOrWhiteSpace(server.TokenFile)) return new PublishAvailability { ErrorKey = "mod_manager/publish_no_key" };
            try
            {
                var permissions = await load(server, cancellation);
                if (permissions.AvailableSections.Count == 0 && !permissions.AllowsCustomSection)
                    return new PublishAvailability { ErrorKey = "mod_manager/publish_no_sections" };
                return new PublishAvailability { Permissions = permissions };
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                return new PublishAvailability { ErrorKey = ErrorMessageKey(error), ErrorDetail = ErrorMessageDetail(error) };
            }
        }

        /// <summary>Preserves the publisher's translated refusal, distinguishing file/network failures from denied scopes.</summary>
        public static string ErrorMessageKey(Exception error)
            => error is InvalidOperationException && error.Message.StartsWith("mod_manager/publish_", StringComparison.Ordinal)
                ? error.Message : "mod_manager/publish_unavailable";

        /// <summary>Transport and local file diagnostics only. Server response bodies and arbitrary exception payloads are never displayed.</summary>
        public static string ErrorMessageDetail(Exception error)
        {
            if (error is InvalidOperationException && error.InnerException != null) return ErrorMessageDetail(error.InnerException);
            return error is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException
                ? error.Message : "";
        }
    }
}
