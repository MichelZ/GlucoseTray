using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GlucoseTray.Enums;
using GlucoseTray.Read;
using GlucoseTray.Read.LibreLinkUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GlucoseTray.Tests;

public class LibreLinkUpClientTests
{
    private static AppSettings Settings() => new()
    {
        DataSource = GlucoseSource.LibreLinkUp,
        LibreLinkUpUsername = "follower@example.test",
        LibreLinkUpPassword = "ENC:test-password",
        LibreLinkUpRegion = "EU",
    };

    private static string Login(long? expires = null) => JsonSerializer.Serialize(new
    {
        status = 0,
        data = new { user = new { id = "test-account" }, authTicket = new { token = "test-token", expires = expires ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() } },
    });

    private static string Connections(int? mg = 180, string? timestamp = "10/14/2026 9:30:00 PM", int? arrow = 4, int count = 1) => JsonSerializer.Serialize(new
    {
        status = 0,
        data = Enumerable.Range(1, count).Select(i => new
        {
            patientId = $"patient-{i}",
            glucoseMeasurement = new { FactoryTimestamp = timestamp, Timestamp = "10/14/2026 11:30:00 PM", ValueInMgPerDl = mg, TrendArrow = arrow, Value = 10.0, GlucoseUnits = 1 },
        }),
    });

    private static (LibreLinkUpClient Client, QueueHandler Handler) Create(params string[] responses)
    {
        var handler = new QueueHandler();
        foreach (var response in responses)
            handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(LibreLinkUpClient.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var protector = Substitute.For<ICredentialProtector>();
        protector.Unprotect("ENC:test-password").Returns("test-password");
        return (new LibreLinkUpClient(factory, protector), handler);
    }

    [Test]
    public async Task ShouldMapMgAndUtcIndependentOfServerUnitsAndLocalTimestamp()
    {
        var (client, _) = Create(Login(), Connections());
        var settings = Settings();
        settings.ServerUnitType = GlucoseUnitType.Mmol;
        var reading = await client.GetLatestGlucoseAsync(settings);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.MgValue, Is.EqualTo(180));
            Assert.That(reading.MmolValue, Is.EqualTo(180 / 18.0182f).Within(0.0001));
            Assert.That(reading.TimestampUtc, Is.EqualTo(new DateTime(2026, 10, 14, 21, 30, 0, DateTimeKind.Utc)));
            Assert.That(reading.TimestampUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(reading.Trend, Is.EqualTo(Trend.FortyFiveUp));
        }
    }

    [TestCase(1, Trend.SingleDown)]
    [TestCase(2, Trend.FortyFiveDown)]
    [TestCase(3, Trend.Flat)]
    [TestCase(4, Trend.FortyFiveUp)]
    [TestCase(5, Trend.SingleUp)]
    [TestCase(99, Trend.Unknown)]
    [TestCase(null, Trend.Unknown)]
    public async Task ShouldMapLibreTrendCodes(int? code, Trend expected)
    {
        var (client, _) = Create(Login(), Connections(arrow: code));
        Assert.That((await client.GetLatestGlucoseAsync(Settings())).Trend, Is.EqualTo(expected));
    }

    [Test]
    public async Task ShouldSendRequiredHeadersAndDecryptedPasswordAndReuseToken()
    {
        var (client, handler) = Create(Login(), Connections(), Connections());
        await client.GetLatestGlucoseAsync(Settings());
        await client.GetLatestGlucoseAsync(Settings());
        var login = handler.Requests[0];
        var read = handler.Requests[1];
        using var body = JsonDocument.Parse(login.Body!);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(body.RootElement.GetProperty("password").GetString(), Is.EqualTo("test-password"));
            Assert.That(login.Headers["product"], Is.EqualTo("llu.ios"));
            Assert.That(login.Headers["version"], Is.EqualTo("4.16.0"));
            Assert.That(login.Headers.ContainsKey("Authorization"), Is.False);
            Assert.That(read.Headers["Authorization"], Is.EqualTo("Bearer test-token"));
            Assert.That(read.Headers["account-id"], Is.EqualTo(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("test-account"))).ToLowerInvariant()));
        }
    }

    [Test]
    public async Task ShouldFollowKnownRegionRedirectAndKeepUsingThatHost()
    {
        var (client, handler) = Create("""{"status":0,"data":{"redirect":true,"region":"eu2"}}""", Login(), Connections(), Connections());
        await client.GetLatestGlucoseAsync(Settings());
        await client.GetLatestGlucoseAsync(Settings());
        Assert.That(handler.Requests.Select(r => r.Uri.Host), Is.EqualTo(new[] { "api-eu.libreview.io", "api-eu2.libreview.io", "api-eu2.libreview.io", "api-eu2.libreview.io" }));
    }

    [TestCase("evil.example")]
    [TestCase("EU")]
    public void ShouldRejectUnknownRegionsAndRedirectLoops(string region)
    {
        var redirect = JsonSerializer.Serialize(new { status = 0, data = new { redirect = true, region } });
        var (client, handler) = Create(redirect);
        Assert.ThrowsAsync<InvalidOperationException>(() => client.GetLatestGlucoseAsync(Settings()));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ShouldReauthenticateOnceAfterUnauthorizedResponse()
    {
        var (client, handler) = Create(Login());
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Login()) });
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Connections()) });
        var reading = await client.GetLatestGlucoseAsync(Settings());
        Assert.That(reading.MgValue, Is.EqualTo(180));
        Assert.That(handler.Requests, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task ShouldRenewExpiringSession()
    {
        var (client, handler) = Create(Login(DateTimeOffset.UtcNow.AddSeconds(20).ToUnixTimeSeconds()), Connections(), Login(), Connections());
        await client.GetLatestGlucoseAsync(Settings());
        await client.GetLatestGlucoseAsync(Settings());
        Assert.That(handler.Requests.Count(r => r.Uri.AbsolutePath == "/llu/auth/login"), Is.EqualTo(2));
    }

    [Test]
    public async Task ShouldDropSessionAfterCredentialChange()
    {
        var (client, handler) = Create(Login(), Connections(), Login(), Connections());
        var settings = Settings();
        await client.GetLatestGlucoseAsync(settings);
        settings.LibreLinkUpUsername = "different@example.test";
        await client.GetLatestGlucoseAsync(settings);
        Assert.That(handler.Requests.Count(r => r.Uri.AbsolutePath == "/llu/auth/login"), Is.EqualTo(2));
    }

    [Test]
    public void ShouldNotSilentlySelectFirstOfMultiplePatients()
    {
        var (client, _) = Create(Login(), Connections(count: 2));
        Assert.That(Assert.ThrowsAsync<InvalidOperationException>(() => client.GetLatestGlucoseAsync(Settings()))!.Message, Does.Contain("LibreLinkUpPatientId"));
    }

    [Test]
    public async Task ShouldSelectConfiguredPatient()
    {
        var (client, _) = Create(Login(), """{"status":0,"data":[{"patientId":"patient-1","glucoseMeasurement":{"ValueInMgPerDl":90,"FactoryTimestamp":"10/14/2026 9:30:00 PM"}},{"patientId":"patient-2","glucoseMeasurement":{"ValueInMgPerDl":180,"FactoryTimestamp":"10/14/2026 9:30:00 PM"}}]}""");
        var settings = Settings();
        settings.LibreLinkUpPatientId = "patient-2";
        Assert.That((await client.GetLatestGlucoseAsync(settings)).MgValue, Is.EqualTo(180));
    }

    [TestCase("{\"status\":0,\"data\":[]}")]
    [TestCase("{\"status\":0,\"data\":[{\"patientId\":\"patient-1\"}]}")]
    [TestCase("{\"status\":4,\"data\":{\"private\":\"do-not-log\"}}")]
    [TestCase("not-json-do-not-log")]
    public void ShouldRejectMissingDataAndConsentErrorsWithoutLoggingBodies(string response)
    {
        var (client, _) = Create(Login(), response);
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => client.GetLatestGlucoseAsync(Settings()));
        Assert.That(error!.ToString(), Does.Not.Contain("do-not-log"));
    }

    [TestCase(null, "10/14/2026 9:30:00 PM")]
    [TestCase(0, "10/14/2026 9:30:00 PM")]
    [TestCase(180, null)]
    [TestCase(180, "invalid")]
    [TestCase(180, "10/14/2099 9:30:00 PM")]
    public void ShouldRejectInvalidValuesAndTimes(int? mg, string? timestamp)
    {
        var (client, _) = Create(Login(), Connections(mg, timestamp));
        Assert.ThrowsAsync<InvalidOperationException>(() => client.GetLatestGlucoseAsync(Settings()));
    }

    [Test]
    public async Task ShouldPreserveLastReadingAndItsAgeWhenCloudFailsThroughFactory()
    {
        var (client, _) = Create(Login(), Connections(), """{"status":0,"data":[]}""");
        var options = Substitute.For<IOptionsMonitor<AppSettings>>();
        options.CurrentValue.Returns(Settings());
        var factory = new ReadStrategyFactory(options, Substitute.For<IExternalCommunicationAdapter>(), Substitute.For<IGlucoseReadingMapper>(), Substitute.For<ICredentialProtector>(), client);
        var reader = new GlucoseReader(factory, NullLogger<GlucoseReader>.Instance);
        var first = await reader.GetLatestGlucoseAsync();
        var fallback = await reader.GetLatestGlucoseAsync();
        Assert.That(fallback, Is.SameAs(first));
        Assert.That(fallback.TimestampUtc, Is.LessThan(DateTime.UtcNow.AddMinutes(-15)));
    }

    [Test]
    public void ShouldBoundRetriesWhenAuthenticationKeepsFailing()
    {
        var (client, handler) = Create("""{"status":2}""", """{"status":2}""");
        Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestGlucoseAsync(Settings()));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void ShouldRejectUnmatchedPatientId()
    {
        var (client, _) = Create(Login(), Connections());
        var settings = Settings();
        settings.LibreLinkUpPatientId = "other-patient";
        Assert.ThrowsAsync<InvalidOperationException>(() => client.GetLatestGlucoseAsync(settings));
    }

    private sealed record SentRequest(Uri Uri, Dictionary<string, string> Headers, string? Body);

    private sealed class QueueHandler : HttpMessageHandler
    {
        public Queue<HttpResponseMessage> Responses { get; } = new();
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(request.RequestUri!, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Responses.Dequeue();
        }
    }
}
