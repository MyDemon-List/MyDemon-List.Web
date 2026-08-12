using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace MyDemonList.Web.Utils
{
    public static class VideoUtils
    {
        public static bool EstUrlValide(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        public static bool EstUrlDiscordValide(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
                return false;

            string hote = uri.Host.ToLowerInvariant();
            return hote is "discord.gg" or "discord.com" or "www.discord.com";
        }

        public static bool EstUrlVideoValide(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
                return false;

            string hote = uri.Host.ToLowerInvariant();
            return hote is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be" or "www.youtu.be"
                or "twitch.tv" or "www.twitch.tv" or "clips.twitch.tv" or "m.twitch.tv"
                or "drive.google.com";
        }

        public static string? ObtenirUrlIframe(string? urlVideo)
        {
            if (!EstUrlValide(urlVideo))
                return null;

            Uri uri = new Uri(urlVideo!, UriKind.Absolute);
            string hote = uri.Host.ToLowerInvariant();

            if (hote is "youtu.be" or "www.youtu.be")
                return $"https://www.youtube.com/embed/{uri.AbsolutePath.Trim('/')}";

            if (hote is "youtube.com" or "www.youtube.com" or "m.youtube.com")
            {
                if (uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
                    return urlVideo;

                if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
                    return $"https://www.youtube.com/embed/{uri.AbsolutePath["/shorts/".Length..].Trim('/')}";

                string? id = QueryHelpers.ParseQuery(uri.Query).TryGetValue("v", out StringValues valeur)
                    ? valeur.ToString()
                    : null;

                if (!string.IsNullOrWhiteSpace(id))
                    return $"https://www.youtube.com/embed/{id}";
            }

            return null;
        }
    }
}
