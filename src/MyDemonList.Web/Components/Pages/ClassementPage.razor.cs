using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;

namespace MyDemonList.Web.Components.Pages
{
    public partial class ClassementPage
    {
        [Parameter]
        public int? ListeId { get; set; }

        [Parameter]
        public string? Slug { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "joueur")]
        public string? JoueurSelectionne { get; set; }

        private int? JoueurSelectionneId =>
            int.TryParse(JoueurSelectionne, out int utilisateurId) && utilisateurId > 0
                ? utilisateurId
                : null;

        private bool ParametreJoueurInvalide =>
            !string.IsNullOrWhiteSpace(JoueurSelectionne) && JoueurSelectionneId is null;

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

        private string ObtenirTitrePage() =>
            (_listeCourante?.Nom ?? ListeSession.ListeNom) is string nom
                ? Texte.Formater("SeoClassementTitre", "Classement de {0}", nom)
                : Texte["Classement", "Classement des joueurs"];

        private string ObtenirDescriptionSeo()
        {
            string nom = _listeCourante?.Nom ?? "cette demon list";
            return SeoUtils.LimiterDescription(null, Texte.Formater("SeoClassementDescription", "Consultez le classement des joueurs de {0}, leurs points, niveaux réussis, créations et vérifications Geometry Dash.", nom, _utilisateursAvecPoints.Count));
        }

        private string ObtenirCheminCanonique() => _listeCourante is null
            ? "/classement"
            : SeoUtils.CheminClassement(_listeCourante.Id, _listeCourante.Nom);

        private string ObtenirImageSeo()
        {
            if (!string.IsNullOrWhiteSpace(_utilisateurSelectionne?.AvatarUrl))
                return _utilisateurSelectionne.AvatarUrl;

            int? premierNiveauId = _listeClassements.OrderBy(c => c.ClassementPosition).FirstOrDefault()?.NiveauId;
            return premierNiveauId is int niveauId
                ? $"/MiniaturesNiveaux/{niveauId}.png"
                : "/Pictures/LogoMyDemonList.png";
        }

        private string? ObtenirAuteurSeo() => _listeCourante?.Utilisateur?.Nom;

        private string ObtenirTexteAlternatifImageSeo()
        {
            if (!string.IsNullOrWhiteSpace(_utilisateurSelectionne?.AvatarUrl))
                return Texte.Formater("AvatarJoueur", "Avatar de {0} dans le classement de {1}", _utilisateurSelectionne.Nom, _listeCourante?.Nom ?? "My Demon List");

            return _listeCourante is null
                ? "Logo My Demon List"
                : Texte.Formater("SeoClassementTitre", "Classement de {0}", _listeCourante.Nom);
        }

        private bool NePasIndexer => _listeCourante?.EstPublique != true || _listeNiveaux.Count == 0;

        private string? ObtenirJsonLd()
        {
            if (_listeCourante is null || NePasIndexer) return null;

            IEnumerable<(int Position, string Nom)> joueurs = _utilisateursAvecPoints
                .OrderBy(u => u.Classement)
                .Where(u => !string.IsNullOrWhiteSpace(u.Nom))
                .Select(u => (u.Classement, u.Nom!));

            string url = NavigationManager.ToAbsoluteUri(SeoUtils.LocaliserChemin(ObtenirCheminCanonique(), Texte.CodeLangue)).AbsoluteUri;
            string accueil = NavigationManager.ToAbsoluteUri(SeoUtils.LocaliserChemin("/", Texte.CodeLangue)).AbsoluteUri;
            string urlListe = NavigationManager.ToAbsoluteUri(SeoUtils.LocaliserChemin(SeoUtils.CheminListe(_listeCourante.Id, _listeCourante.Nom), Texte.CodeLangue)).AbsoluteUri;
            return SeoUtils.CreerJsonLdItemList(
                url,
                ObtenirTitrePage(),
                ObtenirDescriptionSeo(),
                joueurs,
                new[] { (Texte["Accueil", "Accueil"], accueil), (_listeCourante.Nom, urlListe), (Texte["Classement", "Classement"], url) });
        }

        private List<Utilisateur> _listeUtilisateurs = [];
        private List<Classement> _listeClassements = [];
        private List<ReussiteNiveau> _listeReussites = [];
        private List<Niveau> _listeNiveaux = [];
        private List<CreateurNiveau> _listeCreateurs = [];
        private List<Difficulte> _listeFeatures = [];

        private Dictionary<int, string?> _avatarUrlParUtilisateurId = [];

        private List<UtilisateurAvecPoints> _utilisateursAvecPoints = [];
        private List<CreateurAvecNiveaux> _createursAvecNiveaux = [];
        private List<NiveauAvecPoints>? _niveauxReussis = [];
        private List<NiveauAvecPoints>? _niveauxVerifies = [];
        private List<NiveauSimple>? _niveauxCrees = [];

        private UtilisateurAvecPoints? _utilisateurSelectionne;
        private CreateurAvecNiveaux? _meilleurCreateur;
        private UtilisateurAvecPoints? _meilleurReussiteur;
        private bool _afficherMenuTri;
        private bool _fermetureMenuTriEnCours;
        private string _searchQuery = string.Empty;
        private Liste? _listeCourante;
        private int _listeId;

        private enum TabVue { Players, Creators, Wins }
        private enum TriParJoueur { PointsDesc, PointsAsc, NameAsc, NameDesc }
        private enum TriParCreateur { CountDesc, CountAsc, NameAsc, NameDesc }
        private enum TriParVictoire { WinsDesc, WinsAsc, NameAsc, NameDesc }

        private TabVue _tabActuel = TabVue.Players;
        private TabVue _tabEnAttente;
        private TriParJoueur _triParJoueur = TriParJoueur.PointsDesc;
        private TriParCreateur _triParCreateur = TriParCreateur.CountDesc;
        private TriParVictoire _triParVictoire = TriParVictoire.WinsDesc;
        private TriParJoueur _enAttenteTriParJoueur;
        private TriParCreateur _enAttenteTriParCreateur;
        private TriParVictoire _enAttenteTriParVictoire;

        protected override async Task OnInitializedAsync()
        {
            int? listeIdDemande = ListeId ?? ListeSession.ListeId;
            if (listeIdDemande is not int listeId)
            {
                NavigationManager.NavigateTo("/");
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
                    NavigationManager.NavigateTo("/404");
                    return;
                }

                _avatarUrlParUtilisateurId = await dbContext.DiscordAccounts
                    .AsNoTracking()
                    .ToDictionaryAsync(a => a.UtilisateurId, a => ObtenirUrlAvatar(a.DiscordId, a.AvatarHash));
            }

            _listeId = listeId;
            ListeSession.SetListe(_listeCourante.Id, _listeCourante.Nom, _listeCourante.DiscordServerUrl);

            string cheminCanonique = SeoUtils.CheminClassement(_listeCourante.Id, _listeCourante.Nom);
            string cheminActuel = new Uri(NavigationManager.Uri).AbsolutePath.TrimEnd('/');
            if (!cheminActuel.Equals(cheminCanonique, StringComparison.OrdinalIgnoreCase))
            {
                string destination = JoueurSelectionneId is int utilisateurId
                    ? $"{cheminCanonique}?joueur={utilisateurId}"
                    : cheminCanonique;
                NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(destination, Texte.CodeLangue), replace: true);
                return;
            }

            (_listeClassements, _listeCreateurs, _listeUtilisateurs, _listeNiveaux, _listeReussites, _listeFeatures) =
                Chargement.Cache(_listeClassements, _listeCreateurs, _listeUtilisateurs, _listeNiveaux, _listeReussites, _listeFeatures, listeId, DbContextOptions);

            ChargerClassements();
        }

        protected override void OnParametersSet()
        {
            if (_listeId == 0 || (_utilisateursAvecPoints.Count == 0 && _createursAvecNiveaux.Count == 0)) return;

            if (ParametreJoueurInvalide)
            {
                NavigationManager.NavigateTo(
                    SeoUtils.LocaliserChemin(ObtenirCheminCanonique(), Texte.CodeLangue),
                    replace: true);
                return;
            }

            if (JoueurSelectionneId is int utilisateurId)
            {
                if (AfficherDetailsUtilisateur(utilisateurId)) return;

                NavigationManager.NavigateTo(
                    SeoUtils.LocaliserChemin(ObtenirCheminCanonique(), Texte.CodeLangue),
                    replace: true);
                return;
            }

            int idParDefaut = _utilisateursAvecPoints.FirstOrDefault()?.Id
                ?? _createursAvecNiveaux.FirstOrDefault()?.Id
                ?? 0;

            if (idParDefaut > 0) AfficherDetailsUtilisateur(idParDefaut);
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

        private string TabClassEnAttente(TabVue tab) =>
            _tabEnAttente == tab ? "tab active" : "tab";

        private string ClasseFermetureMenuTri => _fermetureMenuTriEnCours ? "is-closing" : string.Empty;

        private async Task FermerMenuTri()
        {
            if (!_afficherMenuTri || _fermetureMenuTriEnCours)
            {
                return;
            }

            _fermetureMenuTriEnCours = true;
            await Task.Delay(180);
            _afficherMenuTri = false;
            _fermetureMenuTriEnCours = false;
        }

        private void TabEnAttente(TabVue tab) => _tabEnAttente = tab;

        private async Task ToggleMenuTri()
        {
            if (_afficherMenuTri)
            {
                await FermerMenuTri();
                return;
            }

            _tabEnAttente = _tabActuel;
            _enAttenteTriParJoueur = _triParJoueur;
            _enAttenteTriParCreateur = _triParCreateur;
            _enAttenteTriParVictoire = _triParVictoire;

            _fermetureMenuTriEnCours = false;
            _afficherMenuTri = true;
        }

        private async Task AppliquerLeTriEtFermer()
        {
            _tabActuel = _tabEnAttente;
            _triParJoueur = _enAttenteTriParJoueur;
            _triParCreateur = _enAttenteTriParCreateur;
            _triParVictoire = _enAttenteTriParVictoire;
            await FermerMenuTri();
        }

        private async Task ResetSort()
        {
            _tabEnAttente = TabVue.Players;
            _enAttenteTriParJoueur = TriParJoueur.PointsDesc;
            _enAttenteTriParCreateur = TriParCreateur.CountDesc;
            _enAttenteTriParVictoire = TriParVictoire.WinsDesc;
            await AppliquerLeTriEtFermer();
        }

        private static string ObtenirUrlAvatar(string discordId, string? avatarHash) =>
            !string.IsNullOrWhiteSpace(avatarHash)
                ? $"https://cdn.discordapp.com/avatars/{discordId}/{avatarHash}.png?size=512"
                : "https://cdn.discordapp.com/embed/avatars/0.png";

        private string ConvertirDuree(int secondes)
        {
            int minutes = secondes / 60;
            int s = secondes % 60;
            return $"{minutes}min {s}sec";
        }

        private string ObtenirClassBackground(int id) =>
            _utilisateurSelectionne != null && _utilisateurSelectionne.Id == id ? "selected" : string.Empty;

        private IEnumerable<UtilisateurAvecPoints> ObtenirLaVueJoueur() =>
            _triParJoueur switch
            {
                TriParJoueur.PointsAsc => _utilisateursAvecPoints.OrderBy(u => u.TotalPoints),
                TriParJoueur.NameAsc => _utilisateursAvecPoints.OrderBy(u => u.Nom),
                TriParJoueur.NameDesc => _utilisateursAvecPoints.OrderByDescending(u => u.Nom),
                _ => _utilisateursAvecPoints.OrderByDescending(u => u.TotalPoints),
            };

        private IEnumerable<CreateurAvecNiveaux> ObtenirLaVueCreateur() =>
            _triParCreateur switch
            {
                TriParCreateur.CountAsc => _createursAvecNiveaux.OrderBy(c => c.NombreNiveaux),
                TriParCreateur.NameAsc => _createursAvecNiveaux.OrderBy(c => c.Nom),
                TriParCreateur.NameDesc => _createursAvecNiveaux.OrderByDescending(c => c.Nom),
                _ => _createursAvecNiveaux.OrderByDescending(c => c.NombreNiveaux),
            };

        private IEnumerable<UtilisateurAvecPoints> ObtenirLaVueVictoire()
        {
            var ordered = _triParVictoire switch
            {
                TriParVictoire.WinsAsc => _utilisateursAvecPoints.OrderBy(u => u.TotalNiveauxReussis).ToList(),
                TriParVictoire.NameAsc => _utilisateursAvecPoints.OrderBy(u => u.Nom).ToList(),
                TriParVictoire.NameDesc => _utilisateursAvecPoints.OrderByDescending(u => u.Nom).ToList(),
                _ => _utilisateursAvecPoints.OrderByDescending(u => u.TotalNiveauxReussis).ToList(),
            };

            for (int i = 0; i < ordered.Count; i++)
            {
                if (i == 0)
                {
                    ordered[i].Classement = 1;
                }
                else
                {
                    if (_triParVictoire == TriParVictoire.WinsAsc || _triParVictoire == TriParVictoire.WinsDesc)
                    {
                        if (ordered[i].TotalNiveauxReussis == ordered[i - 1].TotalNiveauxReussis)
                        {
                            ordered[i].Classement = ordered[i - 1].Classement;
                        }
                        else
                        {
                            ordered[i].Classement = i + 1;
                        }
                    }
                    else
                    {
                        ordered[i].Classement = i + 1;
                    }
                }
            }
            return ordered;
        }

        private void ChargerClassements()
        {
            Dictionary<int, int> verifieurParNiveauId = _listeNiveaux.ToDictionary(n => n.Id, n => n.VerifieurId);
            Dictionary<int, string> nomParNiveauId = _listeNiveaux.ToDictionary(n => n.Id, n => n.Nom);

            int PointsVerifieur(int userId) =>
                _listeClassements
                    .Where(c => verifieurParNiveauId.TryGetValue(c.NiveauId, out int verifId) && verifId == userId)
                    .Sum(c => c.Points);

            int PointsReussis(int userId) =>
                _listeReussites
                    .Where(r => r.UtilisateurId == userId && r.Statut == "Validee")
                    .Join(_listeClassements, r => r.NiveauId, c => c.NiveauId, (_, c) => c.Points)
                    .Sum();

            int TotalNiveauxReussis(int userId) =>
                _listeReussites.Count(r => r.UtilisateurId == userId && r.Statut == "Validee")
                + _listeNiveaux.Count(n => n.VerifieurId == userId);

            int CountNiveauxCrees(int userId) =>
                _listeCreateurs.Count(cn => cn.CreateurId == userId);

            List<UtilisateurAvecPoints> utilisateursNonClasses = _listeUtilisateurs
                .Select(u => new UtilisateurAvecPoints
                {
                    Id = u.Id,
                    Nom = u.Nom,
                    CodePays = u.CodePays,
                    AvatarUrl = _avatarUrlParUtilisateurId.GetValueOrDefault(u.Id),
                    TotalPoints = PointsVerifieur(u.Id) + PointsReussis(u.Id),
                    TotalNiveauxReussis = TotalNiveauxReussis(u.Id),
                    TotalNiveauxCreer = CountNiveauxCrees(u.Id)
                })
                .Where(x => x.TotalPoints > 0)
                .OrderByDescending(x => x.TotalPoints)
                .ToList();

            _utilisateursAvecPoints = utilisateursNonClasses
                .OrderByDescending(x => x.TotalPoints)
                .ToList();

            for (int i = 0; i < _utilisateursAvecPoints.Count; i++)
            {
                if (i == 0)
                {
                    _utilisateursAvecPoints[i].Classement = 1;
                }
                else
                {
                    if (_utilisateursAvecPoints[i].TotalPoints == _utilisateursAvecPoints[i - 1].TotalPoints)
                    {
                        _utilisateursAvecPoints[i].Classement = _utilisateursAvecPoints[i - 1].Classement;
                    }
                    else
                    {
                        _utilisateursAvecPoints[i].Classement = i + 1;
                    }
                }
            }

            _createursAvecNiveaux = _listeUtilisateurs
                .Where(u => _listeCreateurs.Any(cn => cn.CreateurId == u.Id))
                .Select(u => new CreateurAvecNiveaux
                {
                    Id = u.Id,
                    Nom = u.Nom,
                    CodePays = u.CodePays,
                    AvatarUrl = _avatarUrlParUtilisateurId.GetValueOrDefault(u.Id),
                    NombreNiveaux = _listeCreateurs.Count(cn => cn.CreateurId == u.Id)
                })
                .OrderByDescending(c => c.NombreNiveaux)
                .ToList();

            for (int i = 0; i < _createursAvecNiveaux.Count; i++)
            {
                if (i == 0)
                {
                    _createursAvecNiveaux[i].Classement = 1;
                }
                else
                {
                    if (_createursAvecNiveaux[i].NombreNiveaux == _createursAvecNiveaux[i - 1].NombreNiveaux)
                    {
                        _createursAvecNiveaux[i].Classement = _createursAvecNiveaux[i - 1].Classement;
                    }
                    else
                    {
                        _createursAvecNiveaux[i].Classement = i + 1;
                    }
                }
            }

            _meilleurCreateur = _createursAvecNiveaux.FirstOrDefault();
            _meilleurReussiteur = _utilisateursAvecPoints.OrderByDescending(u => u.TotalNiveauxReussis).FirstOrDefault();
        }

        private bool AfficherDetailsUtilisateur(int utilisateurId)
        {
            _utilisateurSelectionne = _utilisateursAvecPoints.FirstOrDefault(u => u.Id == utilisateurId)
                ?? _createursAvecNiveaux
                    .Where(c => c.Id == utilisateurId)
                    .Select(c => new UtilisateurAvecPoints
                    {
                        Id = c.Id,
                        Nom = c.Nom,
                        CodePays = c.CodePays,
                        AvatarUrl = c.AvatarUrl,
                        TotalPoints = 0,
                        TotalNiveauxReussis = 0,
                        TotalNiveauxCreer = c.NombreNiveaux
                    })
                    .FirstOrDefault();

            if (_utilisateurSelectionne == null) return false;

            Dictionary<int, string> nomParNiveauId = _listeNiveaux.ToDictionary(n => n.Id, n => n.Nom);
            Dictionary<int, string> urlParNiveauId = _listeNiveaux.ToDictionary(n => n.Id, n => n.UrlVerification);
            Dictionary<int, int> dureeParNiveauId = _listeNiveaux.ToDictionary(n => n.Id, n => n.Duree);
            Dictionary<int, Classement> classementParNiv = _listeClassements.ToDictionary(c => c.NiveauId, c => c);

            _niveauxReussis = _listeReussites
                .Where(r => r.UtilisateurId == utilisateurId && r.Statut == "Validee")
                .Join(_listeClassements,
                    r => r.NiveauId,
                    c => c.NiveauId,
                    (r, c) => new { r.NiveauId, r.Video, c.Points, c.ClassementPosition })
                .Select(x => new NiveauAvecPoints
                {
                    Id = x.NiveauId,
                    Nom = nomParNiveauId.GetValueOrDefault(x.NiveauId, $"Niveau {x.NiveauId}"),
                    Points = x.Points,
                    ClassementPosition = x.ClassementPosition,
                    Video = x.Video,
                    Duree = dureeParNiveauId.GetValueOrDefault(x.NiveauId, 0)
                })
                .OrderBy(n => n.ClassementPosition)
                .ToList();

            HashSet<int> idsVerifies = _listeNiveaux
                .Where(n => n.VerifieurId == utilisateurId)
                .Select(n => n.Id)
                .ToHashSet();

            _niveauxVerifies = _listeClassements
                .Where(c => idsVerifies.Contains(c.NiveauId))
                .Select(c => new NiveauAvecPoints
                {
                    Id = c.NiveauId,
                    Nom = nomParNiveauId.GetValueOrDefault(c.NiveauId, $"Niveau {c.NiveauId}"),
                    Points = c.Points,
                    ClassementPosition = c.ClassementPosition,
                    UrlVerification = urlParNiveauId.GetValueOrDefault(c.NiveauId),
                    Duree = dureeParNiveauId.GetValueOrDefault(c.NiveauId, 0)
                })
                .OrderBy(n => n.ClassementPosition)
                .ToList();

            _niveauxCrees = _listeCreateurs
                .Where(cn => cn.CreateurId == utilisateurId)
                .Select(cn =>
                {
                    classementParNiv.TryGetValue(cn.NiveauId, out Classement? c);
                    return new NiveauSimple
                    {
                        Id = cn.NiveauId,
                        Nom = nomParNiveauId.GetValueOrDefault(cn.NiveauId, $"Niveau {cn.NiveauId}"),
                        ClassementPosition = c?.ClassementPosition ?? 0,
                        Points = c?.Points ?? 0,
                        Duree = dureeParNiveauId.GetValueOrDefault(cn.NiveauId, 0),
                        Video = urlParNiveauId.GetValueOrDefault(cn.NiveauId)
                    };
                })
                .OrderBy(n => n.ClassementPosition)
                .ToList();

            return true;
        }

        private string ObtenirCheminNiveau(int niveauId)
        {
            string chemin = SeoUtils.CheminListe(_listeId, _listeCourante?.Nom ?? ListeSession.ListeNom ?? "demon-list");
            Niveau? niveau = _listeNiveaux.FirstOrDefault(n => n.Id == niveauId);
            string destination = niveau is null
                ? chemin
                : $"{chemin}?niveau={Uri.EscapeDataString(niveau.IdDuNiveauDansLeJeu)}";
            return SeoUtils.LocaliserChemin(destination, Texte.CodeLangue);
        }

        private string ObtenirCheminUtilisateur(int utilisateurId) =>
            SeoUtils.LocaliserChemin($"{ObtenirCheminCanonique()}?joueur={utilisateurId}", Texte.CodeLangue);

        private class UtilisateurAvecPoints
        {
            public int Id { get; set; }
            public string? Nom { get; set; }
            public string? CodePays { get; set; }
            public string? AvatarUrl { get; set; }
            public int TotalPoints { get; set; }
            public int TotalNiveauxReussis { get; set; }
            public int Classement { get; set; }
            public int TotalNiveauxCreer { get; set; }
        }

        private class CreateurAvecNiveaux
        {
            public int Id { get; set; }
            public string? Nom { get; set; }
            public string? CodePays { get; set; }
            public string? AvatarUrl { get; set; }
            public int NombreNiveaux { get; set; }
            public int Classement { get; set; }
        }

        private class NiveauAvecPoints
        {
            public int Id { get; set; }
            public string? Nom { get; set; }
            public int Points { get; set; }
            public string? Video { get; set; }
            public int ClassementPosition { get; set; }
            public string? UrlVerification { get; set; }
            public int Duree { get; set; }
        }

        private class NiveauSimple
        {
            public int Id { get; set; }
            public string? Nom { get; set; }
            public int ClassementPosition { get; set; }
            public int Points { get; set; }
            public int Duree { get; set; }
            public string? Video { get; set; }
        }
    }
}
