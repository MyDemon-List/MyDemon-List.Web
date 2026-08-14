using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;
using Npgsql;
using System.Text.RegularExpressions;

namespace MyDemonList.Web.Components.Pages
{
    public partial class GererListePage
    {
        private int? _niveauEnEditionId;
        private string _formNom = string.Empty;
        private string _formIdDuNiveauDansLeJeu = string.Empty;
        private string? _formIdDuNiveauErreur;
        private string _formUrlVerification = string.Empty;
        private int _formDureeMinutes;
        private int _formDureeSecondes;
        private int? _formRatingId;

        public record UtilisateurSuggestion(int Id, string Nom, string? DiscordUsername, string? DiscordDisplayName);

        private string _formPublisherInput = string.Empty;
        private string _formVerifieurInput = string.Empty;
        private List<UtilisateurSuggestion> _formPublisherSuggestions = [];
        private List<UtilisateurSuggestion> _formVerifieurSuggestions = [];

        private string _formCreateurInput = string.Empty;
        private string _formCreateursEnMasse = string.Empty;
        private bool _afficherAjoutCreateursEnMasse;
        private List<UtilisateurSuggestion> _formCreateurSuggestions = [];
        private List<string> _formCreateurs = [];

        private int _formPosition = 1;

        private string? _formMiniatureNiveauBase64;
        private string? _formMiniatureNiveauContentType;

        private bool _formEnCours;
        private string? _formErreur;

        private CancellationTokenSource? _gdBrowserDebounceCts;

        private bool EstPremierNiveau => _niveauEnEditionId is null && _niveaux.Count == 0;
        private bool AfficheChampsDesactive => !EstPremierNiveau || !string.IsNullOrWhiteSpace(_formIdDuNiveauDansLeJeu);

        private const int DelaiDebounceGdBrowserMs = 300;

        private const long TailleMaxImage = 10 * 1024 * 1024;

        private static readonly string[] SuffixesDifficulte =
        [
            " Featured",
            " Epic",
            " Legendary",
            " Mythic"
        ];

        private Difficulte? DifficulteSelectionnee =>
            _features.FirstOrDefault(difficulte => difficulte.Id == _formRatingId);

        private static string ObtenirNomDifficultePrincipale(string nomDifficulte)
        {
            string? suffixe = SuffixesDifficulte.FirstOrDefault(suffixe =>
                nomDifficulte.EndsWith(suffixe, StringComparison.OrdinalIgnoreCase));

            return suffixe is null ? nomDifficulte : nomDifficulte[..^suffixe.Length];
        }

        private static string ObtenirNomVarianteDifficulte(Difficulte difficulte)
        {
            string nomPrincipal = ObtenirNomDifficultePrincipale(difficulte.Nom);
            string variante = difficulte.Nom[nomPrincipal.Length..].Trim();
            return string.IsNullOrEmpty(variante) ? "Rated" : variante;
        }

        private List<Difficulte> ObtenirDifficultesPrincipales(bool demons)
        {
            return _features
                .Where(difficulte => difficulte.Nom == ObtenirNomDifficultePrincipale(difficulte.Nom))
                .Where(difficulte => difficulte.Nom.Contains("Demon", StringComparison.OrdinalIgnoreCase) == demons)
                .ToList();
        }

        private List<Difficulte> ObtenirVariantesDifficulteSelectionnee()
        {
            if (DifficulteSelectionnee is not Difficulte selectionnee)
                return [];

            string nomPrincipal = ObtenirNomDifficultePrincipale(selectionnee.Nom);
            return _features
                .Where(difficulte => string.Equals(
                    ObtenirNomDifficultePrincipale(difficulte.Nom),
                    nomPrincipal,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private bool EstDifficultePrincipaleSelectionnee(Difficulte difficulte)
        {
            return DifficulteSelectionnee is Difficulte selectionnee &&
                string.Equals(
                    ObtenirNomDifficultePrincipale(selectionnee.Nom),
                    difficulte.Nom,
                    StringComparison.OrdinalIgnoreCase);
        }

        private void SelectionnerDifficultePrincipale(Difficulte difficulte)
        {
            string varianteActuelle = DifficulteSelectionnee is Difficulte selectionnee
                ? selectionnee.Nom[ObtenirNomDifficultePrincipale(selectionnee.Nom).Length..]
                : string.Empty;

            Difficulte? memeVariante = _features.FirstOrDefault(candidate =>
                string.Equals(candidate.Nom, difficulte.Nom + varianteActuelle, StringComparison.OrdinalIgnoreCase));

            _formRatingId = (memeVariante ?? difficulte).Id;
        }

        private void SelectionnerVarianteDifficulte(Difficulte difficulte)
        {
            _formRatingId = difficulte.Id;
        }

        private List<UtilisateurSuggestion> FiltrerUtilisateurs(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return [];

            return _utilisateurs
                .Where(u => u.Nom.Contains(texte, StringComparison.OrdinalIgnoreCase) ||
                    (_discordParUtilisateur.TryGetValue(u.Id, out DiscordAccount? d) &&
                        ((d.DiscordUsername != null && d.DiscordUsername.Contains(texte, StringComparison.OrdinalIgnoreCase)) ||
                         (d.DiscordDisplayName != null && d.DiscordDisplayName.Contains(texte, StringComparison.OrdinalIgnoreCase)))))
                .Select(u =>
                {
                    _discordParUtilisateur.TryGetValue(u.Id, out DiscordAccount? d);
                    return new UtilisateurSuggestion(u.Id, u.Nom, d?.DiscordUsername, d?.DiscordDisplayName);
                })
                .Take(5)
                .ToList();
        }

        private void OnPublisherInput(ChangeEventArgs e)
        {
            _formPublisherInput = e.Value?.ToString() ?? string.Empty;
            _formPublisherSuggestions = FiltrerUtilisateurs(_formPublisherInput);
        }

        private void OnVerifieurInput(ChangeEventArgs e)
        {
            _formVerifieurInput = e.Value?.ToString() ?? string.Empty;
            _formVerifieurSuggestions = FiltrerUtilisateurs(_formVerifieurInput);
        }

        private void OnCreateurInput()
        {
            if (ExtraireNomsCreateurs(_formCreateurInput).Length > 1)
            {
                string texteColle = _formCreateurInput.Trim();
                _formCreateursEnMasse = string.IsNullOrWhiteSpace(_formCreateursEnMasse)
                    ? texteColle
                    : $"{_formCreateursEnMasse.TrimEnd()}{Environment.NewLine}{texteColle}";
                _formCreateurInput = string.Empty;
                _formCreateurSuggestions = [];
                _afficherAjoutCreateursEnMasse = true;
                return;
            }

            _formCreateurSuggestions = FiltrerUtilisateurs(_formCreateurInput);
        }

        private void SelectPublisher(UtilisateurSuggestion candidat)
        {
            _formPublisherInput = candidat.Nom;
            _formPublisherSuggestions = [];
        }

        private void SelectVerifieur(UtilisateurSuggestion candidat)
        {
            _formVerifieurInput = candidat.Nom;
            _formVerifieurSuggestions = [];
        }

        private void UtiliserPublieurCommeVerifieur()
        {
            string publieur = _formPublisherInput.Trim();
            if (string.IsNullOrWhiteSpace(publieur)) return;

            _formVerifieurInput = publieur;
            _formVerifieurSuggestions = [];
        }

        private void AjouterPublieurCommeCreateur()
        {
            string publieur = _formPublisherInput.Trim();
            if (string.IsNullOrWhiteSpace(publieur)) return;

            if (!_formCreateurs.Any(c => c.Equals(publieur, StringComparison.OrdinalIgnoreCase)))
                _formCreateurs.Add(publieur);

            _formCreateurInput = string.Empty;
            _formCreateurSuggestions = [];
        }

        private void AjouterCreateur(UtilisateurSuggestion? candidat = null)
        {
            string valeur = (candidat?.Nom ?? _formCreateurInput).Trim();
            if (string.IsNullOrWhiteSpace(valeur)) return;

            if (!_formCreateurs.Any(c => c.Equals(valeur, StringComparison.OrdinalIgnoreCase)))
                _formCreateurs.Add(valeur);

            _formCreateurInput = string.Empty;
            _formCreateurSuggestions = [];
        }

        private void BasculerAjoutCreateursEnMasse()
        {
            _afficherAjoutCreateursEnMasse = !_afficherAjoutCreateursEnMasse;
        }

        private void AjouterCreateursEnMasse()
        {
            string[] noms = ExtraireNomsCreateurs(_formCreateursEnMasse);

            foreach (string nom in noms)
            {
                if (!_formCreateurs.Any(createur => createur.Equals(nom, StringComparison.OrdinalIgnoreCase)))
                    _formCreateurs.Add(nom);
            }

            _formCreateursEnMasse = string.Empty;
            _afficherAjoutCreateursEnMasse = false;
        }

        private static string[] ExtraireNomsCreateurs(string valeur)
        {
            return Regex.Split(valeur.Trim(), @"\r?\n+")
                .Select(nom => nom.Trim())
                .Where(nom => !string.IsNullOrWhiteSpace(nom))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void RetirerCreateur(string nom) => _formCreateurs.Remove(nom);

        private (string? NomAuDessus, string? NomEnDessous) ObtenirVoisinsPosition()
        {
            List<LigneNiveau> lignes = ObtenirLignes();
            int position = Math.Clamp(_formPosition, 1, lignes.Count + 1);

            string? nomAuDessus = lignes.FirstOrDefault(l => l.Classement.ClassementPosition == position - 1)?.Niveau.Nom;
            string? nomEnDessous = lignes.FirstOrDefault(l => l.Classement.ClassementPosition == position)?.Niveau.Nom;

            return (nomAuDessus, nomEnDessous);
        }

        private async Task OnMiniatureNiveauSelected(InputFileChangeEventArgs e)
        {
            try
            {
                _formMiniatureNiveauBase64 = await FichierVersBase64Async(e.File);
                _formMiniatureNiveauContentType = e.File.ContentType;
                _formErreur = null;
            }
            catch (Exception ex)
            {
                _formMiniatureNiveauBase64 = null;
                _formMiniatureNiveauContentType = null;
                _formErreur = ex is IOException
                    ? Texte.Formater("ImageTropGrande", "L'image dépasse la taille maximale autorisée ({0} Mo).", TailleMaxImage / (1024 * 1024))
                    : Texte.Formater("ErreurLectureImage", "Erreur lors de la lecture de l'image : {0}", ex.Message);
            }
        }

        private static async Task<string> FichierVersBase64Async(IBrowserFile file)
        {
            using Stream stream = file.OpenReadStream(TailleMaxImage);
            using MemoryStream ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return Convert.ToBase64String(ms.ToArray());
        }

        private async Task OnIdDuNiveauInput(ChangeEventArgs e)
        {
            _formIdDuNiveauDansLeJeu = e.Value?.ToString() ?? string.Empty;
            _formIdDuNiveauErreur = null;

            _gdBrowserDebounceCts?.Cancel();
            _gdBrowserDebounceCts?.Dispose();
            _gdBrowserDebounceCts = null;

            if (string.IsNullOrWhiteSpace(_formIdDuNiveauDansLeJeu))
                return;

            Niveau? niveauAvecLeMemeId = TrouverNiveauAvecLeMemeId(_formIdDuNiveauDansLeJeu);
            if (niveauAvecLeMemeId is not null)
            {
                _formIdDuNiveauErreur = Texte.Formater("IdNiveauDejaUtiliseNom", "Cet ID est déjà utilisé par le niveau « {0} » dans cette liste.", niveauAvecLeMemeId.Nom);
                return;
            }

            CancellationTokenSource cts = new CancellationTokenSource();
            _gdBrowserDebounceCts = cts;

            try
            {
                await Task.Delay(DelaiDebounceGdBrowserMs, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await RechercherSurGdBrowser(cts.Token);
        }

        private Niveau? TrouverNiveauAvecLeMemeId(string idDuNiveauDansLeJeu)
        {
            string idNormalise = idDuNiveauDansLeJeu.Trim();

            return _niveaux.FirstOrDefault(niveau =>
                niveau.Id != _niveauEnEditionId &&
                string.Equals(niveau.IdDuNiveauDansLeJeu.Trim(), idNormalise, StringComparison.Ordinal));
        }

        private async Task RechercherSurGdBrowser(CancellationToken ct)
        {
            try
            {
                Task<GdBrowserLevelInfo?> infoTask = GdBrowserService.ObtenirNiveauAsync(_formIdDuNiveauDansLeJeu, ct);
                Task<MiniatureNiveauResultat?> miniatureTask = LevelThumbnailService.ObtenirMiniatureAsync(_formIdDuNiveauDansLeJeu, ct);
                Task<int?> dureeTask = GeometryDashDurationService.ObtenirDureeAsync(_formIdDuNiveauDansLeJeu, ct);

                await Task.WhenAll(infoTask, miniatureTask, dureeTask);

                GdBrowserLevelInfo? info = infoTask.Result;
                MiniatureNiveauResultat? miniature = miniatureTask.Result;
                int? duree = dureeTask.Result;

                if (info is null)
                {
                    _formNom = string.Empty;
                    _formPublisherInput = string.Empty;
                    _formPublisherSuggestions = [];
                    _formMiniatureNiveauBase64 = null;
                    _formMiniatureNiveauContentType = null;
                }
                else
                {
                    _formNom = info.Nom;

                    if (!string.IsNullOrWhiteSpace(info.Auteur))
                    {
                        _formPublisherInput = info.Auteur;
                        _formPublisherSuggestions = [];
                    }

                    if (miniature is not null)
                    {
                        _formMiniatureNiveauBase64 = Convert.ToBase64String(miniature.Donnees);
                        _formMiniatureNiveauContentType = miniature.ContentType;
                    }

                    if (!string.IsNullOrWhiteSpace(info.Difficulte))
                    {
                        string suffixe = info switch
                        {
                            { Mythic: true } => " Mythic",
                            { Legendary: true } => " Legendary",
                            { Epic: true } => " Epic",
                            { Featured: true } => " Featured",
                            _ => string.Empty
                        };

                        string nomDifficulteComplet = $"{info.Difficulte}{suffixe}";
                        Difficulte? correspondance = _features.FirstOrDefault(
                            f => string.Equals(f.Nom, nomDifficulteComplet, StringComparison.OrdinalIgnoreCase));

                        if (correspondance is not null)
                            _formRatingId = correspondance.Id;
                    }

                    if (duree is int dureeSecondes)
                    {
                        _formDureeMinutes = dureeSecondes / 60;
                        _formDureeSecondes = dureeSecondes % 60;
                    }
                }

                StateHasChanged();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OuvrirCreation()
        {
            if (_niveauEnEditionId is not null)
            {
                _niveauEnEditionId = null;
                ReinitialiserFormulaire();
            }

            _ongletActuel = Onglet.Formulaire;
        }

        private async Task OuvrirEdition(LigneNiveau ligne)
        {
            _niveauEnEditionId = ligne.Niveau.Id;
            _formNom = ligne.Niveau.Nom;
            _formIdDuNiveauDansLeJeu = ligne.Niveau.IdDuNiveauDansLeJeu;
            _formUrlVerification = ligne.Niveau.UrlVerification;
            _formDureeMinutes = ligne.Niveau.Duree / 60;
            _formDureeSecondes = ligne.Niveau.Duree % 60;
            _formRatingId = ligne.Niveau.RatingId;
            _formPublisherInput = ligne.Niveau.Publisher?.Nom ?? string.Empty;
            _formVerifieurInput = ligne.Niveau.Verifieur?.Nom ?? string.Empty;
            _formPublisherSuggestions = [];
            _formVerifieurSuggestions = [];
            _formCreateurs = [.. ligne.NomsCreateurs];
            _formCreateurInput = string.Empty;
            _formCreateursEnMasse = string.Empty;
            _afficherAjoutCreateursEnMasse = false;
            _formCreateurSuggestions = [];
            _formPosition = ligne.Classement.ClassementPosition;
            _formIdDuNiveauErreur = null;
            _formErreur = null;
            _ongletActuel = Onglet.Formulaire;

            await ChargerMiniatureNiveau(ligne.Niveau.Id);
        }

        private async Task ChargerMiniatureNiveau(int niveauId)
        {
            try
            {
                string filePath = Path.Combine(NiveauService.GetMiniaturePath(), $"{niveauId}.png");
                if (File.Exists(filePath))
                {
                    byte[] imageBytes = await File.ReadAllBytesAsync(filePath);
                    _formMiniatureNiveauBase64 = Convert.ToBase64String(imageBytes);
                    _formMiniatureNiveauContentType = "image/png";
                }
                else
                {
                    _formMiniatureNiveauBase64 = null;
                    _formMiniatureNiveauContentType = null;
                }
            }
            catch (Exception ex)
            {
                _formMiniatureNiveauBase64 = null;
                _formMiniatureNiveauContentType = null;
            }
        }

        private void ReinitialiserFormulaire()
        {
            _niveauEnEditionId = null;
            _formNom = string.Empty;
            _formIdDuNiveauDansLeJeu = string.Empty;
            _formIdDuNiveauErreur = null;
            _formUrlVerification = string.Empty;
            _formDureeMinutes = 0;
            _formDureeSecondes = 0;
            _formRatingId = _features.FirstOrDefault()?.Id;
            _formPublisherInput = string.Empty;
            _formVerifieurInput = string.Empty;
            _formPublisherSuggestions = [];
            _formVerifieurSuggestions = [];
            _formCreateurInput = string.Empty;
            _formCreateursEnMasse = string.Empty;
            _afficherAjoutCreateursEnMasse = false;
            _formCreateurSuggestions = [];
            _formCreateurs = [];
            _formPosition = _classements.Count + 1;
            _formMiniatureNiveauBase64 = null;
            _formMiniatureNiveauContentType = null;
            _formErreur = null;
        }

        private static async Task<Utilisateur> ResoudreOuCreerUtilisateurAsync(MyDemonListWebDbContext dbContext, string nom)
        {
            string nomPropre = nom.Trim();
            Utilisateur? existant = await dbContext.Utilisateurs
                .FirstOrDefaultAsync(u => u.Nom.ToLower() == nomPropre.ToLower());

            if (existant is not null) return existant;

            Utilisateur nouveau = new Utilisateur { Nom = nomPropre };
            dbContext.Utilisateurs.Add(nouveau);
            await dbContext.SaveChangesAsync();
            return nouveau;
        }

        private async Task EnregistrerNiveau()
        {
            if (!PeutModifierNiveaux) return;

            if (_niveauEnEditionId is null && !PeutAjouterNiveauSansDemande)
            {
                _formErreur = Texte.Formater("LimiteNiveauxAugmentation", "Cette liste a atteint la limite de {0} niveaux. Faites une demande d'augmentation depuis cet onglet.", LimiteNiveauxActuelle);
                return;
            }

            _formErreur = null;

            if (string.IsNullOrWhiteSpace(_formNom) || _formNom.Trim().Length < 2)
            {
                _formErreur = Texte["NomNiveauRequis", "Le nom du niveau est requis."];
                return;
            }

            if (string.IsNullOrWhiteSpace(_formIdDuNiveauDansLeJeu))
            {
                _formErreur = Texte["IdNiveauRequis", "L'ID du niveau dans le jeu est requis."];
                return;
            }

            Niveau? niveauAvecLeMemeId = TrouverNiveauAvecLeMemeId(_formIdDuNiveauDansLeJeu);
            if (niveauAvecLeMemeId is not null)
            {
                _formIdDuNiveauErreur = Texte.Formater("IdNiveauDejaUtiliseNom", "Cet ID est déjà utilisé par le niveau « {0} » dans cette liste.", niveauAvecLeMemeId.Nom);
                return;
            }

            if (string.IsNullOrWhiteSpace(_formUrlVerification))
            {
                _formErreur = Texte["UrlVerificationRequise", "L'URL de vérification est requise."];
                return;
            }

            if (!VideoUtils.EstUrlVideoValide(_formUrlVerification))
            {
                _formErreur = Texte["UrlVerificationInvalide", "L'URL de vérification doit pointer vers YouTube, Twitch ou Google Drive."];
                return;
            }

            if (_formRatingId is null)
            {
                _formErreur = Texte["DifficulteRequise", "Veuillez choisir une difficulté."];
                return;
            }

            if (string.IsNullOrWhiteSpace(_formPublisherInput))
            {
                _formErreur = Texte["PublieurRequis", "Le publieur est requis."];
                return;
            }

            if (string.IsNullOrWhiteSpace(_formVerifieurInput))
            {
                _formErreur = Texte["VerifieurRequis", "Le vérifieur est requis."];
                return;
            }

            if (!string.IsNullOrWhiteSpace(_formCreateurInput))
                AjouterCreateur();

            if (!string.IsNullOrWhiteSpace(_formCreateursEnMasse))
                AjouterCreateursEnMasse();

            if (_formCreateurs.Count == 0)
            {
                _formErreur = Texte["CreateurRequis", "Au moins un créateur est requis."];
                return;
            }

            if (string.IsNullOrWhiteSpace(_formMiniatureNiveauBase64))
            {
                _formErreur = Texte["ImageNiveauRequise", "Une image est requise (récupérée automatiquement via l'ID du niveau ou importée manuellement)."];
                return;
            }

            if (!NiveauService.EstImageBase64Valide(_formMiniatureNiveauBase64))
            {
                _formErreur = Texte["ImageInvalide", "Le fichier fourni n'est pas une image valide."];
                return;
            }

            _formEnCours = true;

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                NiveauHistorique? etatAvant = _niveauEnEditionId is int niveauAvantId
                    ? await HistoriqueListeService.CapturerNiveauAsync(dbContext, niveauAvantId)
                    : null;

                string idNiveauDansLeJeu = _formIdDuNiveauDansLeJeu.Trim();
                IQueryable<Niveau> niveauxAvecLeMemeId = dbContext.Niveaux
                    .AsNoTracking()
                    .Where(n => n.ListeId == _listeId && n.IdDuNiveauDansLeJeu == idNiveauDansLeJeu);

                if (_niveauEnEditionId is int niveauEnEditionId)
                    niveauxAvecLeMemeId = niveauxAvecLeMemeId.Where(n => n.Id != niveauEnEditionId);

                if (await niveauxAvecLeMemeId.AnyAsync())
                {
                    _formIdDuNiveauErreur = Texte.Formater("IdNiveauDejaUtilise", "L'ID {0} est déjà utilisé par un autre niveau de cette liste.", idNiveauDansLeJeu);
                    return;
                }

                Utilisateur publisher = await ResoudreOuCreerUtilisateurAsync(dbContext, _formPublisherInput);
                Utilisateur verifieur = await ResoudreOuCreerUtilisateurAsync(dbContext, _formVerifieurInput);

                List<Utilisateur> createurs = new List<Utilisateur>();
                foreach (string nomCreateur in _formCreateurs)
                    createurs.Add(await ResoudreOuCreerUtilisateurAsync(dbContext, nomCreateur));

                int duree = _formDureeMinutes * 60 + _formDureeSecondes;
                Niveau niveau;

                if (_niveauEnEditionId is int idExistant)
                {
                    niveau = await dbContext.Niveaux.FirstAsync(n => n.Id == idExistant);
                    niveau.Nom = _formNom.Trim();
                    niveau.IdDuNiveauDansLeJeu = idNiveauDansLeJeu;
                    niveau.UrlVerification = _formUrlVerification.Trim();
                    niveau.Duree = duree;
                    niveau.RatingId = _formRatingId.Value;
                    niveau.PublisherId = publisher.Id;
                    niveau.VerifieurId = verifieur.Id;

                    IQueryable<CreateurNiveau> anciensCreateurs = dbContext.CreateursNiveaux.Where(cn => cn.NiveauId == idExistant);
                    dbContext.CreateursNiveaux.RemoveRange(anciensCreateurs);
                    foreach (Utilisateur c in createurs)
                        dbContext.CreateursNiveaux.Add(new CreateurNiveau { NiveauId = idExistant, CreateurId = c.Id });

                    await dbContext.SaveChangesAsync();
                }
                else
                {
                    List<Classement> classementsExistants = await dbContext.Classements
                        .Where(c => c.ListeId == _listeId)
                        .ToListAsync();

                    int positionCible = Math.Clamp(_formPosition, 1, classementsExistants.Count + 1);

                    List<(Classement Entite, int NouvellePosition)> aDecaler = classementsExistants
                        .Where(c => c.ClassementPosition >= positionCible)
                        .Select(c => (c, c.ClassementPosition + 1))
                        .ToList();

                    foreach ((Classement entite, int _) in aDecaler) entite.ClassementPosition = -1000 - entite.Id;
                    await dbContext.SaveChangesAsync();

                    foreach ((Classement entite, int position) in aDecaler) entite.ClassementPosition = position;
                    await dbContext.SaveChangesAsync();

                    niveau = new Niveau
                    {
                        Nom = _formNom.Trim(),
                        IdDuNiveauDansLeJeu = idNiveauDansLeJeu,
                        UrlVerification = _formUrlVerification.Trim(),
                        Duree = duree,
                        DateAjout = DateTime.Now,
                        RatingId = _formRatingId.Value,
                        PublisherId = publisher.Id,
                        VerifieurId = verifieur.Id,
                        ListeId = _listeId
                    };

                    dbContext.Niveaux.Add(niveau);
                    await dbContext.SaveChangesAsync();

                    foreach (Utilisateur c in createurs)
                        dbContext.CreateursNiveaux.Add(new CreateurNiveau { NiveauId = niveau.Id, CreateurId = c.Id });

                    dbContext.Classements.Add(new Classement
                    {
                        NiveauId = niveau.Id,
                        ListeId = _listeId,
                        ClassementPosition = positionCible,
                        Points = 0
                    });

                    await dbContext.SaveChangesAsync();
                }

                await RecalculerPointsListeAsync(dbContext, _listeId);

                NiveauHistorique? etatApres = await HistoriqueListeService.CapturerNiveauAsync(dbContext, niveau.Id);
                bool estCreation = etatAvant is null;
                HistoriqueListeService.Ajouter(
                    dbContext,
                    _listeId,
                    _utilisateurId,
                    estCreation ? TypesActionHistoriqueListe.NiveauCree : TypesActionHistoriqueListe.NiveauModifie,
                    estCreation
                        ? $"Le niveau {niveau.Nom} a été ajouté à la liste."
                        : $"Le niveau {niveau.Nom} a été modifié.",
                    HistoriqueListeService.CleNiveaux(_listeId),
                    etatAvant,
                    etatApres);
                await dbContext.SaveChangesAsync();

                if (_formMiniatureNiveauBase64 is not null)
                {
                    NiveauService.SaveMiniatureNiveau(niveau.Id, _formMiniatureNiveauBase64);
                }

                Chargement.ClearCache(_listeId);
                await ChargerDonnees();
                ReinitialiserFormulaire();
                _ongletActuel = Onglet.Niveaux;
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException postgresException &&
                postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                string.Equals(postgresException.ConstraintName, "IX_Niveaux_Liste_IdJeu", StringComparison.OrdinalIgnoreCase))
            {
                _formIdDuNiveauErreur = Texte["IdNiveauConcurrent", "Cet ID vient d'être utilisé par un autre niveau de cette liste."];
            }
            catch (Exception ex)
            {
                _formErreur = Texte.Formater("ErreurEnregistrement", "Erreur lors de l'enregistrement : {0}", ex.Message);
            }
            finally
            {
                _formEnCours = false;
            }
        }
    }
}
