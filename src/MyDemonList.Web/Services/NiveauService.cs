using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System.IO;
using System.Text.RegularExpressions;

namespace MyDemonList.Web.Services
{
    public class NiveauService
    {
        private readonly string _niveauDir;
        private readonly string _backgroundListeDir;
        private readonly ILogger<NiveauService> _logger;

        private static readonly Regex DataUriPrefix =
            new(@"^data:image\/[a-zA-Z0-9.+-]+;base64,", RegexOptions.Compiled);

        public NiveauService(IWebHostEnvironment env, ILogger<NiveauService> logger)
        {
            _logger = logger;

#if DEBUG
            string root = Path.Combine(env.ContentRootPath, "wwwroot", "PicturesDev");
#else
            string root = "/var/mydemonlist/images";
#endif

            _niveauDir = Path.Combine(root, "MiniaturesNiveaux");
            _backgroundListeDir = Path.Combine(root, "BackgroundsListes");

            EnsureDirectoryExists(_niveauDir);
            EnsureDirectoryExists(_backgroundListeDir);
        }

        public string GetMiniaturePath() => _niveauDir;
        public string GetBackgroundPath() => _backgroundListeDir;

        private const int DimensionMaxImage = 6000;

        public bool EstImageValide(byte[]? bytes)
        {
            if (bytes is null || bytes.Length == 0)
                return false;

            try
            {
                using Image image = Image.Load(bytes);
                return image.Width <= DimensionMaxImage && image.Height <= DimensionMaxImage;
            }
            catch
            {
                return false;
            }
        }

        public bool EstImageBase64Valide(string? base64Image)
        {
            if (string.IsNullOrWhiteSpace(base64Image))
                return false;

            try
            {
                return EstImageValide(DecodeBase64ToBytes(base64Image));
            }
            catch
            {
                return false;
            }
        }

        public bool SaveMiniatureNiveau(int niveauId, string base64Image)
        {
            if (string.IsNullOrWhiteSpace(base64Image))
                return false;

            try
            {
                return SaveImage(_niveauDir, niveauId, DecodeBase64ToBytes(base64Image), "miniature niveau");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur MiniatureNiveau {Id}", niveauId);
                return false;
            }
        }

        public bool HasBackgroundListe(int listeId) =>
            File.Exists(Path.Combine(_backgroundListeDir, $"{listeId}.png"));

        public bool SaveBackgroundListe(int listeId, byte[] bytes) =>
            SaveImage(_backgroundListeDir, listeId, bytes, "background liste");

        private bool SaveImage(string dossier, int id, byte[] bytes, string libelle)
        {
            if (id <= 0)
                return false;

            EnsureDirectoryExists(dossier);
            string path = Path.Combine(dossier, $"{id}.png");

            try
            {
                using Image image = Image.Load(bytes);

                if (image.Width > DimensionMaxImage || image.Height > DimensionMaxImage)
                {
                    _logger.LogWarning("{Libelle} {Id} rejetée : dimensions {W}x{H}", libelle, id, image.Width, image.Height);
                    return false;
                }

                using MemoryStream ms = new MemoryStream();
                image.Save(ms, new PngEncoder());
                WriteAtomic(path, ms.ToArray());

                _logger.LogInformation("MAJ {Libelle} {Id}", libelle, id);
                return true;
            }
            catch (UnknownImageFormatException ex)
            {
                _logger.LogWarning(ex, "Fichier rejeté pour {Libelle} {Id} : pas une image valide", libelle, id);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur {Libelle} {Id}", libelle, id);
                return false;
            }
        }

        private static byte[] DecodeBase64ToBytes(string input)
        {
            string b64 = DataUriPrefix.Replace(input.Trim(), string.Empty)
                                   .Replace(" ", "+");
            return Convert.FromBase64String(b64);
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            string dir = Path.GetDirectoryName(path)!;
            string tmp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");

            File.WriteAllBytes(tmp, bytes);

            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        private void EnsureDirectoryExists(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    _logger.LogInformation("Dossier créé : {Dir}", dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible de créer le dossier {Dir}", dir);
                throw;
            }
        }
    }
}
