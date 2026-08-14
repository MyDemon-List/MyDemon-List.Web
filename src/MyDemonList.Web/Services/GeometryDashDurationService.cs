using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace MyDemonList.Web.Services
{
    public class GeometryDashDurationService
    {
        private const string CachePrefix = "geometry-dash-duration:";
        private const int ImagesParSecondeVerification = 240;
        private const int TailleMaxNiveauDecompresse = 64 * 1024 * 1024;

        private static readonly IReadOnlyDictionary<int, double> VitessesInitiales = new Dictionary<int, double>
        {
            [0] = 311.58,
            [1] = 251.16,
            [2] = 387.42,
            [3] = 468.0,
            [4] = 576.0
        };

        private static readonly IReadOnlyDictionary<int, double> VitessesPortails = new Dictionary<int, double>
        {
            [200] = 251.16,
            [201] = 311.58,
            [202] = 387.42,
            [203] = 468.0,
            [1334] = 576.0
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<GeometryDashDurationService> _logger;

        public GeometryDashDurationService(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<GeometryDashDurationService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
        }

        public async Task<int?> ObtenirDureeAsync(string idNiveau, CancellationToken ct = default)
        {
            if (!long.TryParse(idNiveau?.Trim(), out long id) || id <= 0)
                return null;

            string cacheKey = CachePrefix + id;
            if (_cache.TryGetValue(cacheKey, out DureeCachee? dureeCachee) && dureeCachee is not null)
                return dureeCachee.Secondes;

            Task<int?> historiqueTask = ObtenirDepuisHistoriqueAsync(id, ct);
            int? duree = await ObtenirDepuisServeurOfficielAsync(id, ct);
            duree ??= await historiqueTask;

            _cache.Set(
                cacheKey,
                new DureeCachee(duree),
                duree.HasValue ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(5));

            return duree;
        }

        private async Task<int?> ObtenirDepuisServeurOfficielAsync(long idNiveau, CancellationToken ct)
        {
            try
            {
                HttpClient client = _httpClientFactory.CreateClient("GeometryDashServer");
                using FormUrlEncodedContent corps = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["levelID"] = idNiveau.ToString(CultureInfo.InvariantCulture),
                    ["secret"] = "Wmfd2893gb7",
                    ["gameVersion"] = "22",
                    ["binaryVersion"] = "48"
                });
                corps.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

                using HttpResponseMessage reponse = await client.PostAsync("downloadGJLevel22.php", corps, ct);
                if (!reponse.IsSuccessStatusCode)
                    return null;

                string contenu = await reponse.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(contenu) || contenu.Trim() == "-1" || contenu.TrimStart().StartsWith('<'))
                    return null;

                string niveau = contenu.Split('#', 2)[0];
                Dictionary<string, string> valeurs = LirePaires(niveau, ':');

                if (valeurs.TryGetValue("57", out string? verification) &&
                    long.TryParse(verification, NumberStyles.Integer, CultureInfo.InvariantCulture, out long images) &&
                    images > 0)
                {
                    return ArrondirDuree(images / (double)ImagesParSecondeVerification);
                }

                if (EstPlateforme(valeurs.GetValueOrDefault("15")))
                    return null;

                return valeurs.TryGetValue("4", out string? donnees)
                    ? CalculerDepuisDonneesCompressees(donnees)
                    : null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de la récupération de la durée du niveau {Id} depuis Geometry Dash", idNiveau);
                return null;
            }
        }

        private async Task<int?> ObtenirDepuisHistoriqueAsync(long idNiveau, CancellationToken ct)
        {
            try
            {
                HttpClient client = _httpClientFactory.CreateClient("GeometryDashHistory");
                using HttpResponseMessage reponse = await client.GetAsync($"api/v1/level/{idNiveau}/", ct);
                if (!reponse.IsSuccessStatusCode)
                    return null;

                await using Stream flux = await reponse.Content.ReadAsStreamAsync(ct);
                GeometryDashHistoryLevel? niveau = await JsonSerializer.DeserializeAsync<GeometryDashHistoryLevel>(flux, cancellationToken: ct);

                if (niveau is null || niveau.CacheLength == 5)
                    return null;

                GeometryDashHistoryRecord? version = niveau.Records
                    .Where(record => record.LevelStringAvailable && !record.IsInvalid)
                    .OrderByDescending(record => record.RealDate ?? DateTimeOffset.MinValue)
                    .ThenByDescending(record => record.Id)
                    .FirstOrDefault();

                if (version is null)
                    return null;

                if (version.VerificationFrames is long images && images > 0)
                    return ArrondirDuree(images / (double)ImagesParSecondeVerification);

                using HttpResponseMessage telechargement = await client.GetAsync($"level/{idNiveau}/{version.Id}/download/", ct);
                if (!telechargement.IsSuccessStatusCode)
                    return null;

                string gmd = await telechargement.Content.ReadAsStringAsync(ct);
                string? donnees = ExtraireDonneesGmd(gmd);
                return donnees is null ? null : CalculerDepuisDonneesCompressees(donnees);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de la récupération de la durée du niveau {Id} depuis GDHistory", idNiveau);
                return null;
            }
        }

        private static string? ExtraireDonneesGmd(string contenu)
        {
            if (string.IsNullOrWhiteSpace(contenu))
                return null;

            XDocument document = XDocument.Parse(contenu, LoadOptions.None);
            List<XElement> elements = document.Root?.Elements().ToList() ?? [];

            for (int i = 0; i < elements.Count - 1; i++)
            {
                if (elements[i].Name.LocalName == "k" && elements[i].Value == "k4")
                    return elements[i + 1].Value;
            }

            return null;
        }

        private static int? CalculerDepuisDonneesCompressees(string donnees)
        {
            string? niveau = DecompresserNiveau(donnees);
            return niveau is null ? null : CalculerDepuisNiveau(niveau);
        }

        private static string? DecompresserNiveau(string donnees)
        {
            if (string.IsNullOrWhiteSpace(donnees))
                return null;

            if (donnees.Contains(';'))
                return donnees;

            try
            {
                string base64 = donnees.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
                byte[] compresse = Convert.FromBase64String(base64);

                using MemoryStream entree = new MemoryStream(compresse);
                using GZipStream gzip = new GZipStream(entree, CompressionMode.Decompress);
                using MemoryStream sortie = new MemoryStream();
                byte[] tampon = new byte[81920];
                int total = 0;

                while (true)
                {
                    int lus = gzip.Read(tampon, 0, tampon.Length);
                    if (lus == 0)
                        break;

                    total += lus;
                    if (total > TailleMaxNiveauDecompresse)
                        return null;

                    sortie.Write(tampon, 0, lus);
                }

                return Encoding.UTF8.GetString(sortie.ToArray());
            }
            catch (InvalidDataException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static int? CalculerDepuisNiveau(string niveau)
        {
            string[] parties = niveau.Split(';');
            if (parties.Length < 2)
                return null;

            Dictionary<string, string> entete = LirePaires(parties[0], ',');
            int vitesseInitiale = int.TryParse(entete.GetValueOrDefault("kA4"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int valeurVitesse)
                ? valeurVitesse
                : 0;
            double vitesse = VitessesInitiales.GetValueOrDefault(vitesseInitiale, VitessesInitiales[0]);
            double positionFinale = 0;
            List<PortailVitesse> portails = [];

            for (int i = 1; i < parties.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parties[i]))
                    continue;

                Dictionary<string, string> objet = LirePaires(parties[i], ',');
                if (!double.TryParse(objet.GetValueOrDefault("2"), NumberStyles.Float, CultureInfo.InvariantCulture, out double positionX))
                    continue;

                positionFinale = Math.Max(positionFinale, positionX);

                if (!int.TryParse(objet.GetValueOrDefault("1"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idObjet) ||
                    !VitessesPortails.TryGetValue(idObjet, out double nouvelleVitesse) ||
                    objet.GetValueOrDefault("13") != "1")
                {
                    continue;
                }

                portails.Add(new PortailVitesse(positionX, nouvelleVitesse));
            }

            if (positionFinale <= 0)
                return null;

            double dernierePosition = 0;
            double duree = 0;

            foreach (PortailVitesse portail in portails.OrderBy(portail => portail.PositionX))
            {
                if (portail.PositionX <= dernierePosition || portail.PositionX >= positionFinale)
                    continue;

                duree += (portail.PositionX - dernierePosition) / vitesse;
                vitesse = portail.Vitesse;
                dernierePosition = portail.PositionX;
            }

            duree += (positionFinale - dernierePosition) / vitesse;
            return ArrondirDuree(duree);
        }

        private static Dictionary<string, string> LirePaires(string valeur, char separateur)
        {
            string[] elements = valeur.Split(separateur);
            Dictionary<string, string> resultat = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int i = 0; i + 1 < elements.Length; i += 2)
                resultat[elements[i]] = elements[i + 1];

            return resultat;
        }

        private static bool EstPlateforme(string? longueur) => longueur == "5";

        private static int? ArrondirDuree(double secondes)
        {
            if (!double.IsFinite(secondes) || secondes <= 0 || secondes > int.MaxValue)
                return null;

            return (int)Math.Round(secondes, MidpointRounding.AwayFromZero);
        }

        private sealed record DureeCachee(int? Secondes);
        private sealed record PortailVitesse(double PositionX, double Vitesse);

        private sealed class GeometryDashHistoryLevel
        {
            [JsonPropertyName("cache_length")]
            public int? CacheLength { get; set; }

            [JsonPropertyName("records")]
            public List<GeometryDashHistoryRecord> Records { get; set; } = [];
        }

        private sealed class GeometryDashHistoryRecord
        {
            [JsonPropertyName("id")]
            public long Id { get; set; }

            [JsonPropertyName("is_invalid")]
            public bool IsInvalid { get; set; }

            [JsonPropertyName("level_string_available")]
            public bool LevelStringAvailable { get; set; }

            [JsonPropertyName("real_date")]
            public DateTimeOffset? RealDate { get; set; }

            [JsonPropertyName("timestamp")]
            public long? VerificationFrames { get; set; }
        }
    }
}
