using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GlossaAI.Core.Services
{
    public class UpdateService
    {
        private readonly HttpClient _httpClient;
        private const string RepoUrl = "https://api.github.com/repos/alessiobianchini/GlossaAI/releases/latest";

        public UpdateService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GlossaAI-Updater/1.0");
        }

        public async Task<(bool IsAvailable, string NewVersion, string DownloadUrl)> CheckForUpdatesAsync()
        {
            try
            {
                var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(RepoUrl);
                if (release == null || string.IsNullOrEmpty(release.TagName))
                    return (false, string.Empty, string.Empty);

                var latestVersionStr = release.TagName.TrimStart('v', 'V');
                if (Version.TryParse(latestVersionStr, out Version latestVersion))
                {
                    var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

                    if (latestVersion > currentVersion)
                    {
                        var asset = release.Assets?.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                        if (asset != null && !string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                        {
                            return (true, latestVersionStr, asset.BrowserDownloadUrl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for updates: {ex.Message}");
            }

            return (false, string.Empty, string.Empty);
        }

        public async Task DownloadAndInstallUpdateAsync(string downloadUrl, Action<int> onProgress)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "GlossaAI-Setup-Update.exe");

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1 && onProgress != null;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            var isMoreToRead = true;
            var totalRead = 0L;

            do
            {
                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    isMoreToRead = false;
                }
                else
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    if (canReportProgress)
                    {
                        var percentage = (int)((totalRead * 100) / totalBytes);
                        onProgress?.Invoke(percentage);
                    }
                }
            }
            while (isMoreToRead);

            // Close the file stream so the installer can execute it without lock issues
            fileStream.Close();

            // Launch the installer (normally, not silent, as requested)
            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });

            // We must exit the current application so the installer can overwrite files
            Environment.Exit(0);
        }

        private class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("assets")]
            public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
        }

        private class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
        }
    }
}
