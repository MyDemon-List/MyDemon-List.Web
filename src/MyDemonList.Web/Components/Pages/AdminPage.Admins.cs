using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;

namespace MyDemonList.Web.Components.Pages
{
    public partial class AdminPage
    {
        public record AdminLigne(int UtilisateurId, string NomSite, string? DiscordUsername, string? DiscordDisplayName);
        public record DiscordUtilisateurSuggestion(int UtilisateurId, string NomSite, string? DiscordUsername, string? DiscordDisplayName);

        private List<AdminLigne> _admins = [];

        private string _rechercheAdminInput = string.Empty;
        private List<DiscordUtilisateurSuggestion> _rechercheAdminSuggestions = [];
        private DiscordUtilisateurSuggestion? _candidatAdminSelectionne;
        private bool _nominationEnCours;
        private string? _adminsErreur;

        private bool _afficherConfirmationRevocationAdmin;
        private AdminLigne? _adminARevoquer;

        private async Task ChargerAdmins()
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            List<AdminSite> adminsSite = await dbContext.AdminsSite
                .AsNoTracking()
                .Include(a => a.Utilisateur)
                .ToListAsync();

            List<int> idsAdmins = adminsSite.Select(a => a.UtilisateurId).ToList();

            List<DiscordAccount> comptes = await dbContext.DiscordAccounts
                .AsNoTracking()
                .Where(d => idsAdmins.Contains(d.UtilisateurId))
                .ToListAsync();

            _admins = adminsSite
                .Select(a =>
                {
                    DiscordAccount? compte = comptes.FirstOrDefault(d => d.UtilisateurId == a.UtilisateurId);
                    return new AdminLigne(a.UtilisateurId, a.Utilisateur.Nom, compte?.DiscordUsername, compte?.DiscordDisplayName);
                })
                .OrderBy(a => a.NomSite)
                .ToList();
        }

        private async Task OnRechercheAdminInput(ChangeEventArgs e)
        {
            _rechercheAdminInput = e.Value?.ToString() ?? string.Empty;
            _candidatAdminSelectionne = null;
            _adminsErreur = null;

            if (string.IsNullOrWhiteSpace(_rechercheAdminInput))
            {
                _rechercheAdminSuggestions = [];
                return;
            }

            string texte = _rechercheAdminInput.Trim().ToLower();
            HashSet<int> idsExclus = [.. _admins.Select(a => a.UtilisateurId), _utilisateurId];

            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            _rechercheAdminSuggestions = await dbContext.DiscordAccounts
                .AsNoTracking()
                .Include(d => d.Utilisateur)
                .Where(d => !idsExclus.Contains(d.UtilisateurId) &&
                    ((d.DiscordUsername != null && d.DiscordUsername.ToLower().Contains(texte)) ||
                     (d.DiscordDisplayName != null && d.DiscordDisplayName.ToLower().Contains(texte)) ||
                     d.Utilisateur.Nom.ToLower().Contains(texte)))
                .OrderBy(d => d.Utilisateur.Nom)
                .Take(5)
                .Select(d => new DiscordUtilisateurSuggestion(d.UtilisateurId, d.Utilisateur.Nom, d.DiscordUsername, d.DiscordDisplayName))
                .ToListAsync();
        }

        private void SelectCandidatAdmin(DiscordUtilisateurSuggestion candidat)
        {
            _candidatAdminSelectionne = candidat;
            _rechercheAdminInput = candidat.NomSite;
            _rechercheAdminSuggestions = [];
        }

        private async Task NommerAdminAsync()
        {
            if (!_estChef) return;

            _adminsErreur = null;

            if (_candidatAdminSelectionne is not DiscordUtilisateurSuggestion candidat)
            {
                _adminsErreur = "Veuillez sélectionner un utilisateur relié à Discord dans la liste de suggestions.";
                return;
            }

            _nominationEnCours = true;

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                bool existeDeja = await dbContext.AdminsSite.AnyAsync(a => a.UtilisateurId == candidat.UtilisateurId);
                if (!existeDeja)
                {
                    dbContext.AdminsSite.Add(new AdminSite { UtilisateurId = candidat.UtilisateurId });
                    MyDemonList.Web.Services.NotificationService.Ajouter(
                        dbContext,
                        candidat.UtilisateurId,
                        TypesNotification.RoleModifie,
                        "Rôle administrateur attribué",
                        "Vous êtes maintenant administrateur du site My Demon List.",
                        "/admin");
                    await dbContext.SaveChangesAsync();
                    NotificationService.Signaler(candidat.UtilisateurId);
                }

                _rechercheAdminInput = string.Empty;
                _candidatAdminSelectionne = null;

                await ChargerAdmins();
            }
            catch (Exception ex)
            {
                _adminsErreur = $"Erreur lors de la nomination : {ex.Message}";
            }
            finally
            {
                _nominationEnCours = false;
            }
        }

        private void DemanderRevocationAdmin(AdminLigne admin)
        {
            if (!_estChef) return;

            _adminARevoquer = admin;
            _afficherConfirmationRevocationAdmin = true;
        }

        private void AnnulerRevocationAdmin()
        {
            _adminARevoquer = null;
            _afficherConfirmationRevocationAdmin = false;
        }

        private async Task ConfirmerRevocationAdmin()
        {
            if (_adminARevoquer is not AdminLigne admin || !_estChef) return;

            _adminsErreur = null;

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                AdminSite? existant = await dbContext.AdminsSite.FirstOrDefaultAsync(a => a.UtilisateurId == admin.UtilisateurId);
                if (existant is not null)
                {
                    dbContext.AdminsSite.Remove(existant);
                    MyDemonList.Web.Services.NotificationService.Ajouter(
                        dbContext,
                        admin.UtilisateurId,
                        TypesNotification.RoleModifie,
                        "Rôle administrateur retiré",
                        "Vous n'êtes plus administrateur du site My Demon List.");
                    await dbContext.SaveChangesAsync();
                    NotificationService.Signaler(admin.UtilisateurId);
                }

                await ChargerAdmins();
            }
            catch (Exception ex)
            {
                _adminsErreur = $"Erreur lors du retrait : {ex.Message}";
            }
            finally
            {
                _afficherConfirmationRevocationAdmin = false;
                _adminARevoquer = null;
            }
        }
    }
}
