using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;

namespace MyDemonList.Web.Components.Pages
{
    [Authorize]
    public partial class Profile : ComponentBase
    {
        public record FusionAffichage(
            int Id,
            FusionService.InfosCompteFusion Demandeur,
            FusionService.InfosCompteFusion Cible,
            string NomConserve,
            string? Motif,
            DateTime DateDemande);

        [Inject]
        private DbContextOptions<MyDemonListWebDbContext> DbContextOptions { get; set; } = default!;

        [Inject]
        private AuthenticationStateProvider AuthProvider { get; set; } = default!;

        [Inject]
        private FusionService FusionService { get; set; } = default!;

        [Inject]
        private NotificationService NotificationService { get; set; } = default!;

        private bool _isLoading = true;
        private bool _saving = false;
        private string? _erreur;
        private string? _validation;
        private string? _succes;
        private bool _popupFusion = false;
        private bool _confirmerModificationDemande = false;

        private string _fusionTargetInput = "";
        private List<Utilisateur> _fusionTargetSuggestions = new();
        private Utilisateur? _fusionTargetSelectionne;
        private List<Utilisateur> _allUtilisateurs = new();

        private string _nomFusionFinal = "";
        private string _fusionMotif = "";
        private string? _fusionMessage;

        private string? _avatarUrl;
        private string? _discordUsername;
        private string? _discordDisplayName;

        private int _utilisateurId;
        private string _nom = string.Empty;
        private string _nomActuel = string.Empty;
        private string? _discordId;

        private FusionAffichage? _demandeSortante;
        private List<FusionAffichage> _demandesEntrantes = new();

        protected override async Task OnInitializedAsync()
        {
            try
            {
                AuthenticationState authState = await AuthProvider.GetAuthenticationStateAsync();
                ClaimsPrincipal user = authState.User;

                if (user?.Identity?.IsAuthenticated is not true)
                {
                    _erreur = Texte["ConnexionObligatoire", "Vous devez être connecté."];
                    _isLoading = false;
                    return;
                }

                _discordId = user.FindFirst("discord:id")?.Value;
                string? avatar = user.FindFirst("discord:avatar")?.Value;

                if (string.IsNullOrWhiteSpace(_discordId))
                {
                    _erreur = "Identifiant Discord introuvable.";
                    _isLoading = false;
                    return;
                }

                _avatarUrl = (!string.IsNullOrWhiteSpace(_discordId) && !string.IsNullOrWhiteSpace(avatar))
                    ? $"https://cdn.discordapp.com/avatars/{_discordId}/{avatar}.png?size=128"
                    : "https://cdn.discordapp.com/embed/avatars/0.png";

                using (MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions))
                {
                    DiscordAccount? account = await dbContext.DiscordAccounts
                    .Include(a => a.Utilisateur)
                    .SingleOrDefaultAsync(a => a.DiscordId == _discordId);

                    if (account is null || account.Utilisateur is null)
                    {
                        _erreur = Texte["DiscordNonLie", "Le compte Discord n'est pas relié à un utilisateur."];
                        _isLoading = false;
                        return;
                    }

                    _discordUsername = account.DiscordUsername ?? "—";
                    _discordDisplayName = account.DiscordDisplayName ?? _discordUsername ?? "—";
                    _utilisateurId = account.Utilisateur.Id;
                    _nom = account.Utilisateur.Nom ?? string.Empty;
                    _nomActuel = _nom;
                }

                await ChargerDemandesFusion();

                _isLoading = false;
            }
            catch (Exception ex)
            {
                _erreur = Texte.Formater("ErreurChargement", "Erreur lors du chargement : {0}", ex.Message);
                _isLoading = false;
            }
        }

        private async Task ChargerDemandesFusion()
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            List<FusionUtilisateur> enAttente = await dbContext.FusionsUtilisateurs
                .Include(f => f.UtilisateurDemandeur)
                .Include(f => f.UtilisateurCible)
                .Where(f => f.Statut == "EnAttente" &&
                    (f.UtilisateurDemandeurId == _utilisateurId || f.UtilisateurCibleId == _utilisateurId))
                .OrderBy(f => f.DateDemande)
                .ToListAsync();

            async Task<FusionAffichage> Vers(FusionUtilisateur f) => new(
                f.Id,
                await FusionService.ObtenirInfosCompteAsync(f.UtilisateurDemandeurId),
                await FusionService.ObtenirInfosCompteAsync(f.UtilisateurCibleId),
                f.NomConserve,
                f.Motif,
                f.DateDemande);

            FusionUtilisateur? sortante = enAttente.FirstOrDefault(f => f.UtilisateurDemandeurId == _utilisateurId);
            _demandeSortante = sortante is not null ? await Vers(sortante) : null;

            _demandesEntrantes = [];
            foreach (FusionUtilisateur f in enAttente.Where(f => f.UtilisateurCibleId == _utilisateurId))
                _demandesEntrantes.Add(await Vers(f));
        }

        private async Task EnregistrerNom()
        {
            _validation = _succes = null;
            string nouveau = (_nom ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(nouveau))
            {
                _validation = Texte["NomRequis", "Le nom est requis."];
                return;
            }

            if (nouveau.Length < 3)
            {
                _validation = Texte["NomTropCourt", "Le nom doit contenir au moins 3 caractères."];
                return;
            }

            if (nouveau == _nomActuel)
                return;

            try
            {
                _saving = true;

                using (MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions))
                {
                    bool existe = await dbContext.Utilisateurs
                    .AsNoTracking()
                    .AnyAsync(u => u.Id != _utilisateurId && (u.Nom ?? string.Empty).ToLower() == nouveau.ToLower());

                    if (existe)
                    {
                        _validation = Texte["NomDejaPrisFusion", "Ce nom est déjà pris. Si c'est bien vous, vous pouvez faire une demande de fusion de compte."];
                        return;
                    }

                    Utilisateur? utilisateur = await dbContext.Utilisateurs.FindAsync(_utilisateurId);
                    if (utilisateur is null)
                    {
                        _validation = "Utilisateur introuvable.";
                        return;
                    }

                    utilisateur.Nom = nouveau;
                    await dbContext.SaveChangesAsync();

                    _nomActuel = nouveau;
                    _succes = Texte["NomMisAJour", "Nom mis à jour."];
                }
            }
            catch (DbUpdateException)
            {
                _validation = Texte["NomDejaPrisFusion", "Ce nom est déjà utilisé. Si c'est bien vous, vous pouvez faire une demande de fusion de compte."];
            }
            catch (Exception ex)
            {
                _validation = Texte.Formater("EnregistrementImpossible", "Impossible d’enregistrer : {0}", ex.Message);
            }
            finally
            {
                _saving = false;
            }
        }

        private async Task OuvrirPopupFusion()
        {
            _fusionMessage = null;
            _confirmerModificationDemande = false;
            _fusionTargetInput = "";
            _fusionTargetSuggestions = [];
            _fusionTargetSelectionne = null;
            _nomFusionFinal = _nom;
            _fusionMotif = "";

            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);
            _allUtilisateurs = await dbContext.Utilisateurs
                .AsNoTracking()
                .Where(u => u.Id != _utilisateurId)
                .ToListAsync();

            _popupFusion = true;
        }

        private void OnFusionTargetInput(ChangeEventArgs e)
        {
            _fusionTargetInput = e.Value?.ToString() ?? string.Empty;
            _fusionTargetSelectionne = null;

            _fusionTargetSuggestions = string.IsNullOrWhiteSpace(_fusionTargetInput)
                ? []
                : _allUtilisateurs
                    .Where(u => (u.Nom ?? string.Empty).Contains(_fusionTargetInput, StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .ToList();
        }

        private void SelectFusionTarget(Utilisateur utilisateur)
        {
            _fusionTargetSelectionne = utilisateur;
            _fusionTargetInput = utilisateur.Nom ?? string.Empty;
            _fusionTargetSuggestions = [];
            if (_nomFusionFinal != "cible")
                _nomFusionFinal = _nom;
        }

        private async Task EnvoyerDemandeFusion()
        {
            _fusionMessage = null;

            if (_fusionTargetSelectionne is null)
            {
                _fusionMessage = Texte["SelectionCompteRequise", "Veuillez sélectionner un compte dans la liste."];
                return;
            }

            int cibleId = _fusionTargetSelectionne.Id;

            if (cibleId == _utilisateurId)
            {
                _fusionMessage = Texte["FusionSoiMeme", "Impossible de fusionner votre compte avec lui-même."];
                return;
            }

            using (MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions))
            {
                Utilisateur? cible = await dbContext.Utilisateurs.FirstOrDefaultAsync(u => u.Id == cibleId);
                if (cible is null)
                {
                    _fusionMessage = "Ce compte n'existe plus.";
                    return;
                }

                string nomConserve = _nomFusionFinal == "cible" ? cible.Nom! : _nom;

                FusionUtilisateur? demandeExistante = await dbContext.FusionsUtilisateurs
                    .FirstOrDefaultAsync(f => f.UtilisateurDemandeurId == _utilisateurId && f.Statut == "EnAttente");

                if (demandeExistante is not null)
                {
                    if (!_confirmerModificationDemande)
                    {
                        _fusionMessage = Texte["DemandeFusionExiste", "Vous avez déjà une demande de fusion en attente. Voulez-vous la modifier ?"];
                        _confirmerModificationDemande = true;
                        return;
                    }

                    demandeExistante.UtilisateurCibleId = cible.Id;
                    demandeExistante.NomConserve = nomConserve;
                    demandeExistante.Motif = string.IsNullOrWhiteSpace(_fusionMotif) ? null : _fusionMotif.Trim();

                    MyDemonList.Web.Services.NotificationService.Ajouter(
                        dbContext,
                        cible.Id,
                        TypesNotification.Information,
                        "Demande de fusion reçue",
                        $"{_nom} souhaite fusionner son compte avec le vôtre.",
                        "/profil");

                    await dbContext.SaveChangesAsync();
                    NotificationService.Signaler(cible.Id);

                    _fusionMessage = Texte["DemandeModifiee", "La demande existante a été modifiée."];
                    _confirmerModificationDemande = false;
                    _popupFusion = false;
                    await ChargerDemandesFusion();
                    return;
                }

                FusionUtilisateur fusion = new FusionUtilisateur
                {
                    UtilisateurDemandeurId = _utilisateurId,
                    UtilisateurCibleId = cible.Id,
                    NomConserve = nomConserve,
                    Motif = string.IsNullOrWhiteSpace(_fusionMotif) ? null : _fusionMotif.Trim(),
                    Statut = "EnAttente"
                };

                dbContext.FusionsUtilisateurs.Add(fusion);
                MyDemonList.Web.Services.NotificationService.Ajouter(
                    dbContext,
                    cible.Id,
                    TypesNotification.Information,
                    "Demande de fusion reçue",
                    $"{_nom} souhaite fusionner son compte avec le vôtre.",
                    "/profil");
                await dbContext.SaveChangesAsync();
                NotificationService.Signaler(cible.Id);
            }

            _fusionMessage = Texte["DemandeFusionEnvoyee", "Demande envoyée. Le compte ciblé doit maintenant l'accepter depuis son propre profil."];
            _confirmerModificationDemande = false;
            _popupFusion = false;
            await ChargerDemandesFusion();
        }

        private async Task AnnulerMaDemande()
        {
            if (_demandeSortante is null) return;

            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);
            FusionUtilisateur? fusion = await dbContext.FusionsUtilisateurs.FirstOrDefaultAsync(f => f.Id == _demandeSortante.Id);
            if (fusion is not null)
            {
                dbContext.FusionsUtilisateurs.Remove(fusion);
                await dbContext.SaveChangesAsync();
            }

            await ChargerDemandesFusion();
        }

        private string ResumeActivite(FusionService.InfosCompteFusion info)
        {
            List<string> parties = [];
            if (info.NombreReussitesValidees > 0) parties.Add(Texte.Formater("ReussitesResume", "{0} réussite(s)", info.NombreReussitesValidees));
            if (info.NombreListesPossedees > 0) parties.Add(Texte.Formater("ListesPossedeesResume", "{0} liste(s) possédée(s)", info.NombreListesPossedees));
            if (info.NombreNiveauxPublies > 0) parties.Add(Texte.Formater("NiveauxPubliesResume", "{0} niveau(x) publié(s)", info.NombreNiveauxPublies));
            if (info.NombreNiveauxVerifies > 0) parties.Add(Texte.Formater("NiveauxVerifiesResume", "{0} niveau(x) vérifié(s)", info.NombreNiveauxVerifies));
            if (info.NombreNiveauxCrees > 0) parties.Add(Texte.Formater("NiveauxCreesResume", "{0} niveau(x) créé(s)", info.NombreNiveauxCrees));

            return parties.Count > 0 ? string.Join(", ", parties) : Texte["AucuneActiviteSite", "Aucune activité sur le site"];
        }

        private async Task<bool> EstDemandeCibleeSurMoiAsync(int fusionId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);
            return await dbContext.FusionsUtilisateurs
                .AsNoTracking()
                .AnyAsync(f => f.Id == fusionId && f.UtilisateurCibleId == _utilisateurId);
        }

        private async Task AccepterFusion(int fusionId)
        {
            if (!await EstDemandeCibleeSurMoiAsync(fusionId))
            {
                _fusionMessage = "Cette demande ne vous concerne pas.";
                return;
            }

            (bool succes, string message) = await FusionService.AccepterAsync(fusionId);
            _fusionMessage = message;

            if (succes)
            {
                await OnInitializedAsync();
            }
        }

        private async Task RefuserFusion(int fusionId)
        {
            if (!await EstDemandeCibleeSurMoiAsync(fusionId))
            {
                _fusionMessage = "Cette demande ne vous concerne pas.";
                return;
            }

            (bool succes, string message) = await FusionService.RefuserAsync(fusionId);
            _fusionMessage = message;
            if (succes)
                await ChargerDemandesFusion();
        }
    }
}
