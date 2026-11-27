using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LanBridge.Core
{
    [DataContract]
    internal sealed class GitHubRelease
    {
        [DataMember(Name = "tag_name")] public string TagName { get; set; }
        [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
    }

    public sealed class UpdateInfo
    {
        public bool IsNewer { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseUrl { get; set; }
    }

    public static class UpdateChecker
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/SMR-2009/lanbridge/releases/latest";

        public static async Task<UpdateInfo> CheckAsync(
            Version current,
            CancellationToken cancellationToken = default)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(8);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LanBridge/" + current);

                using (var response = await client.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Update server returned " + (int)response.StatusCode + ".");

                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var release = ProtocolJson.Deserialize<GitHubRelease>(Encoding.UTF8.GetBytes(json));
                    if (string.IsNullOrWhiteSpace(release.TagName))
                        throw new InvalidDataException("The update response did not include a version.");

                    var latest = ParseTag(release.TagName);
                    return new UpdateInfo
                    {
                        IsNewer = latest > current,
                        LatestVersion = latest.ToString(3),
                        ReleaseUrl = release.HtmlUrl
                    };
                }
            }
        }

        public static bool IsNewer(string tag, Version current)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            try
            {
                return ParseTag(tag) > current;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Version ParseTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                throw new FormatException("The update tag was empty.");

            var value = tag.Trim().TrimStart('v', 'V');
            var separator = value.IndexOf('-');
            if (separator >= 0) value = value.Substring(0, separator);

            return Version.Parse(value);
        }
    }
}
