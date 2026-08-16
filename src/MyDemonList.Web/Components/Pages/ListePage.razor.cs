using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;
using System.Security.Claims;

namespace MyDemonList.Web.Components.Pages
{
    public partial class ListePage : IAsyncDisposable
    {
        [Parameter]
        public int? ListeId { get; set; }

        [Parameter]
        public string? Slug { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "niveau")]
        public string? NiveauSelectionne { get; set; }

        [Inject]
        private DbContextOptions<MyDemonListWebDbContext> DbContextOptions { get; set; } = default!;

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        [Inject]
        private Chargement Chargement { get; set; } = default!;

        [Inject]
        private ListeSessionService ListeSession { get; set; } = default!;

        [Inject]
        private AuthenticationStateProvider AuthProvider { get; set; } = default!;

        [Inject]
        private IJSRuntime JsRuntime { get; set; } = default!;

        private HashSet<int> _niveauxReussisParUtilisateurConnecte = [];
        private HashSet<int> _niveauxEnAttenteParUtilisateurConnecte = [];
        private bool _peutFiltrerNiveauxNonTermines;
        private bool _afficherSeulementNiveauxNonTermines;
        private bool _idNiveauCopie;
        private CancellationTokenSource? _idNiveauCopieCts;
        private IJSObjectReference? _jsModule;
        private ElementReference _articleElement;
        private bool _vueMobileDetail;
        private string? _dernierParametreNiveauMobile;
        private bool _vueMobileInitialisee;
        private bool _remonterVueMobileApresRendu;

        private List<Utilisateur> _listeEntiereUtilisateurs = [];
        private List<Niveau> _listeEntiereNiveaux = [];
        private List<Classement> _listeEntiereClassements = [];
        private List<CreateurNiveau> _listeEntiereCreateursNiveaux = [];
        private List<ReussiteNiveau> _listeEntiereReussitesNiveaux = [];
        private List<Difficulte> _listeEntiereFeatures = [];
        private List<Niveau> _niveaux = [];
        private List<Niveau> _niveauxFiltres = [];
        private List<Classement> _classements = [];
        private List<ReussiteNiveau> _reussitesNiveau = [];
        private List<Utilisateur> _createurs = [];
        private Liste? _listeCourante;
        private int _listeId;

        private bool NePasIndexer => _listeCourante?.EstPublique != true || _niveaux.Count == 0;

        private string ObtenirTitrePage()
        {
            string nomListe = _listeCourante?.Nom ?? ListeSession.ListeNom ?? "Demon list";

            if (_niveauSelectionne is null)
                return $"{nomListe}";

            int? position = _classements.FirstOrDefault(c => c.NiveauId == _niveauSelectionne.Id)?.ClassementPosition;
            return position is int p
                ? $"{nomListe} - #{p} {_niveauSelectionne.Nom}"
                : $"{nomListe} - {_niveauSelectionne.Nom}";
        }

        private string ObtenirDescriptionSeo()
        {
            string nom = _listeCourante?.Nom ?? "cette demon list";
            string valeurParDefaut = Texte.Formater("SeoListeDescription", "Découvrez {0}, son classement de {1} niveaux Geometry Dash, leurs difficultés, créateurs et les vidéos de réussite.", nom, _niveaux.Count);
            return SeoUtils.LimiterDescription(_listeCourante?.Description, valeurParDefaut);
        }

        private string ObtenirCheminCanonique() => _listeCourante is null
            ? "/liste"
            : SeoUtils.CheminListe(_listeCourante.Id, _listeCourante.Nom);

        private string ObtenirImageSeo()
        {
            if (_listeCourante is null) return "/Pictures/LogoMyDemonList.png";
            if (_niveauSelectionne is not null) return $"/MiniaturesNiveaux/{_niveauSelectionne.Id}.png";
            int? premierNiveauId = _classements.OrderBy(c => c.ClassementPosition).FirstOrDefault()?.NiveauId;
            if (premierNiveauId is int niveauId) return $"/MiniaturesNiveaux/{niveauId}.png";
            return "/Pictures/LogoMyDemonList.png";
        }

        private string? ObtenirAuteurSeo() => _listeCourante?.Utilisateur?.Nom;

        private string ObtenirTexteAlternatifImageSeo() => _listeCourante is null
            ? "Logo My Demon List"
            : Texte.Formater("ApercuListe", "Aperçu de la demon list {0}", _listeCourante.Nom);

        private string? ObtenirJsonLd()
        {
            if (_listeCourante is null || NePasIndexer) return null;

            Dictionary<int, Niveau> niveauxParId = _listeEntiereNiveaux.ToDictionary(n => n.Id);
            IEnumerable<(int Position, string Nom)> niveaux = _classements
                .Where(c => niveauxParId.ContainsKey(c.NiveauId))
                .Select(c => (c.ClassementPosition, niveauxParId[c.NiveauId].Nom));

            string url = NavigationManager.ToAbsoluteUri(SeoUtils.LocaliserChemin(ObtenirCheminCanonique(), Texte.CodeLangue)).AbsoluteUri;
            string accueil = NavigationManager.ToAbsoluteUri(SeoUtils.LocaliserChemin("/", Texte.CodeLangue)).AbsoluteUri;
            return SeoUtils.CreerJsonLdItemList(
                url,
                _listeCourante.Nom,
                ObtenirDescriptionSeo(),
                niveaux,
                new[] { (Texte["Accueil", "Accueil"], accueil), (_listeCourante.Nom, url) });
        }

        private Niveau? _niveauSelectionne;
        private string _recherche = string.Empty;
        private string _filtreDuree = string.Empty;
        private bool _isLoading = true;

        private string? _fondA;
        private string? _fondB;
        private string _fondActif = "a";
        private int? _dernierNiveauId;

        protected override async Task OnInitializedAsync()
        {
            int? listeIdDemande = ListeId ?? ListeSession.ListeId;
            if (listeIdDemande is not int listeId)
            {
                NavigationManager.NavigateTo(SeoUtils.LocaliserChemin("/", Texte.CodeLangue));
                return;
            }

            using (MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions))
            {
                _listeCourante = await dbContext.Listes
                    .Include(l => l.Utilisateur)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(l => l.Id == listeId);

                if (_listeCourante is null || !await PeutConsulterListeAsync(dbContext, _listeCourante))
                {
                    NavigationManager.NavigateTo(SeoUtils.LocaliserChemin("/404", Texte.CodeLangue));
                    return;
                }
            }

            _listeId = listeId;
            ListeSession.SetListe(_listeCourante.Id, _listeCourante.Nom, _listeCourante.DiscordServerUrl);

            string cheminCanonique = SeoUtils.CheminListe(_listeCourante.Id, _listeCourante.Nom);
            string cheminActuel = SeoUtils.RetirerPrefixeLangue(new Uri(NavigationManager.Uri).AbsolutePath).TrimEnd('/');
            if (!cheminActuel.Equals(cheminCanonique, StringComparison.OrdinalIgnoreCase))
            {
                string destination = !string.IsNullOrWhiteSpace(NiveauSelectionne)
                    ? $"{cheminCanonique}?niveau={Uri.EscapeDataString(NiveauSelectionne)}"
                    : cheminCanonique;
                NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(destination, Texte.CodeLangue), replace: true);
                return;
            }

            (_listeEntiereClassements, _listeEntiereCreateursNiveaux, _listeEntiereUtilisateurs, _listeEntiereNiveaux, _listeEntiereReussitesNiveaux, _listeEntiereFeatures)
                = Chargement.Cache(_listeEntiereClassements, _listeEntiereCreateursNiveaux, _listeEntiereUtilisateurs, _listeEntiereNiveaux, _listeEntiereReussitesNiveaux, _listeEntiereFeatures, listeId, DbContextOptions);

            ChargerNiveauxEtClassementsProgressivement();
            await ChargerNiveauxReussisParUtilisateurConnecte();
        }

        protected override void OnParametersSet()
        {
            SynchroniserVueMobile();

            if (_listeId == 0 || _listeEntiereNiveaux.Count == 0) return;

            if (!string.IsNullOrWhiteSpace(NiveauSelectionne))
            {
                Niveau? niveau = _listeEntiereNiveaux.FirstOrDefault(n =>
                    n.IdDuNiveauDansLeJeu.Equals(NiveauSelectionne, StringComparison.OrdinalIgnoreCase));

                if (niveau is not null && AfficherDetailsNiveau(niveau.Id)) return;

                NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(ObtenirCheminCanonique(), Texte.CodeLangue), replace: true);
                return;
            }

            SelectionnerNiveauParDefaut();
        }

        private void SynchroniserVueMobile()
        {
            if (!_vueMobileInitialisee)
            {
                _vueMobileDetail = !string.IsNullOrWhiteSpace(NiveauSelectionne);
                _dernierParametreNiveauMobile = NiveauSelectionne;
                _vueMobileInitialisee = true;
                return;
            }

            if (string.Equals(_dernierParametreNiveauMobile, NiveauSelectionne, StringComparison.Ordinal)) return;

            _dernierParametreNiveauMobile = NiveauSelectionne;
            _vueMobileDetail = !string.IsNullOrWhiteSpace(NiveauSelectionne);
            _remonterVueMobileApresRendu = true;
        }

        private void ChangerVueMobile(bool afficherDetail)
        {
            if (afficherDetail && _niveauSelectionne is null) return;
            if (_vueMobileDetail == afficherDetail) return;

            _vueMobileDetail = afficherDetail;
            _remonterVueMobileApresRendu = true;
        }

        private async Task<bool> PeutConsulterListeAsync(MyDemonListWebDbContext dbContext, Liste liste)
        {
            if (liste.EstPublique) return true;

            AuthenticationState authState = await AuthProvider.GetAuthenticationStateAsync();
            string? discordId = authState.User.FindFirst("discord:id")?.Value;
            if (string.IsNullOrWhiteSpace(discordId)) return false;

            int? utilisateurId = await dbContext.DiscordAccounts
                .AsNoTracking()
                .Where(a => a.DiscordId == discordId)
                .Select(a => (int?)a.UtilisateurId)
                .SingleOrDefaultAsync();

            if (utilisateurId is null) return false;
            if (liste.UtilisateurId == utilisateurId) return true;

            return await dbContext.MembresListe
                .AsNoTracking()
                .AnyAsync(m => m.ListeId == liste.Id && m.UtilisateurId == utilisateurId);
        }

        private async Task ChargerNiveauxReussisParUtilisateurConnecte()
        {
            AuthenticationState authState = await AuthProvider.GetAuthenticationStateAsync();
            ClaimsPrincipal user = authState.User;

            if (user.Identity?.IsAuthenticated != true) return;

            string? discordId = user.FindFirst("discord:id")?.Value;
            if (string.IsNullOrWhiteSpace(discordId)) return;

            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            DiscordAccount? compte = await dbContext.DiscordAccounts
                .Include(a => a.Utilisateur)
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.DiscordId == discordId);

            if (compte?.Utilisateur is null) return;

            HashSet<int> niveauxVerifies = _listeEntiereNiveaux
                .Where(n => n.VerifieurId == compte.Utilisateur.Id)
                .Select(n => n.Id)
                .ToHashSet();

            _niveauxReussisParUtilisateurConnecte = _listeEntiereReussitesNiveaux
                .Where(r => r.UtilisateurId == compte.Utilisateur.Id && r.Statut == "Validee")
                .Select(r => r.NiveauId)
                .Union(niveauxVerifies)
                .ToHashSet();

            HashSet<int> niveauIdsDeLaListe = _listeEntiereNiveaux.Select(n => n.Id).ToHashSet();
            string nomUtilisateur = compte.Utilisateur.Nom.ToLower();

            List<int> niveauxEnAttente = await dbContext.SoumissionsNiveaux
                .AsNoTracking()
                .Where(s => niveauIdsDeLaListe.Contains(s.NiveauId) && s.NomUtilisateur.ToLower() == nomUtilisateur)
                .Select(s => s.NiveauId)
                .ToListAsync();

            _niveauxEnAttenteParUtilisateurConnecte = niveauxEnAttente.ToHashSet();

            _peutFiltrerNiveauxNonTermines = true;
            FiltrerNiveaux();
            StateHasChanged();
        }

        private void MettreAJourFond()
        {
            if (_niveauSelectionne is null) return;

            string url = $"/MiniaturesNiveaux/{_niveauSelectionne.Id}.png";

            if (_fondA is null && _fondB is null)
            {
                _fondA = url;
                _fondB = url;
                _fondActif = "a";
                _dernierNiveauId = _niveauSelectionne.Id;
                return;
            }

            if (_dernierNiveauId == _niveauSelectionne.Id) return;

            if (_fondActif == "a")
            {
                _fondB = url;
                _fondActif = "b";
            }
            else
            {
                _fondA = url;
                _fondActif = "a";
            }

            _dernierNiveauId = _niveauSelectionne.Id;
            StateHasChanged();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!_remonterVueMobileApresRendu) return;

            _remonterVueMobileApresRendu = false;

            try
            {
                IJSObjectReference module = await ObtenirModuleJsAsync();
                await module.InvokeVoidAsync("remonterConteneur", _articleElement);
            }
            catch (JSDisconnectedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            _idNiveauCopieCts?.Cancel();
            _idNiveauCopieCts?.Dispose();

            if (_jsModule is not null)
            {
                try
                {
                    await _jsModule.DisposeAsync();
                }
                catch (JSDisconnectedException)
                {
                }
            }
        }

        private async Task<IJSObjectReference> ObtenirModuleJsAsync()
        {
            _jsModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./Components/Pages/ListePage.razor.js");
            return _jsModule;
        }

        private async Task CopierIdNiveau()
        {
            if (_niveauSelectionne is null) return;

            IJSObjectReference module = await ObtenirModuleJsAsync();
            await module.InvokeVoidAsync("copierTexte", _niveauSelectionne.IdDuNiveauDansLeJeu.ToString());

            _idNiveauCopieCts?.Cancel();
            _idNiveauCopieCts = new CancellationTokenSource();
            CancellationToken token = _idNiveauCopieCts.Token;

            _idNiveauCopie = true;
            StateHasChanged();

            try
            {
                await Task.Delay(1500, token);
                _idNiveauCopie = false;
                StateHasChanged();
            }
            catch (TaskCanceledException)
            {
            }
        }

        private void SelectionnerNiveauParDefaut()
        {
            Classement? top1 = _classements.FirstOrDefault();
            if (top1 is not null)
                AfficherDetailsNiveau(top1.NiveauId);
            else
                ClearSelection();
        }

        private void ClearSelection()
        {
            _niveauSelectionne = null;
            _createurs.Clear();
            _reussitesNiveau.Clear();
            StateHasChanged();
        }

        private void ChargerNiveauxEtClassementsProgressivement()
        {
            _isLoading = true;

            _niveaux = _listeEntiereNiveaux;
            _niveauxFiltres = _niveaux;

            _classements = _listeEntiereClassements
                .OrderBy(c => c.ClassementPosition)
                .ToList();

            _isLoading = false;
        }

        private bool AfficherDetailsNiveau(int niveauId)
        {
            try
            {
                Niveau? niveau = _listeEntiereNiveaux.FirstOrDefault(n => n.Id == niveauId);

                if (niveau is null) return false;

                List<Utilisateur> createursList = _listeEntiereCreateursNiveaux
                    .Where(cn => cn.NiveauId == niveauId)
                    .Select(cn => cn.Createur)
                    .ToList();

                List<ReussiteNiveau> reussitesList = _listeEntiereReussitesNiveaux
                    .Where(r => r.NiveauId == niveauId)
                    .ToList();

                _niveauSelectionne = niveau;
                _createurs = createursList;
                _reussitesNiveau = reussitesList;

                MettreAJourFond();
                StateHasChanged();
                return true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Chargement annulé.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du chargement des détails du niveau : {ex.Message}");
                return false;
            }
        }

        private void EnvoyerReussiteClick()
        {
            if (_niveauSelectionne is null) return;

            string chemin = SeoUtils.CheminSoumission(_listeId, _listeCourante?.Nom ?? ListeSession.ListeNom ?? "demon-list");
            NavigationManager.NavigateTo(SeoUtils.LocaliserChemin($"{chemin}?niveau={Uri.EscapeDataString(_niveauSelectionne.IdDuNiveauDansLeJeu)}", Texte.CodeLangue));
        }

        private string ObtenirCheminNiveau(Niveau niveau) =>
            SeoUtils.LocaliserChemin($"{ObtenirCheminCanonique()}?niveau={Uri.EscapeDataString(niveau.IdDuNiveauDansLeJeu)}", Texte.CodeLangue);

        private string ObtenirCheminUtilisateur(int utilisateurId)
        {
            string chemin = SeoUtils.CheminClassement(_listeId, _listeCourante?.Nom ?? ListeSession.ListeNom ?? "demon-list");
            return SeoUtils.LocaliserChemin($"{chemin}?joueur={utilisateurId}", Texte.CodeLangue);
        }

        private void OnRechercheChanged(string valeurRecherchee)
        {
            _recherche = valeurRecherchee;
            FiltrerNiveaux();
        }

        private void EffacerRecherche()
        {
            _recherche = string.Empty;
            FiltrerNiveaux();
        }

        private string GetSelectedDureeLabel() => _filtreDuree switch
        {
            "short" => "Short",
            "long" => "Long",
            "xl" => "XL",
            "xxl" => "XXL",
            _ => Texte["TousDurees", "Toutes les durées"]
        };

        private void OnFiltreDureeChanged(string nouvelleDuree)
        {
            _filtreDuree = nouvelleDuree;
            FiltrerNiveaux();
        }

        private void BasculerFiltreNiveauxNonTermines()
        {
            if (!_peutFiltrerNiveauxNonTermines) return;

            _afficherSeulementNiveauxNonTermines = !_afficherSeulementNiveauxNonTermines;
            FiltrerNiveaux();
        }

        private void FiltrerNiveaux()
        {
            _niveauxFiltres = _niveaux
                .Where(n => string.IsNullOrEmpty(_recherche) || n.Nom.Contains(_recherche, StringComparison.OrdinalIgnoreCase))
                .Where(FiltrerParDuree)
                .Where(n => !_afficherSeulementNiveauxNonTermines || !_niveauxReussisParUtilisateurConnecte.Contains(n.Id))
                .ToList();
        }

        private bool FiltrerParDuree(Niveau niveau)
        {
            int dureeEnMinutes = niveau.Duree / 60;

            return _filtreDuree switch
            {
                "short" => dureeEnMinutes >= 0.5 && dureeEnMinutes < 1,
                "long" => dureeEnMinutes >= 1 && dureeEnMinutes < 2,
                "xl" => dureeEnMinutes >= 2 && dureeEnMinutes < 5,
                "xxl" => dureeEnMinutes >= 5,
                _ => true
            };
        }

        private static string ConvertirDuree(int dureeEnSecondes)
        {
            int minutes = dureeEnSecondes / 60;
            int secondes = dureeEnSecondes % 60;
            return $"{minutes}min {secondes}sec";
        }
    }
}
