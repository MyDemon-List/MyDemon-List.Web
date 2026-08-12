using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.JSInterop;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;
using System.Security.Claims;

namespace MyDemonList.Web.Components.Pages
{
    public partial class GererListePage : ComponentBase, IDisposable
    {
        [Parameter]
        public int? ListeId { get; set; }

        [Parameter]
        public string? Slug { get; set; }

        [Inject]
        private DbContextOptions<MyDemonListWebDbContext> DbContextOptions { get; set; } = default!;

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        [Inject]
        private ListeSessionService ListeSession { get; set; } = default!;

        [Inject]
        private AuthenticationStateProvider AuthProvider { get; set; } = default!;

        [Inject]
        private NiveauService NiveauService { get; set; } = default!;

        [Inject]
        private Chargement Chargement { get; set; } = default!;

        [Inject]
        private GdBrowserService GdBrowserService { get; set; } = default!;

        [Inject]
        private LevelThumbnailService LevelThumbnailService { get; set; } = default!;

        [Inject]
        private SiteAdminService SiteAdminService { get; set; } = default!;

        [Inject]
        private QuotaService QuotaService { get; set; } = default!;

        [Inject]
        private NotificationService NotificationService { get; set; } = default!;

        [Inject]
        private IJSRuntime JsRuntime { get; set; } = default!;

        private string ObtenirTitrePage() =>
            ListeSession.ListeNom is string nom ? $"{nom} - Gestion de la liste" : "Gestion de la liste";

        private string ObtenirCheminCanonique() => _listeId > 0
            ? SeoUtils.CheminGestion(_listeId, _listeNom.Length > 0 ? _listeNom : ListeSession.ListeNom ?? "demon-list")
            : "/liste/gerer";

        private enum Onglet { Niveaux, Formulaire, Soumissions, Parametres, Moderation }

        private enum RoleEffectif { Proprietaire, Administrateur, EditeurNiveaux, Moderateur }

        private record LigneNiveau(Classement Classement, Niveau Niveau, List<string> NomsCreateurs);

        private Onglet _ongletActuel = Onglet.Niveaux;
        private RoleEffectif? _roleEffectif;

        private bool PeutVoirOngletNiveaux =>
            _roleEffectif is RoleEffectif.Proprietaire or RoleEffectif.Administrateur or RoleEffectif.EditeurNiveaux;

        private bool PeutModifierNiveaux => PeutVoirOngletNiveaux;
        private bool PeutSupprimerNiveaux => _roleEffectif is RoleEffectif.Proprietaire or RoleEffectif.Administrateur;
        private bool PeutGererSoumissions => _roleEffectif is not null;
        private bool PeutVoirOngletParametres => _roleEffectif == RoleEffectif.Proprietaire;
        private bool PeutGererParametres => _roleEffectif == RoleEffectif.Proprietaire;
        private bool PeutVoirOngletModeration => _roleEffectif is RoleEffectif.Proprietaire or RoleEffectif.Administrateur;

        private bool PeutAssignerRole(RoleListe role) => _roleEffectif switch
        {
            RoleEffectif.Proprietaire => true,
            RoleEffectif.Administrateur => role is RoleListe.EditeurNiveaux or RoleListe.Moderateur,
            _ => false
        };

        private bool PeutRevoquerRole(RoleListe role) => PeutAssignerRole(role);

        private const long TailleMaxBackground = 10 * 1024 * 1024;
        private static readonly string[] ExtensionsAutorisees = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        private bool _isLoading = true;
        private bool _estDispose;
        private bool _estAutorise;
        private int _listeId;
        private int _utilisateurId;
        private bool _listeProprietaireEstAdminSite;

        private int _paliersNiveauxApprouves;
        private DemandeNiveauxSupplementaires? _derniereDemandeNiveaux;
        private bool _demandeNiveauxEnCours;

        private int LimiteNiveauxActuelle => QuotaService.LimiteNiveauxActuelle(_paliersNiveauxApprouves);
        private int ProchaineLimiteNiveaux => QuotaService.ProchaineLimiteNiveaux(_paliersNiveauxApprouves);

        private bool PeutAjouterNiveauSansDemande =>
            _listeProprietaireEstAdminSite || _niveaux.Count < LimiteNiveauxActuelle;

        private TimeSpan? CooldownRestantNiveaux => QuotaService.CooldownRestant(_derniereDemandeNiveaux);

        private List<Niveau> _niveaux = [];
        private List<Classement> _classements = [];
        private List<CreateurNiveau> _createursNiveaux = [];
        private List<Difficulte> _features = [];
        private List<Utilisateur> _utilisateurs = [];
        private Dictionary<int, DiscordAccount> _discordParUtilisateur = [];
        private List<SoumissionNiveau> _soumissions = [];

        private bool _afficherConfirmationSuppression;
        private int? _niveauASupprimerId;

        private readonly Dictionary<int, string> _positionsSaisies = [];
        private string _rechercheNiveau = string.Empty;

        private string _listeNom = string.Empty;
        private string _listeDescription = string.Empty;
        private bool _listeEstPublique = true;
        private string _listeDiscordServerUrl = string.Empty;
        private RawFootageMode _rawFootageMode = RawFootageMode.None;
        private int? _rawFootageTopStart;
        private bool _menuRawFootageOuvert;
        private bool _parametresEnCours;
        private string? _parametresErreur;
        private bool _parametresSauvegardes;
        private byte[]? _backgroundImageBytes;
        private string? _backgroundImageContentType;
        private bool _listeAUneImageDeFond;
        private long _imageDeFondVersion = DateTime.UtcNow.Ticks;

        private static string NomModeRawFootage(RawFootageMode mode) => mode switch
        {
            RawFootageMode.None => "Aucun",
            RawFootageMode.All => "Pour tous les niveaux",
            RawFootageMode.FromTop => "À partir du top",
            _ => mode.ToString()
        };

        private static string DescriptionModeRawFootage(RawFootageMode mode) => mode switch
        {
            RawFootageMode.None => "Aucune vidéo brute n'est demandée.",
            RawFootageMode.All => "Une vidéo brute est requise pour chaque niveau.",
            RawFootageMode.FromTop => "Une vidéo brute est requise à partir d'une position choisie.",
            _ => string.Empty
        };

        private void BasculerMenuRawFootage() => _menuRawFootageOuvert = !_menuRawFootageOuvert;

        private void ChoisirModeRawFootage(RawFootageMode mode)
        {
            _rawFootageMode = mode;
            _menuRawFootageOuvert = false;
        }

        protected override async Task OnInitializedAsync()
        {
            int? listeIdDemande = ListeId ?? ListeSession.ListeId;
            if (listeIdDemande is not int lid)
            {
                NavigationManager.NavigateTo("/");
                return;
            }
            _listeId = lid;

            AuthenticationState authState = await AuthProvider.GetAuthenticationStateAsync();
            ClaimsPrincipal user = authState.User;

            if (user.Identity?.IsAuthenticated != true)
            {
                NavigationManager.NavigateTo("/liste");
                return;
            }

            string? discordId = user.FindFirst("discord:id")?.Value;

            using (MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions))
            {
                DiscordAccount? compte = string.IsNullOrWhiteSpace(discordId)
                    ? null
                    : await dbContext.DiscordAccounts
                        .Include(a => a.Utilisateur)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(a => a.DiscordId == discordId);

                if (compte?.Utilisateur is null)
                {
                    NavigationManager.NavigateTo("/liste");
                    return;
                }

                _utilisateurId = compte.Utilisateur.Id;

                Liste? liste = await dbContext.Listes.AsNoTracking().FirstOrDefaultAsync(l => l.Id == _listeId);
                if (liste is null)
                {
                    NavigationManager.NavigateTo("/liste");
                    return;
                }

                _proprietaireUtilisateurId = liste.UtilisateurId;
                _listeProprietaireEstAdminSite = await SiteAdminService.EstAdminOuChefDuSiteAsync(liste.UtilisateurId);

                if (liste.UtilisateurId == _utilisateurId)
                {
                    _roleEffectif = RoleEffectif.Proprietaire;
                }
                else
                {
                    MembreListe? membre = await dbContext.MembresListe
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.ListeId == _listeId && m.UtilisateurId == _utilisateurId);

                    _roleEffectif = membre?.Role switch
                    {
                        RoleListe.Administrateur => RoleEffectif.Administrateur,
                        RoleListe.EditeurNiveaux => RoleEffectif.EditeurNiveaux,
                        RoleListe.Moderateur => RoleEffectif.Moderateur,
                        _ => null
                    };
                }

                if (_roleEffectif is null)
                {
                    NavigationManager.NavigateTo(liste.EstPublique ? SeoUtils.CheminListe(liste.Id, liste.Nom) : "/");
                    return;
                }

                _estAutorise = true;
                ListeSession.SetListe(liste.Id, liste.Nom, liste.DiscordServerUrl);
                _listeNom = liste.Nom;

                string cheminCanonique = SeoUtils.CheminGestion(liste.Id, liste.Nom);
                string cheminActuel = new Uri(NavigationManager.Uri).AbsolutePath.TrimEnd('/');
                if (!cheminActuel.Equals(cheminCanonique, StringComparison.OrdinalIgnoreCase))
                {
                    NavigationManager.NavigateTo(cheminCanonique, replace: true);
                    return;
                }
            }

            await ChargerDonnees();
            ReinitialiserFormulaire();

            _ongletActuel = PeutVoirOngletNiveaux ? Onglet.Niveaux : Onglet.Soumissions;

            Uri uri = new Uri(NavigationManager.Uri);
            if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("onglet", out StringValues onglet))
            {
                _ongletActuel = onglet.ToString().ToLowerInvariant() switch
                {
                    "parametres" when PeutVoirOngletParametres => Onglet.Parametres,
                    "soumissions" => Onglet.Soumissions,
                    "niveaux" when PeutVoirOngletNiveaux => Onglet.Niveaux,
                    "moderation" when PeutVoirOngletModeration => Onglet.Moderation,
                    _ => _ongletActuel
                };
            }

            _isLoading = false;
        }

        private async Task ChargerDonnees()
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            _niveaux = await dbContext.Niveaux
                .AsNoTracking()
                .Where(n => n.ListeId == _listeId)
                .Include(n => n.Publisher)
                .Include(n => n.Verifieur)
                .Include(n => n.Rating)
                .ToListAsync();

            _classements = await dbContext.Classements
                .AsNoTracking()
                .Where(c => c.ListeId == _listeId)
                .OrderBy(c => c.ClassementPosition)
                .ToListAsync();

            List<int> niveauIds = _niveaux.Select(n => n.Id).ToList();

            _createursNiveaux = await dbContext.CreateursNiveaux
                .AsNoTracking()
                .Where(cn => niveauIds.Contains(cn.NiveauId))
                .Include(cn => cn.Createur)
                .ToListAsync();

            _features = await dbContext.Difficultes.AsNoTracking().OrderBy(d => d.Id).ToListAsync();
            _utilisateurs = await dbContext.Utilisateurs.AsNoTracking().ToListAsync();

            _discordParUtilisateur = await dbContext.DiscordAccounts
                .AsNoTracking()
                .GroupBy(d => d.UtilisateurId)
                .ToDictionaryAsync(g => g.Key, g => g.First());

            _soumissions = await dbContext.SoumissionsNiveaux
                .AsNoTracking()
                .Where(s => niveauIds.Contains(s.NiveauId))
                .Include(s => s.Niveau)
                .OrderBy(s => s.DateSoumission)
                .ToListAsync();

            _soumissionOuverteId = _soumissions.FirstOrDefault()?.IdSoumission;

            if (PeutVoirOngletModeration)
                await ChargerMembres();

            _paliersNiveauxApprouves = await QuotaService.CompterApprobationsNiveauxAsync(_listeId);
            _derniereDemandeNiveaux = await QuotaService.DerniereDemandeNiveauxAsync(_listeId);
            _demandeNiveauxEnCours = _derniereDemandeNiveaux?.Statut == "EnAttente";

            Liste? liste = await dbContext.Listes.AsNoTracking().FirstOrDefaultAsync(l => l.Id == _listeId);
            if (liste != null)
            {
                _listeNom = liste.Nom;
                _listeDescription = liste.Description ?? string.Empty;
                _listeEstPublique = liste.EstPublique;
                _listeDiscordServerUrl = liste.DiscordServerUrl ?? string.Empty;
                _rawFootageMode = liste.RawFootageMode;
                _rawFootageTopStart = liste.RawFootageTopStart;
                _listeAUneImageDeFond = NiveauService.HasBackgroundListe(_listeId);
            }
        }

        private List<LigneNiveau> ObtenirLignes() =>
            _classements
                .OrderBy(c => c.ClassementPosition)
                .Select(c =>
                {
                    Niveau? niveau = _niveaux.FirstOrDefault(n => n.Id == c.NiveauId);
                    if (niveau is null) return null;

                    List<string> noms = _createursNiveaux
                        .Where(cn => cn.NiveauId == c.NiveauId)
                        .Select(cn => cn.Createur?.Nom ?? "?")
                        .ToList();

                    return new LigneNiveau(c, niveau, noms);
                })
                .Where(l => l is not null)
                .Select(l => l!)
                .ToList();

        private List<LigneNiveau> FiltrerLignesParNom(IEnumerable<LigneNiveau> lignes)
        {
            string recherche = _rechercheNiveau.Trim();
            if (string.IsNullOrWhiteSpace(recherche))
                return lignes.ToList();

            return lignes
                .Where(ligne => ligne.Niveau.Nom.Contains(recherche, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private string ObtenirPositionSaisie(LigneNiveau ligne) =>
            _positionsSaisies.TryGetValue(ligne.Classement.Id, out string? saisie)
                ? saisie
                : ligne.Classement.ClassementPosition.ToString();

        private void OnPositionInput(int classementId, ChangeEventArgs e) =>
            _positionsSaisies[classementId] = e.Value?.ToString() ?? "";

        private async Task OnPositionKeyDown(KeyboardEventArgs e, int classementId)
        {
            if (e.Key == "Enter")
                await ValiderPosition(classementId);
        }

        private async Task OnBackgroundImageSelected(InputFileChangeEventArgs e)
        {
            _backgroundImageBytes = null;
            _backgroundImageContentType = null;

            try
            {
                IBrowserFile file = e.File;

                if (file.Size > TailleMaxBackground)
                {
                    _parametresErreur = $"L'image dépasse la taille maximale autorisée ({TailleMaxBackground / (1024 * 1024)} Mo).";
                    return;
                }

                string extension = Path.GetExtension(file.Name).ToLower();
                if (!ExtensionsAutorisees.Contains(extension))
                {
                    _parametresErreur = $"Seules les images {string.Join(", ", ExtensionsAutorisees)} sont autorisées.";
                    return;
                }

                using Stream stream = file.OpenReadStream(TailleMaxBackground);
                using MemoryStream ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                byte[] bytes = ms.ToArray();

                if (!NiveauService.EstImageValide(bytes))
                {
                    _parametresErreur = "Le fichier fourni n'est pas une image valide.";
                    return;
                }

                _backgroundImageBytes = bytes;
                _backgroundImageContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/png" : file.ContentType;
                _parametresErreur = null;
            }
            catch (Exception ex)
            {
                _parametresErreur = $"Erreur lors de la sélection de l'image : {ex.Message}";
            }
        }

        private async Task SauvegarderParametres()
        {
            if (!PeutGererParametres) return;

            _parametresErreur = null;
            _parametresEnCours = true;

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                Liste? liste = await dbContext.Listes.FirstOrDefaultAsync(l => l.Id == _listeId);
                if (liste == null)
                {
                    _parametresErreur = "Liste introuvable.";
                    return;
                }

                string nom = (_listeNom ?? string.Empty).Trim();
                if (nom.Length < 3)
                {
                    _parametresErreur = "Le nom doit contenir au moins 3 caractères.";
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_listeDiscordServerUrl) && !VideoUtils.EstUrlDiscordValide(_listeDiscordServerUrl))
                {
                    _parametresErreur = "Le lien Discord doit être une URL valide vers discord.gg ou discord.com.";
                    return;
                }

                liste.Nom = nom;
                liste.Description = string.IsNullOrWhiteSpace(_listeDescription) ? null : _listeDescription.Trim();
                liste.EstPublique = _listeEstPublique;
                liste.DiscordServerUrl = string.IsNullOrWhiteSpace(_listeDiscordServerUrl) ? null : _listeDiscordServerUrl.Trim();
                liste.RawFootageMode = _rawFootageMode;
                liste.RawFootageTopStart = _rawFootageTopStart;

                if (_backgroundImageBytes != null)
                {
                    bool success = NiveauService.SaveBackgroundListe(_listeId, _backgroundImageBytes);
                    if (!success)
                    {
                        _parametresErreur = "Erreur lors de l'enregistrement de l'image de fond.";
                        return;
                    }
                    _backgroundImageBytes = null;
                    _backgroundImageContentType = null;
                    _listeAUneImageDeFond = true;
                    _imageDeFondVersion = DateTime.UtcNow.Ticks;
                }

                await dbContext.SaveChangesAsync();

                ListeSession.SetListe(liste.Id, liste.Nom, liste.DiscordServerUrl);

                _parametresSauvegardes = true;
                await InvokeAsync(StateHasChanged);
                await Task.Yield();
                await DefilerVersParametresAsync();
            }
            catch (Exception ex)
            {
                _parametresErreur = $"Erreur lors de la sauvegarde : {ex.Message}";
            }
            finally
            {
                _parametresEnCours = false;

                if (_parametresSauvegardes)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        _parametresSauvegardes = false;
                        await InvokeAsync(StateHasChanged);
                    });
                }
            }
        }

        private async Task DefilerVersParametresAsync()
        {
            try
            {
                IJSObjectReference module = await JsRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./Components/Pages/GererListePage.razor.js");

                try
                {
                    await module.InvokeVoidAsync("defilerVersElement", "parametres-haut");
                }
                finally
                {
                    await module.DisposeAsync();
                }
            }
            catch (JSException)
            {
            }
        }

        private async Task DemanderAugmentationNiveaux()
        {
            if (!PeutModifierNiveaux || PeutAjouterNiveauSansDemande || _demandeNiveauxEnCours || CooldownRestantNiveaux is not null)
                return;

            await QuotaService.DemanderNiveauxSupplementairesAsync(_listeId, _utilisateurId);

            _paliersNiveauxApprouves = await QuotaService.CompterApprobationsNiveauxAsync(_listeId);
            _derniereDemandeNiveaux = await QuotaService.DerniereDemandeNiveauxAsync(_listeId);
            _demandeNiveauxEnCours = _derniereDemandeNiveaux?.Statut == "EnAttente";
        }

        private static string FormaterDuree(TimeSpan duree) => DureeUtils.Formater(duree);

        public void Dispose()
        {
            _estDispose = true;
            _gdBrowserDebounceCts?.Cancel();
            _gdBrowserDebounceCts?.Dispose();
        }
    }
}
