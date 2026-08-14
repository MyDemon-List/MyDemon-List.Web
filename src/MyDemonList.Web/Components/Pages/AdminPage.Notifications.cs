using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;

namespace MyDemonList.Web.Components.Pages
{
    public partial class AdminPage
    {
        private record DestinataireNotification(int UtilisateurId, string NomSite, string? DiscordUsername, string? DiscordDisplayName);

        private bool _notificationPourTous;
        private string _rechercheDestinataireNotification = string.Empty;
        private List<DestinataireNotification> _suggestionsDestinatairesNotification = [];
        private DestinataireNotification? _destinataireNotification;
        private string _titreNotification = string.Empty;
        private string _messageNotification = string.Empty;
        private string _lienNotification = string.Empty;
        private string? _notificationAdminMessage;
        private bool _envoiNotificationEnCours;

        private void BasculerNotificationPourTous(ChangeEventArgs e)
        {
            _notificationPourTous = _estChef && e.Value is bool valeur && valeur;
            if (_notificationPourTous)
            {
                _rechercheDestinataireNotification = string.Empty;
                _suggestionsDestinatairesNotification = [];
                _destinataireNotification = null;
            }
        }

        private async Task RechercherDestinataireNotification(ChangeEventArgs e)
        {
            _rechercheDestinataireNotification = e.Value?.ToString() ?? string.Empty;
            _destinataireNotification = null;
            _notificationAdminMessage = null;

            if (_notificationPourTous || string.IsNullOrWhiteSpace(_rechercheDestinataireNotification))
            {
                _suggestionsDestinatairesNotification = [];
                return;
            }

            string recherche = _rechercheDestinataireNotification.Trim().ToLower();
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            List<DiscordAccount> comptes = await dbContext.DiscordAccounts
                .AsNoTracking()
                .Include(d => d.Utilisateur)
                .Where(d =>
                    d.Utilisateur.Nom.ToLower().Contains(recherche) ||
                    (d.DiscordUsername != null && d.DiscordUsername.ToLower().Contains(recherche)) ||
                    (d.DiscordDisplayName != null && d.DiscordDisplayName.ToLower().Contains(recherche)))
                .OrderBy(d => d.Utilisateur.Nom)
                .Take(15)
                .ToListAsync();

            _suggestionsDestinatairesNotification = comptes
                .GroupBy(d => d.UtilisateurId)
                .Select(groupe =>
                {
                    DiscordAccount compte = groupe.First();
                    return new DestinataireNotification(
                        compte.UtilisateurId,
                        compte.Utilisateur.Nom,
                        compte.DiscordUsername,
                        compte.DiscordDisplayName);
                })
                .Take(5)
                .ToList();
        }

        private void SelectionnerDestinataireNotification(DestinataireNotification destinataire)
        {
            _destinataireNotification = destinataire;
            _rechercheDestinataireNotification = destinataire.NomSite;
            _suggestionsDestinatairesNotification = [];
        }

        private async Task EnvoyerNotificationAdminAsync()
        {
            if (!_estAutorise || _envoiNotificationEnCours) return;

            if (_notificationPourTous && !_estChef)
            {
                _notificationPourTous = false;
                _notificationAdminMessage = Texte["NotificationTousReserveeChef", "Seul le super-administrateur peut envoyer une notification à tous les utilisateurs."];
                return;
            }

            _notificationAdminMessage = null;
            string titre = _titreNotification.Trim();
            string message = _messageNotification.Trim();
            string? lien = string.IsNullOrWhiteSpace(_lienNotification) ? null : _lienNotification.Trim();

            if (titre.Length is < 3 or > 160)
            {
                _notificationAdminMessage = Texte["TitreNotificationInvalide", "Le titre doit contenir entre 3 et 160 caractères."];
                return;
            }

            if (message.Length is < 3 or > 2000)
            {
                _notificationAdminMessage = Texte["MessageNotificationInvalide", "Le message doit contenir entre 3 et 2 000 caractères."];
                return;
            }

            if (lien is not null && (!lien.StartsWith('/') || lien.StartsWith("//", StringComparison.Ordinal)))
            {
                _notificationAdminMessage = Texte["LienNotificationInvalide", "Le lien doit être un chemin interne commençant par /, par exemple /profil."];
                return;
            }

            if (!_notificationPourTous && _destinataireNotification is null)
            {
                _notificationAdminMessage = Texte["DestinataireRequis", "Sélectionnez un destinataire dans les propositions."];
                return;
            }

            _envoiNotificationEnCours = true;

            try
            {
                if (_notificationPourTous)
                {
                    int nombre = await NotificationService.EnvoyerATousAsync(titre, message, lien);
                    _notificationAdminMessage = Texte.Formater("NotificationEnvoyeePlusieurs", "Notification envoyée à {0} utilisateur(s).", nombre);
                }
                else if (_destinataireNotification is DestinataireNotification destinataire)
                {
                    await NotificationService.EnvoyerAsync(destinataire.UtilisateurId, titre, message, lien);
                    _notificationAdminMessage = Texte.Formater("NotificationEnvoyee", "Notification envoyée à {0}.", destinataire.NomSite);
                }

                _titreNotification = string.Empty;
                _messageNotification = string.Empty;
                _lienNotification = string.Empty;
                _rechercheDestinataireNotification = string.Empty;
                _destinataireNotification = null;
                _suggestionsDestinatairesNotification = [];
            }
            catch (Exception ex)
            {
                _notificationAdminMessage = $"Envoi impossible : {ex.Message}";
            }
            finally
            {
                _envoiNotificationEnCours = false;
            }
        }
    }
}
