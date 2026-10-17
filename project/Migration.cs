
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mentality
{
    public sealed class UpdateInfo
    {
        public Version Version { get; set; }
        public Uri DownloadUri { get; set; }
    }

    public static class Migrate
    {
        private static readonly Uri WebsitePage = new Uri("https://sensationalx.com/tools/apps/mentality/index.html");
        private static readonly Uri GithubLatestRelease = new Uri("https://api.github.com/repos/onegrandband/mentality/releases/latest");
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mentality-Updater/" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            return client;
        }

        public static void UpgradeUserSettings()
        {
            var version = NormalizeVersion(Assembly.GetExecutingAssembly().GetName().Version).ToString();
            var settings = Properties.Settings.Default;
            if (settings.SettingsVersion == version) return;
            settings.Upgrade();
            settings.SettingsVersion = version;
            settings.Save();
        }

        public static async Task<UpdateInfo> CheckForUpdateAsync(Version currentVersion)
        {
            var candidates = new List<UpdateInfo>();
            var errors = new List<Exception>();
            var sourceResponded = false;

            try
            {
                var html = await Client.GetStringAsync(WebsitePage).ConfigureAwait(false);
                sourceResponded = true;
                candidates.AddRange(ParseWebsite(html));
            }
            catch (Exception ex) { errors.Add(ex); }

            try
            {
                var json = await Client.GetStringAsync(GithubLatestRelease).ConfigureAwait(false);
                sourceResponded = true;
                var release = ParseGithubRelease(json);
                if (release != null) candidates.Add(release);
            }
            catch (Exception ex) { errors.Add(ex); }

            if (candidates.Count == 0)
            {
                if (!sourceResponded && errors.Count > 0)
                    throw new AggregateException("Could not contact either update source.", errors);

                const string message = "Update sources responded, but no stable Windows release with a valid download link was found.";
                if (errors.Count > 0) throw new InvalidDataException(message, new AggregateException(errors));
                throw new InvalidDataException(message);
            }

            UpdateInfo latest = null;
            foreach (var candidate in candidates)
            {
                if (latest == null || candidate.Version > latest.Version) latest = candidate;
            }

            return latest != null && latest.Version > NormalizeVersion(currentVersion) ? latest : null;
        }

        private static List<UpdateInfo> ParseWebsite(string html)
        {
            var updates = new List<UpdateInfo>();
            var rows = Regex.Matches(html, "<tr\\b[^>]*data-os\\s*=\\s*[\\\"']Windows[\\\"'][^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match row in rows)
            {
                var cells = Regex.Matches(row.Groups["row"].Value, "<td\\b[^>]*>(?<cell>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (cells.Count < 8) continue;
                var versionText = StripHtml(cells[2].Groups["cell"].Value);
                var releaseType = StripHtml(cells[4].Groups["cell"].Value);
                if (!releaseType.Equals("Stable", StringComparison.OrdinalIgnoreCase)) continue;

                var link = Regex.Match(cells[7].Groups["cell"].Value, "href\\s*=\\s*[\\\"'](?<url>[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase);
                if (!link.Success) continue;
                Uri downloadUri;
                if (!Uri.TryCreate(WebsitePage, WebUtility.HtmlDecode(link.Groups["url"].Value), out downloadUri) || !IsTrustedExecutableUri(downloadUri)) continue;

                Version version;
                if (TryParseVersion(versionText, out version))
                    updates.Add(new UpdateInfo { Version = version, DownloadUri = downloadUri });
            }
            return updates;
        }

        private static UpdateInfo ParseGithubRelease(string json)
        {
            var versionMatch = Regex.Match(json, "\\\"tag_name\\\"\\s*:\\s*\\\"v?(?<version>[0-9]+(?:\\.[0-9]+){1,3})\\\"", RegexOptions.IgnoreCase);
            if (!versionMatch.Success) return null;

            Version version;
            if (!TryParseVersion(versionMatch.Groups["version"].Value, out version)) return null;

            Uri downloadUri = null;
            var assetMatches = Regex.Matches(json, "\\\"browser_download_url\\\"\\s*:\\s*\\\"(?<url>[^\\\"]+\\.exe(?:\\?[^\\\"]*)?)\\\"", RegexOptions.IgnoreCase);
            foreach (Match asset in assetMatches)
            {
                Uri candidate;
                if (Uri.TryCreate(asset.Groups["url"].Value, UriKind.Absolute, out candidate) && IsTrustedExecutableUri(candidate))
                {
                    downloadUri = candidate;
                    break;
                }
            }

            if (downloadUri == null)
            {
                var bodyMatch = Regex.Match(json, "\\\"body\\\"\\s*:\\s*\\\"(?<body>(?:\\\\.|[^\\\"\\\\])*)\\\"", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (bodyMatch.Success)
                {
                    var body = Regex.Unescape(bodyMatch.Groups["body"].Value);
                    var links = Regex.Matches(body, "https?://[^\\s\\\"'<>\\]\\)]+?\\.exe(?:\\?[^\\s\\\"'<>\\]\\)]*)?", RegexOptions.IgnoreCase);
                    foreach (Match link in links)
                    {
                        Uri candidate;
                        if (Uri.TryCreate(link.Value.TrimEnd('.', ','), UriKind.Absolute, out candidate) && IsTrustedExecutableUri(candidate))
                        {
                            downloadUri = candidate;
                            break;
                        }
                    }
                }
            }

            return downloadUri == null ? null : new UpdateInfo { Version = version, DownloadUri = downloadUri };
        }

        private static string StripHtml(string value)
        {
            return WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", string.Empty)).Trim();
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            value = (value ?? string.Empty).Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            Version parsed;
            if (!Version.TryParse(value, out parsed) || parsed.Major < 0 || parsed.Minor < 0)
            {
                version = null;
                return false;
            }
            version = NormalizeVersion(parsed);
            return true;
        }

        private static Version NormalizeVersion(Version version)
        {
            if (version == null) return new Version(0, 0, 0);
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        private static bool IsTrustedExecutableUri(Uri uri)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            var host = uri.Host.ToLowerInvariant();
            return host == "sensationalx.com" || host == "github.com" || host == "objects.githubusercontent.com" || host == "release-assets.githubusercontent.com";
        }

        public static async Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<string> progress)
        {
            if (update == null || !IsTrustedExecutableUri(update.DownloadUri)) throw new InvalidOperationException("The update download link is not trusted.");
            if (progress != null) progress.Report("Downloading update...");

            using (var response = await Client.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (!IsTrustedExecutableUri(response.RequestMessage.RequestUri)) throw new InvalidOperationException("The download redirected to an untrusted location.");
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > 100 * 1024 * 1024)
                    throw new InvalidOperationException("The update file is unexpectedly large.");

                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mentality", "Updates");
                Directory.CreateDirectory(directory);
                var packagePath = Path.Combine(directory, "Mentality_update_" + Guid.NewGuid().ToString("N") + ".exe");
                try
                {
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long totalBytes = 0;
                        int bytesRead;
                        while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            totalBytes += bytesRead;
                            if (totalBytes > 100 * 1024 * 1024) throw new InvalidDataException("The update file is unexpectedly large.");
                            await output.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                        }
                    }

                    var info = new FileInfo(packagePath);
                    if (info.Length < 16 * 1024 || info.Length > 100 * 1024 * 1024)
                        throw new InvalidDataException("The downloaded update has an invalid file size.");

                    var downloadedVersion = NormalizeVersion(AssemblyName.GetAssemblyName(packagePath).Version);
                    if (downloadedVersion != update.Version)
                        throw new InvalidDataException("The downloaded executable version does not match the release version.");
                    return packagePath;
                }
                catch
                {
                    try { File.Delete(packagePath); } catch { }
                    throw;
                }
            }
        }

        public static void StartInstall(string packagePath, string targetPath, int parentProcessId)
        {
            if (!File.Exists(packagePath)) throw new FileNotFoundException("The downloaded update was not found.", packagePath);
            if (string.IsNullOrWhiteSpace(targetPath) || !Path.IsPathRooted(targetPath)) throw new ArgumentException("The application path is invalid.", nameof(targetPath));

            var arguments = "--migrate " + parentProcessId + " " + QuoteArgument(Path.GetFullPath(targetPath));
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(packagePath),
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(packagePath)),
                UseShellExecute = true
            });
        }

        public static bool TryApplyUpdateCommand(string[] args)
        {
            if (args == null || args.Length != 3 || !args[0].Equals("--migrate", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                int parentProcessId;
                if (!int.TryParse(args[1], out parentProcessId) || parentProcessId <= 0) throw new ArgumentException("The update process ID is invalid.");
                ApplyUpdate(parentProcessId, Path.GetFullPath(args[2]));
            }
            catch (UnauthorizedAccessException) when (!IsAdministrator())
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = "--migrate " + args[1] + " " + QuoteArgument(Path.GetFullPath(args[2])),
                        WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath),
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return true;
        }

        private static void ApplyUpdate(int parentProcessId, string targetPath)
        {
            if (string.Equals(Path.GetFullPath(Application.ExecutablePath), targetPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The update package and installed application paths must be different.");

            try
            {
                using (var parent = Process.GetProcessById(parentProcessId))
                {
                    if (!parent.WaitForExit(60000)) throw new TimeoutException("Mentality did not close in time to install the update.");
                }
            }
            catch (ArgumentException) { }

            var sourcePath = Path.GetFullPath(Application.ExecutablePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            var stagedPath = targetPath + ".update";
            var backupPath = targetPath + ".bak";
            File.Copy(sourcePath, stagedPath, true);

            var hadTarget = File.Exists(targetPath);
            if (hadTarget)
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Replace(stagedPath, targetPath, backupPath);
            }
            else
            {
                File.Move(stagedPath, targetPath);
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetPath,
                    WorkingDirectory = targetDirectory,
                    UseShellExecute = true
                });
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
            catch
            {
                if (hadTarget && File.Exists(backupPath)) File.Copy(backupPath, targetPath, true);
                throw;
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
