using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Utils;

namespace MyDemonList.Web.Components.Pages
{
    public partial class GererListePage
    {
        private record MembreLigne(int UtilisateurId, string NomSite, string? DiscordUsername, string? DiscordDisplayName, RoleListe Role);
        private record DiscordUtilisateurSuggestion(int UtilisateurId, string NomSite, string? DiscordUsername, string? DiscordDisplayName);

        private int _proprietaireUtilisateurId;
        private string _proprietaireNomSite = string.Empty;
        private string? _proprietaireDiscordUsername;
        private string? _proprietaireDiscordDisplayName;
        private List<MembreLigne> _membres = [];

        private string _rechercheMembreInput = string.Empty;
        private List<DiscordUtilisateurSuggestion> _rechercheMembreSuggestions = [];
        private DiscordUtilisateurSuggestion? _candidatSelectionne;
        private RoleListe _roleAAssigner = RoleListe.EditeurNiveaux;
        private bool _assignationEnCours;
        private string? _moderationErreur;

        private bool _afficherConfirmationRevocation;
        private MembreLigne? _membreARevoquer;
        private int? _menuRoleMembreOuvertId;
        private bool _menuRoleAttributionOuvert;
        private readonly Dictionary<int, RoleListe> _rolesMembresEnAttente = [];

        private IEnumerable<RoleListe> RolesAssignables => Enum.GetValues<RoleListe>().Where(PeutAssignerRole);

        private static string NomRole(RoleListe role) => role switch
        {
            RoleListe.Administrateur => "Administrateur",
            RoleListe.EditeurNiveaux => "Éditeur de niveaux",
            RoleListe.Moderateur => "Modérateur",
            _ => role.ToString()
        };

        private static string DescriptionRole(RoleListe role) => role switch
        {
            RoleListe.Administrateur => "Accès complet aux niveaux (créer, modifier, réordonner, supprimer) et aux soumissions (accepter/refuser). Peut nommer et révoquer des Éditeurs et Modérateurs. N'a pas accès aux paramètres de la liste.",
            RoleListe.EditeurNiveaux => "Peut créer, modifier et réordonner les niveaux (pas les supprimer), et gérer les soumissions (accepter/refuser). Aucun accès aux paramètres ni à la modération des rôles.",
            RoleListe.Moderateur => "Peut uniquement accepter ou refuser les soumissions. Aucun accès aux niveaux, aux paramètres ni à la modération des rôles.",
            _ => string.Empty
        };

        private void BasculerMenuRoleMembre(int utilisateurId)
        {
            _menuRoleAttributionOuvert = false;
            _menuRoleMembreOuvertId = _menuRoleMembreOuvertId == utilisateurId ? null : utilisateurId;
        }

        private RoleListe ObtenirRoleMembreSelectionne(MembreLigne membre) =>
            _rolesMembresEnAttente.TryGetValue(membre.UtilisateurId, out RoleListe role)
                ? role
                : membre.Role;

        private void ChoisirRoleMembre(MembreLigne membre, RoleListe role)
        {
            _menuRoleMembreOuvertId = null;

            if (role == membre.Role)
                _rolesMembresEnAttente.Remove(membre.UtilisateurId);
            else
                _rolesMembresEnAttente[membre.UtilisateurId] = role;
        }

        private async Task ValiderRoleMembreAsync(MembreLigne membre)
        {
            if (!_rolesMembresEnAttente.TryGetValue(membre.UtilisateurId, out RoleListe role))
                return;

            await ChangerRoleAsync(membre, role);

            if (string.IsNullOrWhiteSpace(_moderationErreur))
                _rolesMembresEnAttente.Remove(membre.UtilisateurId);
        }

        private void BasculerMenuRoleAttribution()
        {
            _menuRoleMembreOuvertId = null;
            _menuRoleAttributionOuvert = !_menuRoleAttributionOuvert;
        }

        private void ChoisirRoleAttribution(RoleListe role)
        {
            _roleAAssigner = role;
            _menuRoleAttributionOuvert = false;
        }

        private async Task ChargerMembres()
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            List<MembreListe> membresListe = await dbContext.MembresListe
                .AsNoTracking()
                .Where(m => m.ListeId == _listeId)
                .Include(m => m.Utilisateur)
                .ToListAsync();

            List<int> idsUtilisateurs = [.. membresListe.Select(m => m.UtilisateurId), _proprietaireUtilisateurId];

            List<DiscordAccount> comptes = await dbContext.DiscordAccounts
                .AsNoTracking()
                .Where(d => idsUtilisateurs.Contains(d.UtilisateurId))
                .ToListAsync();

            _membres = membresListe
                .Select(m =>
                {
                    DiscordAccount? compte = comptes.FirstOrDefault(d => d.UtilisateurId == m.UtilisateurId);
                    return new MembreLigne(m.UtilisateurId, m.Utilisateur.Nom, compte?.DiscordUsername, compte?.DiscordDisplayName, m.Role);
                })
                .OrderBy(m => m.Role)
                .ThenBy(m => m.NomSite)
                .ToList();

            Utilisateur? proprietaire = await dbContext.Utilisateurs
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == _proprietaireUtilisateurId);

            if (proprietaire is not null)
            {
                DiscordAccount? compteProprietaire = comptes.FirstOrDefault(d => d.UtilisateurId == _proprietaireUtilisateurId);
                _proprietaireNomSite = proprietaire.Nom;
                _proprietaireDiscordUsername = compteProprietaire?.DiscordUsername;
                _proprietaireDiscordDisplayName = compteProprietaire?.DiscordDisplayName;
            }
        }

        private async Task OnRechercheMembreInput(ChangeEventArgs e)
        {
            _rechercheMembreInput = e.Value?.ToString() ?? string.Empty;
            _candidatSelectionne = null;
            _moderationErreur = null;

            if (string.IsNullOrWhiteSpace(_rechercheMembreInput))
            {
                _rechercheMembreSuggestions = [];
                return;
            }

            string texte = _rechercheMembreInput.Trim().ToLower();
            HashSet<int> idsExclus = [.. _membres.Select(m => m.UtilisateurId), _proprietaireUtilisateurId, _utilisateurId];

            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            _rechercheMembreSuggestions = await dbContext.DiscordAccounts
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

        private void SelectCandidatMembre(DiscordUtilisateurSuggestion candidat)
        {
            _candidatSelectionne = candidat;
            _rechercheMembreInput = candidat.NomSite;
            _rechercheMembreSuggestions = [];
        }

        private async Task AssignerRoleAsync()
        {
            _moderationErreur = null;

            if (_candidatSelectionne is not DiscordUtilisateurSuggestion candidat)
            {
                _moderationErreur = "Veuillez sélectionner un utilisateur relié à Discord dans la liste de suggestions.";
                return;
            }

            if (!PeutAssignerRole(_roleAAssigner))
            {
                _moderationErreur = "Vous n'avez pas la permission d'attribuer ce rôle.";
                return;
            }

            _assignationEnCours = true;

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                MembreListe? existant = await dbContext.MembresListe
                    .FirstOrDefaultAsync(m => m.ListeId == _listeId && m.UtilisateurId == candidat.UtilisateurId);

                if (existant is not null)
                {
                    if (!PeutRevoquerRole(existant.Role))
                    {
                        _moderationErreur = "Vous ne pouvez pas modifier le rôle de cet utilisateur.";
                        return;
                    }

                    existant.Role = _roleAAssigner;
                }
                else
                {
                    dbContext.MembresListe.Add(new MembreListe
                    {
                        ListeId = _listeId,
                        UtilisateurId = candidat.UtilisateurId,
                        Role = _roleAAssigner
                    });
                }

                MyDemonList.Web.Services.NotificationService.Ajouter(
                    dbContext,
                    candidat.UtilisateurId,
                    TypesNotification.RoleModifie,
                    "Accès à une liste",
                    $"Le rôle {NomRole(_roleAAssigner)} vous a été attribué sur {_listeNom}.",
                    SeoUtils.CheminGestion(_listeId, _listeNom));

                await dbContext.SaveChangesAsync();
                NotificationService.Signaler(candidat.UtilisateurId);

                _rechercheMembreInput = string.Empty;
                _candidatSelectionne = null;
                _roleAAssigner = RolesAssignables.FirstOrDefault();

                await ChargerMembres();
            }
            catch (Exception ex)
            {
                _moderationErreur = $"Erreur lors de l'attribution du rôle : {ex.Message}";
            }
            finally
            {
                _assignationEnCours = false;
            }
        }

        private async Task ChangerRoleAsync(MembreLigne membre, RoleListe nouveauRole)
        {
            _moderationErreur = null;

            if (nouveauRole == membre.Role) return;

            if (!PeutRevoquerRole(membre.Role) || !PeutAssignerRole(nouveauRole))
            {
                _moderationErreur = "Vous n'avez pas la permission de modifier ce rôle.";
                return;
            }

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                MembreListe? existant = await dbContext.MembresListe
                    .FirstOrDefaultAsync(m => m.ListeId == _listeId && m.UtilisateurId == membre.UtilisateurId);

                if (existant is not null)
                {
                    existant.Role = nouveauRole;
                    MyDemonList.Web.Services.NotificationService.Ajouter(
                        dbContext,
                        membre.UtilisateurId,
                        TypesNotification.RoleModifie,
                        "Rôle modifié",
                        $"Votre rôle sur {_listeNom} est maintenant {NomRole(nouveauRole)}.",
                        SeoUtils.CheminGestion(_listeId, _listeNom));
                    await dbContext.SaveChangesAsync();
                    NotificationService.Signaler(membre.UtilisateurId);
                }

                await ChargerMembres();
            }
            catch (Exception ex)
            {
                _moderationErreur = $"Erreur lors de la modification du rôle : {ex.Message}";
            }
        }

        private void DemanderRevocation(MembreLigne membre)
        {
            if (!PeutRevoquerRole(membre.Role)) return;

            _membreARevoquer = membre;
            _afficherConfirmationRevocation = true;
        }

        private void AnnulerRevocation()
        {
            _membreARevoquer = null;
            _afficherConfirmationRevocation = false;
        }

        private async Task ConfirmerRevocation()
        {
            if (_membreARevoquer is not MembreLigne membre) return;

            _moderationErreur = null;

            if (!PeutRevoquerRole(membre.Role))
            {
                _moderationErreur = "Vous n'avez pas la permission de retirer ce rôle.";
                return;
            }

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                MembreListe? existant = await dbContext.MembresListe
                    .FirstOrDefaultAsync(m => m.ListeId == _listeId && m.UtilisateurId == membre.UtilisateurId);

                if (existant is not null)
                {
                    dbContext.MembresListe.Remove(existant);
                    MyDemonList.Web.Services.NotificationService.Ajouter(
                        dbContext,
                        membre.UtilisateurId,
                        TypesNotification.RoleModifie,
                        "Accès à une liste retiré",
                        $"Votre rôle sur {_listeNom} a été retiré.");
                    await dbContext.SaveChangesAsync();
                    NotificationService.Signaler(membre.UtilisateurId);
                }

                await ChargerMembres();
            }
            catch (Exception ex)
            {
                _moderationErreur = $"Erreur lors du retrait du rôle : {ex.Message}";
            }
            finally
            {
                _afficherConfirmationRevocation = false;
                _membreARevoquer = null;
            }
        }
    }
}
