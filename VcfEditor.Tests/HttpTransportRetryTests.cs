using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VcfEditor.Core;

namespace VcfEditor.Tests;

public class HttpTransportRetryTests
{
    private sealed class FlakyHandler : HttpMessageHandler
    {
        private readonly int _failures;
        private int _count;
        public int CallCount => _count;

        public FlakyHandler(int failures)
        {
            _failures = failures;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _count);
            if (n <= _failures)
                throw new HttpRequestException("simulated network error");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
        }
    }

    [Test]
    public async Task Get_is_retried_on_transient_HttpRequestException()
    {
        var handler = new FlakyHandler(failures: 2);
        var transport = new HttpTransport("test-client", handler, "http://localhost");
        transport.SetHmacSecret(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var json = await transport.SendAsync(HttpMethod.Get, "http://localhost/api/v2/status", "/api/v2/status");

        Assert.That(json, Does.Contain("ok"));
        Assert.That(handler.CallCount, Is.EqualTo(3));
    }

    [Test]
    public void Post_is_not_retried_by_transport()
    {
        var handler = new FlakyHandler(failures: 2);
        var transport = new HttpTransport("test-client", handler, "http://localhost");
        transport.SetHmacSecret(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await transport.SendAsync(HttpMethod.Post, "http://localhost/api/v2/backup/create", "/api/v2/backup/create", body: "{}",
                cancellationToken: CancellationToken.None);
        });

        Assert.That(handler.CallCount, Is.EqualTo(1));
    }
}
