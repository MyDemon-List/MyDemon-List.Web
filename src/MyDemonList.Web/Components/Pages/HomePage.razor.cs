using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;

namespace MyDemonList.Web.Components.Pages
{
    public partial class HomePage : ComponentBase
    {
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
        private SiteAdminService SiteAdminService { get; set; } = default!;

        [Inject]
        private QuotaService QuotaService { get; set; } = default!;

        private List<Liste> _listesPubliques = [];
        private List<Liste> _listesPossedees = [];
        private List<Liste> _listesGerees = [];
        private List<Liste> _listesSupprimees = [];
        private Dictionary<int, int> _nombreNiveauxParListe = [];
        private Dictionary<int, RoleListe> _rolesParListe = [];
        private HashSet<int> _listesAvecFond = [];

        private int _nombreListesPossedees;
        private bool _estAdminSite;
        private int _paliersListesApprouves;
        private DemandeListesSupplementaires? _derniereDemandeListes;
        private bool _demandeListesEnCours;

        private int LimiteListesActuelle => QuotaService.LimiteListesActuelle(_paliersListesApprouves);
        private int ProchaineLimiteListes => QuotaService.ProchaineLimiteListes(_paliersListesApprouves);
        private bool PeutCreerListeSansDemande => _estAdminSite || _nombreListesPossedees < LimiteListesActuelle;
        private TimeSpan? CooldownRestantListes => QuotaService.CooldownRestant(_derniereDemandeListes);

        private bool _isLoading = true;
        private bool _isAuthenticated;
        private int? _utilisateurId;

        private bool _afficherFormulaireCreation;
        private string _nouveauNom = string.Empty;
        private string _nouvelleDescription = string.Empty;
        private bool _nouvelleEstPublique = true;
        private bool _creationEnCours;
        private string? _erreurCreation;

        protected override async Task OnInitializedAsync()
        {
            AuthenticationState authState = await AuthProvider.GetAuthenticationStateAsync();
            ClaimsPrincipal user = authState.User;
            _isAuthenticated = user.Identity?.IsAuthenticated == true;

            if (_isAuthenticated)
            {
                string? discordId = user.FindFirst("discord:id")?.Value;

                if (!string.IsNullOrWhiteSpace(discordId))
                {
                    using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);
                    DiscordAccount? compte = await dbContext.DiscordAccounts
                        .Include(a => a.Utilisateur)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(a => a.DiscordId == discordId);

                    _utilisateurId = compte?.Utilisateur?.Id;
                }

                if (_utilisateurId is int uid)
                    _estAdminSite = await SiteAdminService.EstAdminOuChefDuSiteAsync(uid);
            }

            await ChargerListes();
        }

        private async Task ChargerListes()
        {
            _isLoading = true;
            _listesGerees = [];
            _listesSupprimees = [];

            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            _listesPubliques = await dbContext.Listes
                .AsNoTracking()
                .Include(l => l.Utilisateur)
                .Where(l => l.EstPublique && dbContext.Niveaux.Any(n => n.ListeId == l.Id))
                .OrderByDescending(l => l.DateCreation)
                .ToListAsync();

            _listesPossedees = _utilisateurId is int uid
                ? await dbContext.Listes
                    .AsNoTracking()
                    .Include(l => l.Utilisateur)
                    .Where(l => l.UtilisateurId == uid)
                    .OrderByDescending(l => l.DateCreation)
                    .ToListAsync()
                : [];

            _nombreListesPossedees = _listesPossedees.Count;

            if (_utilisateurId is int uidArchives)
            {
                _listesSupprimees = await dbContext.Listes
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(l => l.UtilisateurId == uidArchives && l.EstSupprimee)
                    .OrderByDescending(l => l.DateSuppression)
                    .ToListAsync();
            }

            if (_utilisateurId is int uidQuota)
            {
                _paliersListesApprouves = await QuotaService.CompterApprobationsListesAsync(uidQuota);
                _derniereDemandeListes = await QuotaService.DerniereDemandeListesAsync(uidQuota);
                _demandeListesEnCours = _derniereDemandeListes?.Statut == "EnAttente";
            }

            if (_utilisateurId is int uidRoles)
            {
                List<MembreListe> membresListe = await dbContext.MembresListe
                    .AsNoTracking()
                    .Where(m => m.UtilisateurId == uidRoles)
                    .ToListAsync();

                _rolesParListe = membresListe.ToDictionary(m => m.ListeId, m => m.Role);

                List<int> listeIdsAvecRole = membresListe.Select(m => m.ListeId).ToList();
                if (listeIdsAvecRole.Count > 0)
                {
                    List<Liste> listesAvecRole = await dbContext.Listes
                        .AsNoTracking()
                        .Include(l => l.Utilisateur)
                        .Where(l => listeIdsAvecRole.Contains(l.Id))
                        .ToListAsync();

                    _listesGerees = listesAvecRole
                        .Where(l => l.UtilisateurId != uidRoles)
                        .OrderByDescending(l => l.DateCreation)
                        .ToList();
                }
            }

            List<int> listeIds = _listesPubliques.Select(l => l.Id)
                .Concat(_listesPossedees.Select(l => l.Id))
                .Concat(_listesGerees.Select(l => l.Id))
                .Distinct()
                .ToList();

            _nombreNiveauxParListe = await dbContext.Niveaux
                .AsNoTracking()
                .Where(n => listeIds.Contains(n.ListeId))
                .GroupBy(n => n.ListeId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            _isLoading = false;

            _listesAvecFond = _listesPubliques
                .Concat(_listesPossedees)
                .Concat(_listesGerees)
                .Select(l => l.Id)
                .Distinct()
                .Where(NiveauService.HasBackgroundListe)
                .ToHashSet();
        }

        private int NombreDeNiveaux(int listeId) =>
            _nombreNiveauxParListe.GetValueOrDefault(listeId, 0);

        private string LibelleNiveaux(int listeId) =>
            NombreDeNiveaux(listeId) > 1 ? Texte["Niveaux", "niveaux"] : Texte["Niveau", "niveau"];

        private bool AfficherBoutonGerer(Liste liste) =>
            liste.UtilisateurId == _utilisateurId || _rolesParListe.ContainsKey(liste.Id);

        private string NomRole(RoleListe role) => role switch
        {
            RoleListe.Administrateur => Texte["Administrateur", "Administrateur"],
            RoleListe.EditeurNiveaux => Texte["EditeurNiveaux", "Éditeur de niveaux"],
            RoleListe.Moderateur => Texte["Moderateur", "Modérateur"],
            _ => role.ToString()
        };

        private void OuvrirListe(Liste liste)
        {
            ListeSession.SetListe(liste.Id, liste.Nom, liste.DiscordServerUrl);
            NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(SeoUtils.CheminListe(liste.Id, liste.Nom), Texte.CodeLangue));
        }

        private void GererListe(Liste liste, bool ouvrirSurParametres = false)
        {
            ListeSession.SetListe(liste.Id, liste.Nom, liste.DiscordServerUrl);
            string chemin = SeoUtils.CheminGestion(liste.Id, liste.Nom);
            string url = ouvrirSurParametres ? $"{chemin}?onglet=parametres" : chemin;
            NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(url, Texte.CodeLangue));
        }

        private void OuvrirHistoriqueListe(Liste liste)
        {
            ListeSession.SetListe(liste.Id, liste.Nom, liste.DiscordServerUrl);
            string url = $"{SeoUtils.CheminGestion(liste.Id, liste.Nom)}?onglet=historique";
            NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(url, Texte.CodeLangue));
        }

        private async Task DemanderAugmentationListes()
        {
            if (_utilisateurId is not int uid || PeutCreerListeSansDemande || _demandeListesEnCours || CooldownRestantListes is not null)
                return;

            await QuotaService.DemanderListesSupplementairesAsync(uid);

            _paliersListesApprouves = await QuotaService.CompterApprobationsListesAsync(uid);
            _derniereDemandeListes = await QuotaService.DerniereDemandeListesAsync(uid);
            _demandeListesEnCours = _derniereDemandeListes?.Statut == "EnAttente";
        }

        private string FormaterDuree(TimeSpan duree) => DureeUtils.Formater(duree, Texte.CodeLangue);

        private void ToggleFormulaireCreation()
        {
            _afficherFormulaireCreation = !_afficherFormulaireCreation;
            _erreurCreation = null;
        }

        private async Task CreerListe()
        {
            _erreurCreation = null;

            if (_utilisateurId is not int uid)
            {
                _erreurCreation = Texte["CreationConnexionRequise", "Vous devez être connecté pour créer une liste."];
                return;
            }

            string nom = (_nouveauNom ?? string.Empty).Trim();
            if (nom.Length < 3)
            {
                _erreurCreation = Texte["NomListeTropCourt", "Le nom doit contenir au moins 3 caractères."];
                return;
            }

            if (!PeutCreerListeSansDemande)
            {
                _erreurCreation = Texte.Formater("LimiteListes", "Vous avez atteint la limite de {0} listes.", LimiteListesActuelle);
                return;
            }

            _creationEnCours = true;

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                Liste liste = new Liste
                {
                    Nom = nom,
                    Description = string.IsNullOrWhiteSpace(_nouvelleDescription) ? null : _nouvelleDescription.Trim(),
                    EstPublique = _nouvelleEstPublique,
                    UtilisateurId = uid
                };

                dbContext.Listes.Add(liste);
                await dbContext.SaveChangesAsync();

                HistoriqueListeService.Ajouter(
                    dbContext,
                    liste.Id,
                    uid,
                    TypesActionHistoriqueListe.ListeCreee,
                    $"La liste {liste.Nom} a été créée.",
                    HistoriqueListeService.CleSuppression(liste.Id),
                    null,
                    HistoriqueListeService.CapturerSuppression(liste));
                await dbContext.SaveChangesAsync();

                _nouveauNom = string.Empty;
                _nouvelleDescription = string.Empty;
                _nouvelleEstPublique = true;
                _afficherFormulaireCreation = false;

                GererListe(liste, ouvrirSurParametres: true);
            }
            catch (Exception ex)
            {
                _erreurCreation = Texte.Formater("CreationListeImpossible", "Impossible de créer la liste : {0}", ex.Message);
            }
            finally
            {
                _creationEnCours = false;
            }
        }
    }
}
