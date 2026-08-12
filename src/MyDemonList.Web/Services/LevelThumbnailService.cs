namespace MyDemonList.Web.Services
{
    public class MiniatureNiveauResultat
    {
        public required byte[] Donnees { get; init; }
        public required string ContentType { get; init; }
    }

    public class LevelThumbnailService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<LevelThumbnailService> _logger;

        public LevelThumbnailService(HttpClient httpClient, ILogger<LevelThumbnailService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<MiniatureNiveauResultat?> ObtenirMiniatureAsync(string idNiveau, CancellationToken ct = default)
        {
            if (!long.TryParse(idNiveau?.Trim(), out _))
                return null;

            try
            {
                using HttpResponseMessage reponse = await _httpClient.GetAsync($"thumbnail/{idNiveau!.Trim()}", ct);
                if (!reponse.IsSuccessStatusCode)
                    return null;

                string contentType = reponse.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    return null;

                byte[] donnees = await reponse.Content.ReadAsByteArrayAsync(ct);
                return donnees.Length == 0 ? null : new MiniatureNiveauResultat { Donnees = donnees, ContentType = contentType };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de la récupération de la miniature du niveau {Id}", idNiveau);
                return null;
            }
        }
    }
}
