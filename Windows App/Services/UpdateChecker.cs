using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Voxa.Services
{
    /// <summary>
    /// The app's own version number, shown in the header and used as the baseline for the
    /// update check. Kept as its own tiny type (rather than reading the assembly's version
    /// at runtime) so it always matches exactly what the installer and .csproj claim to ship -
    /// see the "keep these three in sync" note in README.md.
    /// </summary>
    public static class AppVersion
    {
        public static readonly Version Current = new(1, 0, 0);
    }

    /// <summary>Result of a single update check - never throws, always safe to inspect.</summary>
    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; init; }
        public Version? LatestVersion { get; init; }
        public string? ReleaseUrl { get; init; }
    }

    /// <summary>
    /// Quiet, best-effort check against a GitHub repo's Releases API for a newer tagged
    /// version than the one currently running. Entirely optional and entirely silent on
    /// failure: no internet, GitHub being unreachable, a missing/renamed repo, or a
    /// malformed tag all just mean "no update found" - never an exception the caller has
    /// to handle, and never anything that delays or blocks the app opening.
    ///
    /// Before shipping, point RepoOwner/RepoName at your real GitHub repository and tag
    /// releases there like "v1.1.0" (a leading "v" is stripped automatically). Leaving the
    /// placeholder values in place is a safe do-nothing default - the check will simply
    /// 404 every time and no banner will ever appear.
    /// </summary>
    public static class UpdateChecker
    {
        private const string RepoOwner = "your-github-username";
        private const string RepoName = "voxa";

        // Generous but not indefinite - this runs once, silently, in the background right
        // after the main window opens, so it should never be the reason the app feels slow,
        // but a hung connection still shouldn't linger forever.
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

        public static async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = RequestTimeout };
                // GitHub's REST API requires a User-Agent on every request or it responds 403.
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Voxa-UpdateChecker/1.0");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                using var response = await http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return NoUpdate();

                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagProp))
                    return NoUpdate();

                var tag = tagProp.GetString();
                if (string.IsNullOrWhiteSpace(tag))
                    return NoUpdate();

                var versionText = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? tag[1..]
                    : tag;

                if (!Version.TryParse(versionText, out var latest))
                    return NoUpdate();

                if (latest <= AppVersion.Current)
                    return NoUpdate();

                var releaseUrl = root.TryGetProperty("html_url", out var urlProp)
                    ? urlProp.GetString()
                    : $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

                return new UpdateCheckResult
                {
                    UpdateAvailable = true,
                    LatestVersion = latest,
                    ReleaseUrl = releaseUrl
                };
            }
            catch
            {
                // Anything at all going wrong here - no internet, DNS failure, timeout,
                // unexpected JSON shape, GitHub rate-limiting - just means no banner shows.
                // This check is a nice-to-have, never a reason to bother the user.
                return NoUpdate();
            }
        }

        private static UpdateCheckResult NoUpdate() => new() { UpdateAvailable = false };
    }
}
