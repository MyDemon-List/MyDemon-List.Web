using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyDemonList.Web.Services
{
    public class GdBrowserLevelInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string Nom { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string? Auteur { get; set; }

        [JsonPropertyName("difficulty")]
        public string? Difficulte { get; set; }

        [JsonPropertyName("featured")]
        public bool Featured { get; set; }

        [JsonPropertyName("epic")]
        public bool Epic { get; set; }

        [JsonPropertyName("legendary")]
        public bool Legendary { get; set; }

        [JsonPropertyName("mythic")]
        public bool Mythic { get; set; }

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("length")]
        public string? Duree { get; set; }

        [JsonPropertyName("songName")]
        public string? NomMusique { get; set; }

        [JsonPropertyName("songAuthor")]
        public string? AuteurMusique { get; set; }
    }

    public class GdBrowserService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GdBrowserService> _logger;

        public GdBrowserService(HttpClient httpClient, ILogger<GdBrowserService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<GdBrowserLevelInfo?> ObtenirNiveauAsync(string idNiveau, CancellationToken ct = default)
        {
            if (!long.TryParse(idNiveau?.Trim(), out _))
                return null;

            try
            {
                using HttpResponseMessage reponse = await _httpClient.GetAsync($"level/{idNiveau!.Trim()}", ct);
                if (!reponse.IsSuccessStatusCode)
                    return null;

                string contenu = await reponse.Content.ReadAsStringAsync(ct);

                if (string.IsNullOrWhiteSpace(contenu) || contenu.Trim() == "-1")
                    return null;

                GdBrowserLevelInfo? info = JsonSerializer.Deserialize<GdBrowserLevelInfo>(contenu, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                });

                return string.IsNullOrWhiteSpace(info?.Nom) ? null : info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de la récupération du niveau {Id} via GDBrowser", idNiveau);
                return null;
            }
        }
    }
}
