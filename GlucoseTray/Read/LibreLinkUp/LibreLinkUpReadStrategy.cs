namespace GlucoseTray.Read.LibreLinkUp;

internal sealed class LibreLinkUpReadStrategy(AppSettings settings, ILibreLinkUpClient client) : IReadStrategy
{
    public Task<GlucoseReading> GetLatestGlucoseAsync() => client.GetLatestGlucoseAsync(settings);
}
