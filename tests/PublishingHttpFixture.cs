using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mods.ModManager.Tests
{
    /// <summary>A bounded loopback server for the real HTTP client; owns its token and listener.</summary>
    internal sealed class PublishingHttpFixture : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(20));
        private readonly (int Status, string Json)[] replies;
        private readonly string directory;
        public string CatalogUrl { get; }
        public string TokenFile { get; }
        public Task Completion { get; }
        public List<string> Paths { get; } = new();
        public List<string> Bodies { get; } = new();
        public List<string> Bearers { get; } = new();

        public PublishingHttpFixture(params (int Status, string Json)[] responses)
        {
            replies = responses;
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            var origin = $"http://127.0.0.1:{port}/";
            CatalogUrl = origin + "v1/catalog";
            directory = Path.Combine(Path.GetTempPath(), "publishing-http-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            TokenFile = Path.Combine(directory, "author.token");
            File.WriteAllText(TokenFile, new string('A', 1024));
            listener.Prefixes.Add(origin);
            listener.Start();
            Completion = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            foreach (var reply in replies)
            {
                var context = await listener.GetContextAsync().WaitAsync(lifetime.Token);
                Paths.Add(context.Request.HttpMethod + " " + context.Request.RawUrl);
                Bearers.Add(context.Request.Headers["Authorization"]);
                using var reader = new StreamReader(context.Request.InputStream);
                Bodies.Add(await reader.ReadToEndAsync(lifetime.Token));
                context.Response.StatusCode = reply.Status;
                if (reply.Status == 503)
                {
                    context.Response.Headers["Retry-After"] = "0";
                    // A retry must reread the key, just as a key renewed during a transfer.
                    File.WriteAllText(TokenFile, new string('B', 1024));
                }
                if (reply.Status == 302) context.Response.RedirectLocation = "/must-not-follow";
                context.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(reply.Json);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, lifetime.Token);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            lifetime.Cancel();
            listener.Close();
            try { Completion.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
            lifetime.Dispose();
            Directory.Delete(directory, true);
        }
    }
}
