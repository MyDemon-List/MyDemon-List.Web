using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;
using System.Security.Claims;

namespace MyDemonList.Web.Components.Pages
{
    public partial class EnvoyerUneVideoPage : IDisposable
    {
        [Parameter]
        public int? ListeId { get; set; }

        [Parameter]
        public string? Slug { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "niveau")]
        public string? NiveauPreselectionne { get; set; }

        [Inject]
        private DbContextOptions<MyDemonListWebDbContext> DbContextOptions { get; set; } = default!;

        [Inject]
        private Chargement Chargement { get; set; } = default!;

        [Inject]
        private ListeSessionService ListeSession { get; set; } = default!;

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        [CascadingParameter]
        private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

        private Liste _listeCourante = new();

        private bool _isAuthenticated;
        private string? _connectedUsername;
        private int? _connectedUtilisateurId;

        private string ObtenirTitrePage() =>
            (_listeCourante.Nom ?? ListeSession.ListeNom) is string nom
                ? Texte.Formater("SeoSoumissionTitre", "Soumettre une réussite à {0}", nom)
                : Texte["SoumettreReussite", "Soumettre une réussite"];

        private string ObtenirDescriptionSeo() => _listeCourante.Nom is string nom
            ? Texte.Formater("SeoSoumissionDescription", "Déclarez une réussite pour un niveau de la demon list {0}.", nom)
            : Texte["SeoSoumissionDescriptionGenerique", "Déclarez une réussite pour un niveau Geometry Dash."];

        private string ObtenirCheminCanonique() => _listeCourante.Id > 0
            ? SeoUtils.CheminSoumission(_listeCourante.Id, _listeCourante.Nom)
            : "/soumettre-une-reussite";

        private Niveau? ResoudreNiveauParId(int niveauId) =>
            _listeEntiereNiveaux.FirstOrDefault(n => n.Id == niveauId);

        private Niveau? ResoudreNiveauParIdentifiantJeu(string identifiant) =>
            _listeEntiereNiveaux.FirstOrDefault(n =>
                n.IdDuNiveauDansLeJeu.Equals(identifiant, StringComparison.OrdinalIgnoreCase));

        private List<NiveauSuggestion> CreerSuggestionsNiveaux(string saisie)
        {
            string recherche = saisie.Trim();

            return _listeEntiereNiveaux
                .Where(n =>
                    n.Nom.Contains(recherche, StringComparison.OrdinalIgnoreCase) ||
                    n.IdDuNiveauDansLeJeu.Contains(recherche, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.Nom.Equals(recherche, StringComparison.OrdinalIgnoreCase))
                .ThenBy(n => _listeEntiereClassements
                    .FirstOrDefault(c => c.NiveauId == n.Id)?.ClassementPosition ?? int.MaxValue)
                .Take(5)
                .Select(n => new NiveauSuggestion(
                    n.Id,
                    n.Nom,
                    n.Publisher?.Nom ?? "Inconnu"))
                .ToList();
        }

        private bool RawFootageEstRequis(Niveau niveau)
        {
            if (_listeCourante.RawFootageMode == RawFootageMode.None)
                return false;
            if (_listeCourante.RawFootageMode == RawFootageMode.All)
                return true;
            if (_listeCourante.RawFootageMode == RawFootageMode.FromTop && _listeCourante.RawFootageTopStart.HasValue)
            {
                int topStart = _listeCourante.RawFootageTopStart.Value;
                Classement? classement = _listeEntiereClassements.FirstOrDefault(c => c.NiveauId == niveau.Id);
                if (classement != null)
                {
                    return classement.ClassementPosition <= topStart;
                }
            }
            return false;
        }

        private bool VideoEstRequise(Niveau niveau)
        {
            Classement? classement = _listeEntiereClassements.FirstOrDefault(c => c.NiveauId == niveau.Id);
            return PreuveVideoUtils.EstRequise(_listeCourante, niveau, classement, _listeEntiereFeatures);
        }

        private bool VideoEstRequisePourNiveauSelectionne() =>
            _niveauSelectionne is null || VideoEstRequise(_niveauSelectionne);

        private bool AcceptationAutomatiqueDisponible() =>
            _niveauSelectionne is not null &&
            !VideoEstRequise(_niveauSelectionne) &&
            !RawFootageEstRequis(_niveauSelectionne);

        private bool AfficherChampRawFootage() =>
            _listeCourante.RawFootageMode switch
            {
                RawFootageMode.All => true,
                RawFootageMode.FromTop => _niveauSelectionne != null && RawFootageEstRequis(_niveauSelectionne),
                _ => false
            };

        private CancellationTokenSource? _debounceNiveauToken;
        private CancellationTokenSource? _debounceUtilisateurToken;
        private SoumissionNiveau _newSubmission = new();
        private List<Niveau> _listeEntiereNiveaux = [];
        private List<Utilisateur> _listeEntiereUtilisateurs = [];
        private List<SoumissionNiveau> _listeEntiereSoumissionsNiveaux = [];
        private List<ReussiteNiveau> _listeEntiereReussitesNiveaux = [];
        private List<CreateurNiveau> _listeEntiereCreateursNiveaux = [];
        private List<Classement> _listeEntiereClassements = [];
        private List<Difficulte> _listeEntiereFeatures = [];
        private List<NiveauSuggestion> _niveauSuggestions = [];
        private List<string> _utilisateurSuggestions = [];

        private sealed record NiveauSuggestion(int NiveauId, string Nom, string Publisher);

        private string? _errorMessage;
        private SoumissionNiveau? _soumissionExistante;
        private Niveau? _niveauSelectionne;
        private string _currentNiveauInput = string.Empty;
        private string _currentUtilisateurInput = string.Empty;
        private bool _submissionSuccess;
        private bool _reussiteAccepteeAutomatiquement;
        private int _selectedNiveauIndex = -1;
        private int _selectedUtilisateurIndex = -1;
        private bool _isLoading;

        private string _bgA = "/Pictures/preview-placeholder.png";
        private string _bgB = "/Pictures/preview-placeholder.png";
        private bool _showBgA = true;
        private System.Timers.Timer? _bgTimer;
        private readonly Random _rng = new();

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
                _listeCourante = await dbContext.Listes.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listeId) ?? new Liste();
                if (_listeCourante.Id == 0 || !await PeutConsulterListeAsync(dbContext, _listeCourante))
                {
                    NavigationManager.NavigateTo(SeoUtils.LocaliserChemin("/404", Texte.CodeLangue));
                    return;
                }
            }

            ListeSession.SetListe(_listeCourante.Id, _listeCourante.Nom, _listeCourante.DiscordServerUrl);

            string cheminCanonique = SeoUtils.CheminSoumission(_listeCourante.Id, _listeCourante.Nom);
            string cheminActuel = SeoUtils.RetirerPrefixeLangue(new Uri(NavigationManager.Uri).AbsolutePath).TrimEnd('/');
            if (!cheminActuel.Equals(cheminCanonique, StringComparison.OrdinalIgnoreCase))
            {
                string destination = !string.IsNullOrWhiteSpace(NiveauPreselectionne)
                    ? $"{cheminCanonique}?niveau={Uri.EscapeDataString(NiveauPreselectionne)}"
                    : cheminCanonique;
                NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(destination, Texte.CodeLangue), replace: true);
                return;
            }

            (_listeEntiereClassements, _listeEntiereCreateursNiveaux, _listeEntiereUtilisateurs, _listeEntiereNiveaux, _listeEntiereReussitesNiveaux, _listeEntiereFeatures) =
                Chargement.Cache(_listeEntiereClassements, _listeEntiereCreateursNiveaux, _listeEntiereUtilisateurs, _listeEntiereNiveaux, _listeEntiereReussitesNiveaux, _listeEntiereFeatures, listeId, DbContextOptions);

            if (!string.IsNullOrWhiteSpace(NiveauPreselectionne))
            {
                _niveauSelectionne = ResoudreNiveauParIdentifiantJeu(NiveauPreselectionne);
                _currentNiveauInput = _niveauSelectionne?.Nom ?? string.Empty;

                if (_niveauSelectionne is null)
                    NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(ObtenirCheminCanonique(), Texte.CodeLangue), replace: true);
            }

            _bgA = RandomBg();
            _bgB = RandomBg(_bgA);

            _bgTimer = new System.Timers.Timer(10000)
            {
                AutoReset = true
            };

            _bgTimer.Elapsed += (_, __) =>
            {
                if (_showBgA)
                    _bgB = RandomBg(_bgA);
                else
                    _bgA = RandomBg(_bgB);

                _showBgA = !_showBgA;
                InvokeAsync(StateHasChanged);
            };

            _bgTimer.Start();

            using (MyDemonListWebDbContext ctx = new MyDemonListWebDbContext(DbContextOptions))
            {
                _listeEntiereSoumissionsNiveaux = ctx.SoumissionsNiveaux
                    .Include(s => s.Niveau)
                    .Where(s => s.Niveau.ListeId == listeId)
                    .AsNoTracking()
                    .ToList();
            }

            AuthenticationState authState = await AuthStateTask;
            ClaimsPrincipal user = authState.User;
            _isAuthenticated = user.Identity?.IsAuthenticated == true;

            if (_isAuthenticated)
            {
                string? discordId = user.FindFirst("discord:id")?.Value;
                string? fallbackName = user.FindFirst("discord:global_name")?.Value
                                   ?? user.Identity?.Name
                                   ?? user.FindFirst(ClaimTypes.Name)?.Value;

                using MyDemonListWebDbContext ctx = new MyDemonListWebDbContext(DbContextOptions);
                if (!string.IsNullOrEmpty(discordId))
                {
                    DiscordAccount? account = ctx.DiscordAccounts
                        .Include(a => a.Utilisateur)
                        .AsNoTracking()
                        .SingleOrDefault(a => a.DiscordId == discordId);

                    _connectedUtilisateurId = account?.Utilisateur?.Id;
                    _connectedUsername = account?.Utilisateur?.Nom ?? fallbackName;
                }
                else
                {
                    _connectedUsername = fallbackName;
                }

                _currentUtilisateurInput = _connectedUsername ?? string.Empty;
                _newSubmission.NomUtilisateur = _connectedUsername;
                _newSubmission.UtilisateurId = _connectedUtilisateurId;
                _utilisateurSuggestions.Clear();
            }
        }

        private async Task<bool> PeutConsulterListeAsync(MyDemonListWebDbContext dbContext, Liste liste)
        {
            if (liste.EstPublique) return true;

            AuthenticationState authState = await AuthStateTask;
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

        public void Dispose()
        {
            _bgTimer?.Stop();
            _bgTimer?.Dispose();
        }

        private async Task OnNiveauInput(ChangeEventArgs e)
        {
            _currentNiveauInput = e.Value?.ToString() ?? string.Empty;
            _niveauSelectionne = null;
            _soumissionExistante = null;

            _debounceNiveauToken?.Cancel();
            _debounceNiveauToken = new CancellationTokenSource();
            CancellationToken token = _debounceNiveauToken.Token;

            if (string.IsNullOrWhiteSpace(_currentNiveauInput))
            {
                _niveauSuggestions.Clear();
                StateHasChanged();
                return;
            }

            try
            {
                await Task.Delay(50, token);
                if (!token.IsCancellationRequested)
                {
                    _niveauSuggestions = CreerSuggestionsNiveaux(_currentNiveauInput);
                    _selectedNiveauIndex = -1;

                    StateHasChanged();
                }
            }
            catch (TaskCanceledException) { }
        }

        private async Task OnUtilisateurInput(ChangeEventArgs e)
        {
            if (_isAuthenticated) return;

            _currentUtilisateurInput = e.Value?.ToString() ?? string.Empty;
            _newSubmission.NomUtilisateur = _currentUtilisateurInput;
            MettreAJourEtatSoumissionExistante();

            _debounceUtilisateurToken?.Cancel();
            _debounceUtilisateurToken = new CancellationTokenSource();
            CancellationToken token = _debounceUtilisateurToken.Token;

            if (string.IsNullOrWhiteSpace(_currentUtilisateurInput))
            {
                _utilisateurSuggestions.Clear();
                StateHasChanged();
                _selectedUtilisateurIndex = -1;
                return;
            }

            try
            {
                await Task.Delay(50, token);
                if (!token.IsCancellationRequested)
                {
                    bool exactMatchExists = _listeEntiereUtilisateurs.Any(u =>
                        u.Nom.Equals(_currentUtilisateurInput, StringComparison.OrdinalIgnoreCase));

                    _utilisateurSuggestions = exactMatchExists
                        ? []
                        : _listeEntiereUtilisateurs
                            .Where(u => u.Nom.Contains(_currentUtilisateurInput, StringComparison.OrdinalIgnoreCase))
                            .Select(u => u.Nom)
                            .Take(5)
                            .ToList();

                    StateHasChanged();
                }
            }
            catch (TaskCanceledException) { }
        }

        private void HandleKeyDownNiveau(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                if (_selectedNiveauIndex >= 0 && _selectedNiveauIndex < _niveauSuggestions.Count)
                    AppliquerApercuNiveau(_niveauSuggestions[_selectedNiveauIndex]);

                ConfirmerNiveau();
                MettreAJourNiveauDansUrl();
                _niveauSuggestions.Clear();
                _selectedNiveauIndex = -1;
                StateHasChanged();
                return;
            }

            if (!_niveauSuggestions.Any()) return;

            if (e.Key == "ArrowDown")
            {
                _selectedNiveauIndex = (_selectedNiveauIndex + 1) % _niveauSuggestions.Count;
                AppliquerApercuNiveau(_niveauSuggestions[_selectedNiveauIndex]);
                StateHasChanged();
            }
            else if (e.Key == "ArrowUp")
            {
                _selectedNiveauIndex = (_selectedNiveauIndex - 1 + _niveauSuggestions.Count) % _niveauSuggestions.Count;
                AppliquerApercuNiveau(_niveauSuggestions[_selectedNiveauIndex]);
                StateHasChanged();
            }
        }

        private void HandleKeyDownUtilisateur(KeyboardEventArgs e)
        {
            if (_isAuthenticated || !_utilisateurSuggestions.Any()) return;

            if (e.Key == "ArrowDown")
            {
                _selectedUtilisateurIndex = (_selectedUtilisateurIndex + 1) % _utilisateurSuggestions.Count;
                AppliquerSuggestionUtilisateur(_utilisateurSuggestions[_selectedUtilisateurIndex]);
            }
            else if (e.Key == "ArrowUp")
            {
                _selectedUtilisateurIndex = (_selectedUtilisateurIndex - 1 + _utilisateurSuggestions.Count) % _utilisateurSuggestions.Count;
                AppliquerSuggestionUtilisateur(_utilisateurSuggestions[_selectedUtilisateurIndex]);
            }
            else if (e.Key == "Enter" && _selectedUtilisateurIndex >= 0)
            {
                AppliquerSuggestionUtilisateur(_utilisateurSuggestions[_selectedUtilisateurIndex]);
                _utilisateurSuggestions.Clear();
                _selectedUtilisateurIndex = -1;
            }

            StateHasChanged();
        }

        private void AppliquerApercuNiveau(NiveauSuggestion suggestion)
        {
            _currentNiveauInput = suggestion.Nom;
            _niveauSelectionne = ResoudreNiveauParId(suggestion.NiveauId);
            MettreAJourEtatSoumissionExistante();
        }

        private void ConfirmerNiveau()
        {
            string nomSaisi = _currentNiveauInput.Trim();

            if (_niveauSelectionne is null ||
                !_niveauSelectionne.Nom.Equals(nomSaisi, StringComparison.OrdinalIgnoreCase))
            {
                List<Niveau> correspondances = _listeEntiereNiveaux
                    .Where(n => n.Nom.Equals(nomSaisi, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToList();

                _niveauSelectionne = correspondances.Count == 1 ? correspondances[0] : null;
            }

            MettreAJourEtatSoumissionExistante();
        }

        private void MettreAJourEtatSoumissionExistante()
        {
            string nomUtilisateur = (_isAuthenticated ? _connectedUsername : _currentUtilisateurInput)?.Trim() ?? string.Empty;

            _soumissionExistante = _niveauSelectionne is null || string.IsNullOrWhiteSpace(nomUtilisateur)
                ? null
                : _listeEntiereSoumissionsNiveaux.FirstOrDefault(s =>
                    s.NiveauId == _niveauSelectionne.Id &&
                    (_isAuthenticated && _connectedUtilisateurId is int utilisateurId
                        ? s.UtilisateurId == utilisateurId ||
                          (s.UtilisateurId == null && s.NomUtilisateur.Equals(nomUtilisateur, StringComparison.OrdinalIgnoreCase))
                        : s.NomUtilisateur.Equals(nomUtilisateur, StringComparison.OrdinalIgnoreCase)));
        }

        private void AppliquerSuggestionUtilisateur(string nomUtilisateur)
        {
            _currentUtilisateurInput = nomUtilisateur;
            _newSubmission.NomUtilisateur = nomUtilisateur;
            MettreAJourEtatSoumissionExistante();
        }

        private void SelectNiveau(NiveauSuggestion suggestion)
        {
            AppliquerApercuNiveau(suggestion);
            MettreAJourNiveauDansUrl();
            _niveauSuggestions.Clear();
            _selectedNiveauIndex = -1;
            StateHasChanged();
        }

        private void SelectUtilisateur(string nomUtilisateur)
        {
            if (_isAuthenticated) return;

            AppliquerSuggestionUtilisateur(nomUtilisateur);
            _utilisateurSuggestions.Clear();
            _selectedUtilisateurIndex = -1;
            StateHasChanged();
        }

        private async Task ValidateAndSubmit()
        {
            _reussiteAccepteeAutomatiquement = false;

            if (_isAuthenticated)
            {
                _currentUtilisateurInput = _connectedUsername ?? string.Empty;
                _newSubmission.NomUtilisateur = _connectedUsername;
            }

            if (string.IsNullOrWhiteSpace(_currentNiveauInput))
            {
                _errorMessage = Texte["ChampNiveauRequis", "Veuillez remplir le champ : Nom du niveau."];
                _submissionSuccess = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentUtilisateurInput))
            {
                _errorMessage = Texte["ChampUtilisateurRequis", "Veuillez remplir le champ : Nom d'utilisateur."];
                _submissionSuccess = false;
                return;
            }

            if (!_isAuthenticated)
            {
                string nomSaisi = _currentUtilisateurInput.Trim().ToLower();

                using MyDemonListWebDbContext ctx = new MyDemonListWebDbContext(DbContextOptions);
                bool estLieADiscord = await ctx.DiscordAccounts
                    .AsNoTracking()
                    .AnyAsync(d => d.Utilisateur.Nom.ToLower() == nomSaisi);

                if (estLieADiscord)
                {
                    _errorMessage = Texte.Formater("CompteDiscordConnexion", "\"{0}\" est un compte relié à Discord. Connectez-vous avec Discord pour soumettre une réussite en son nom.", _currentUtilisateurInput.Trim());
                    _submissionSuccess = false;
                    return;
                }
            }

            ConfirmerNiveau();
            bool videoRequise = VideoEstRequisePourNiveauSelectionne();

            if (videoRequise && string.IsNullOrWhiteSpace(_newSubmission.UrlVideo))
            {
                _errorMessage = Texte["ChampVideoRequis", "Veuillez remplir le champ : URL de la vidéo."];
                _submissionSuccess = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(_newSubmission.UrlVideo) && !VideoUtils.EstUrlVideoValide(_newSubmission.UrlVideo))
            {
                _errorMessage = Texte["UrlVideoInvalide", "L'URL de la vidéo doit pointer vers YouTube, Twitch ou Google Drive."];
                _submissionSuccess = false;
                return;
            }

            _newSubmission.UrlVideo = _newSubmission.UrlVideo?.Trim() ?? string.Empty;

            if (_niveauSelectionne != null && RawFootageEstRequis(_niveauSelectionne))
            {
                if (string.IsNullOrWhiteSpace(_newSubmission.RawFootageUrl))
                {
                    _errorMessage = Texte["RawFootageRequis", "Le Raw Footage est requis pour ce niveau. Veuillez remplir le champ URL du Raw Footage."];
                    _submissionSuccess = false;
                    return;
                }
                if (!VideoUtils.EstUrlVideoValide(_newSubmission.RawFootageUrl))
                {
                _errorMessage = Texte["UrlRawFootageInvalide", "L'URL du Raw Footage doit pointer vers YouTube, Twitch ou Google Drive."];
                    _submissionSuccess = false;
                    return;
                }
            }
            else
            {
                _newSubmission.RawFootageUrl = null;
            }

            _errorMessage = string.Empty;
            _submissionSuccess = false;
            _isLoading = true;
            StateHasChanged();

            bool estValide = CheckIfAlreadySucceeded();
            if (!estValide)
            {
                _isLoading = false;
                _submissionSuccess = false;
                StateHasChanged();
                return;
            }

            if (AcceptationAutomatiqueDisponible())
                await AccepterAutomatiquement();
            else if (_soumissionExistante is not null)
                await MettreAJourSoumission();
            else
                await HandleValidSubmit();

            _isLoading = false;
            StateHasChanged();
        }

        private async Task AccepterAutomatiquement()
        {
            if (_niveauSelectionne is null) return;

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);
                await using var transaction = await dbContext.Database.BeginTransactionAsync();

                SoumissionNiveau? soumission = _soumissionExistante is null
                    ? null
                    : await dbContext.SoumissionsNiveaux
                        .FirstOrDefaultAsync(s => s.IdSoumission == _soumissionExistante.IdSoumission);

                if (soumission is null)
                {
                    soumission = new SoumissionNiveau
                    {
                        NiveauId = _niveauSelectionne.Id,
                        UtilisateurId = _isAuthenticated ? _connectedUtilisateurId : null,
                        NomUtilisateur = _currentUtilisateurInput.Trim(),
                        UrlVideo = _newSubmission.UrlVideo,
                        RawFootageUrl = null,
                        DateSoumission = DateTime.Now
                    };
                    dbContext.SoumissionsNiveaux.Add(soumission);
                }
                else
                {
                    soumission.UtilisateurId = _isAuthenticated ? _connectedUtilisateurId : soumission.UtilisateurId;
                    soumission.UrlVideo = _newSubmission.UrlVideo;
                    soumission.RawFootageUrl = null;
                    soumission.DateSoumission = DateTime.Now;
                }

                await dbContext.SaveChangesAsync();

                Utilisateur? utilisateur = soumission.UtilisateurId is int utilisateurId
                    ? await dbContext.Utilisateurs.FirstOrDefaultAsync(u => u.Id == utilisateurId)
                    : null;

                string nomUtilisateur = soumission.NomUtilisateur.Trim();
                utilisateur ??= await dbContext.Utilisateurs
                    .FirstOrDefaultAsync(u => u.Nom.ToLower() == nomUtilisateur.ToLower());

                if (utilisateur is null)
                {
                    utilisateur = new Utilisateur { Nom = nomUtilisateur };
                    dbContext.Utilisateurs.Add(utilisateur);
                    await dbContext.SaveChangesAsync();
                }

                SoumissionHistorique soumissionAvant = HistoriqueListeService.CapturerSoumission(soumission) with
                {
                    UtilisateurId = utilisateur.Id
                };

                ReussiteNiveau? reussite = await dbContext.ReussitesNiveaux
                    .FirstOrDefaultAsync(r => r.UtilisateurId == utilisateur.Id && r.NiveauId == _niveauSelectionne.Id);
                ReussiteHistorique? reussiteAvant = reussite is null
                    ? null
                    : HistoriqueListeService.CapturerReussite(reussite);

                if (reussite is null)
                {
                    dbContext.ReussitesNiveaux.Add(new ReussiteNiveau
                    {
                        UtilisateurId = utilisateur.Id,
                        NiveauId = _niveauSelectionne.Id,
                        Video = soumission.UrlVideo,
                        Statut = "Validee"
                    });
                }
                else
                {
                    reussite.Video = soumission.UrlVideo;
                    reussite.Statut = "Validee";
                }

                dbContext.SoumissionsNiveaux.Remove(soumission);
                HistoriqueListeService.Ajouter(
                    dbContext,
                    _listeCourante.Id,
                    _connectedUtilisateurId,
                    TypesActionHistoriqueListe.SoumissionAcceptee,
                    $"La réussite de {utilisateur.Nom} pour {_niveauSelectionne.Nom} a été acceptée automatiquement.",
                    HistoriqueListeService.CleSoumission(_listeCourante.Id, soumission.IdSoumission),
                    new DecisionSoumissionHistorique(soumissionAvant, reussiteAvant),
                    null);

                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                Chargement.ClearCache(_listeCourante.Id);
                _submissionSuccess = true;
                _reussiteAccepteeAutomatiquement = true;
                _soumissionExistante = null;
                _newSubmission = new SoumissionNiveau();
                _selectedNiveauIndex = -1;
                _selectedUtilisateurIndex = -1;
                _currentNiveauInput = string.Empty;
                _niveauSelectionne = null;
                if (!_isAuthenticated) _currentUtilisateurInput = string.Empty;
            }
            catch (Exception ex)
            {
                _submissionSuccess = false;
                _errorMessage = Texte.Formater("ErreurMiseAJour", "Erreur lors de la mise à jour : {0}", ex.Message);
            }
        }

        private async Task HandleValidSubmit()
        {
            try
            {
                if (DbContextOptions is not null)
                {
                    using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);
                    _newSubmission.DateSoumission = DateTime.Now;
                    _newSubmission.UtilisateurId = _isAuthenticated ? _connectedUtilisateurId : null;

                    dbContext.SoumissionsNiveaux.Add(_newSubmission);
                    await dbContext.SaveChangesAsync();

                    SoumissionHistorique soumissionCreee = HistoriqueListeService.CapturerSoumission(_newSubmission);
                    HistoriqueListeService.Ajouter(
                        dbContext,
                        _listeCourante.Id,
                        _connectedUtilisateurId,
                        TypesActionHistoriqueListe.SoumissionCreee,
                        $"{_newSubmission.NomUtilisateur} a soumis une réussite pour {_niveauSelectionne?.Nom ?? "un niveau"}.",
                        HistoriqueListeService.CleSoumission(_listeCourante.Id, _newSubmission.IdSoumission),
                        null,
                        soumissionCreee);
                    await dbContext.SaveChangesAsync();

                    _newSubmission = new SoumissionNiveau();
                    _submissionSuccess = true;
                }
            }
            catch
            {
                _submissionSuccess = false;
            }
            finally
            {
                _selectedNiveauIndex = -1;
                _selectedUtilisateurIndex = -1;
                _currentNiveauInput = string.Empty;
                _currentUtilisateurInput = string.Empty;
                _niveauSelectionne = null;
                StateHasChanged();
            }
        }

        private async Task MettreAJourSoumission()
        {
            if (_soumissionExistante is null) return;

            _isLoading = true;
            StateHasChanged();

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);
                SoumissionNiveau? soumission = await dbContext.SoumissionsNiveaux
                    .FirstOrDefaultAsync(s => s.IdSoumission == _soumissionExistante.IdSoumission);

                if (soumission is not null)
                {
                    SoumissionHistorique avant = HistoriqueListeService.CapturerSoumission(soumission);
                    soumission.UrlVideo = _newSubmission.UrlVideo;
                    soumission.RawFootageUrl = _newSubmission.RawFootageUrl;
                    soumission.DateSoumission = DateTime.Now;
                    soumission.UtilisateurId = _isAuthenticated ? _connectedUtilisateurId : soumission.UtilisateurId;
                    SoumissionHistorique apres = HistoriqueListeService.CapturerSoumission(soumission);
                    HistoriqueListeService.Ajouter(
                        dbContext,
                        _listeCourante.Id,
                        _connectedUtilisateurId,
                        TypesActionHistoriqueListe.SoumissionModifiee,
                        $"{soumission.NomUtilisateur} a modifié sa soumission pour {_niveauSelectionne?.Nom ?? "un niveau"}.",
                        HistoriqueListeService.CleSoumission(_listeCourante.Id, soumission.IdSoumission),
                        avant,
                        apres);
                    await dbContext.SaveChangesAsync();
                }

                _submissionSuccess = true;
                _soumissionExistante = null;
                _newSubmission = new SoumissionNiveau();
                _selectedNiveauIndex = -1;
                _selectedUtilisateurIndex = -1;
                _currentNiveauInput = string.Empty;
                _niveauSelectionne = null;
                if (!_isAuthenticated) _currentUtilisateurInput = string.Empty;
            }
            catch (Exception ex)
            {
                _errorMessage = Texte.Formater("ErreurMiseAJour", "Erreur lors de la mise à jour : {0}", ex.Message);
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        private bool CheckIfAlreadySucceeded()
        {
            try
            {
                if (_isAuthenticated && !string.IsNullOrWhiteSpace(_connectedUsername))
                {
                    _currentUtilisateurInput = _connectedUsername;
                    _newSubmission.NomUtilisateur = _connectedUsername;
                    _newSubmission.UtilisateurId = _connectedUtilisateurId;
                }

                ConfirmerNiveau();
                Niveau? niveau = _niveauSelectionne;

                if (niveau == null)
                {
                    int niveauxAvecCeNom = _listeEntiereNiveaux.Count(n =>
                        n.Nom.Equals(_currentNiveauInput.Trim(), StringComparison.OrdinalIgnoreCase));

                    _errorMessage = niveauxAvecCeNom > 1
                        ? Texte.Formater("NiveauxHomonymes", "Plusieurs niveaux portent le nom \"{0}\". Sélectionnez celui qui possède le bon ID dans les propositions.", _currentNiveauInput.Trim())
                        : Texte.Formater("NiveauIntrouvable", "Le niveau \"{0}\" est introuvable. Veuillez sélectionner un niveau existant.", _currentNiveauInput);
                    StateHasChanged();
                    return false;
                }

                _niveauSelectionne = niveau;
                _newSubmission.NiveauId = niveau.Id;

                Utilisateur? utilisateur = _listeEntiereUtilisateurs
                    .FirstOrDefault(u => u.Nom.Equals(_currentUtilisateurInput.Trim(), StringComparison.OrdinalIgnoreCase));

                if (utilisateur != null)
                {
                    ReussiteNiveau? reussite = _listeEntiereReussitesNiveaux
                        .FirstOrDefault(r => r.UtilisateurId == utilisateur.Id && r.NiveauId == niveau.Id);

                    if (reussite != null && reussite.Statut == "Validee")
                    {
                        _errorMessage = Texte.Formater("JoueurDejaReussi", "{0} a déjà réussi le niveau {1}.", utilisateur.Nom, niveau.Nom);
                        StateHasChanged();
                        return false;
                    }

                    bool niveauVerifie = _listeEntiereNiveaux
                        .Any(n => n.Id == niveau.Id && n.VerifieurId == utilisateur.Id);

                    if (niveauVerifie)
                    {
                        _errorMessage = Texte.Formater("VerifieurNePeutSoumettre", "{0} a vérifié le niveau {1}, et ne peut donc pas soumettre de réussite pour ce niveau.", utilisateur.Nom, niveau.Nom);
                        StateHasChanged();
                        return false;
                    }
                }

                MettreAJourEtatSoumissionExistante();

                return true;
            }
            catch (Exception ex)
            {
                _errorMessage = Texte.Formater("ErreurVerification", "Erreur lors de la vérification : {0}", ex.Message);
                return false;
            }
        }

        private void HandleFocusOutNiveau(FocusEventArgs e)
        {
            ConfirmerNiveau();

            Task.Delay(100).ContinueWith(_ =>
            {
                _niveauSuggestions.Clear();
                _selectedNiveauIndex = -1;
                InvokeAsync(StateHasChanged);
            });
        }

        private void HandleFocusOutUtilisateur(FocusEventArgs e)
        {
            if (_isAuthenticated) return;

            Task.Delay(100).ContinueWith(_ =>
            {
                _utilisateurSuggestions.Clear();
                _selectedUtilisateurIndex = -1;
                InvokeAsync(StateHasChanged);
            });
        }

        private void OnFocusNiveau(FocusEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentNiveauInput)) return;

            _niveauSuggestions = CreerSuggestionsNiveaux(_currentNiveauInput);
            _selectedNiveauIndex = -1;

            StateHasChanged();
        }

        private void MettreAJourNiveauDansUrl()
        {
            string chemin = _niveauSelectionne is null
                ? ObtenirCheminCanonique()
                : $"{ObtenirCheminCanonique()}?niveau={Uri.EscapeDataString(_niveauSelectionne.IdDuNiveauDansLeJeu)}";

            NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(chemin, Texte.CodeLangue), replace: true);
        }

        private void OnFocusUtilisateur(FocusEventArgs e)
        {
            if (_isAuthenticated || string.IsNullOrWhiteSpace(_newSubmission.NomUtilisateur)) return;

            bool exactMatchExists = _listeEntiereUtilisateurs.Any(u =>
                u.Nom.Equals(_newSubmission.NomUtilisateur, StringComparison.OrdinalIgnoreCase));

            _utilisateurSuggestions = exactMatchExists
                ? []
                : _listeEntiereUtilisateurs
                    .Where(u => u.Nom.Contains(_newSubmission.NomUtilisateur, StringComparison.OrdinalIgnoreCase))
                    .Select(u => u.Nom)
                    .Take(5)
                    .ToList();

            StateHasChanged();
        }

        private string RandomBg(string? exclude = null)
        {
            if (_listeEntiereNiveaux.Count == 0)
                return exclude ?? "/Pictures/preview-placeholder.png";

            for (int i = 0; i < 4; i++)
            {
                Niveau n = _listeEntiereNiveaux[_rng.Next(_listeEntiereNiveaux.Count)];
                string url = $"/MiniaturesNiveaux/{n.Id}.png";
                if (url != exclude) return url;
            }

            return $"/MiniaturesNiveaux/{_listeEntiereNiveaux[0].Id}.png";
        }
    }
}
