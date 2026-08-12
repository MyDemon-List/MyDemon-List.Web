using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Utils;

namespace MyDemonList.Web.Components.Pages
{
    public partial class AdminPage
    {
        public record ListeRef(int Id, string Nom, string? DiscordServerUrl);
        public record DemandeNiveauxAffichage(int Id, int ListeId, string ListeNom, string DemandeurNom, int LimiteActuelle, int ProchaineLimite, List<string> NomsNiveaux, DateTime DateDemande);
        public record DemandeListesAffichage(int Id, int UtilisateurId, string DemandeurNom, int LimiteActuelle, int ProchaineLimite, List<ListeRef> ListesExistantes, DateTime DateDemande);

        private List<DemandeNiveauxAffichage> _demandesNiveaux = [];
        private List<DemandeListesAffichage> _demandesListes = [];
        private string? _quotaMessage;

        private async Task ChargerDemandesQuota()
        {
            List<DemandeNiveauxSupplementaires> demandesNiveaux = await QuotaService.ObtenirDemandesEnAttenteNiveauxAsync();
            List<DemandeListesSupplementaires> demandesListes = await QuotaService.ObtenirDemandesEnAttenteListesAsync();

            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            List<DemandeNiveauxAffichage> resultatNiveaux = [];
            foreach (DemandeNiveauxSupplementaires demande in demandesNiveaux)
            {
                int approbations = await QuotaService.CompterApprobationsNiveauxAsync(demande.ListeId);
                List<string> noms = await dbContext.Niveaux
                    .AsNoTracking()
                    .Where(n => n.ListeId == demande.ListeId)
                    .OrderBy(n => n.Nom)
                    .Select(n => n.Nom)
                    .ToListAsync();

                resultatNiveaux.Add(new DemandeNiveauxAffichage(
                    demande.Id,
                    demande.ListeId,
                    demande.Liste.Nom,
                    demande.UtilisateurDemandeur.Nom,
                    QuotaService.LimiteNiveauxActuelle(approbations),
                    QuotaService.ProchaineLimiteNiveaux(approbations),
                    noms,
                    demande.DateDemande));
            }
            _demandesNiveaux = resultatNiveaux;

            List<DemandeListesAffichage> resultatListes = [];
            foreach (DemandeListesSupplementaires demande in demandesListes)
            {
                int approbations = await QuotaService.CompterApprobationsListesAsync(demande.UtilisateurId);
                List<ListeRef> listesExistantes = await dbContext.Listes
                    .AsNoTracking()
                    .Where(l => l.UtilisateurId == demande.UtilisateurId)
                    .OrderBy(l => l.Nom)
                    .Select(l => new ListeRef(l.Id, l.Nom, l.DiscordServerUrl))
                    .ToListAsync();

                resultatListes.Add(new DemandeListesAffichage(
                    demande.Id,
                    demande.UtilisateurId,
                    demande.Utilisateur.Nom,
                    QuotaService.LimiteListesActuelle(approbations),
                    QuotaService.ProchaineLimiteListes(approbations),
                    listesExistantes,
                    demande.DateDemande));
            }
            _demandesListes = resultatListes;
        }

        private async Task AccepterDemandeNiveaux(int demandeId)
        {
            if (!_estAutorise) return;

            (bool succes, string message) = await QuotaService.AccepterDemandeNiveauxAsync(demandeId);
            _quotaMessage = message;

            if (succes)
                await ChargerDemandesQuota();
        }

        private async Task RefuserDemandeNiveaux(int demandeId)
        {
            if (!_estAutorise) return;

            (bool succes, string message) = await QuotaService.RefuserDemandeNiveauxAsync(demandeId);
            _quotaMessage = message;

            if (succes)
                await ChargerDemandesQuota();
        }

        private async Task AccepterDemandeListes(int demandeId)
        {
            if (!_estAutorise) return;

            (bool succes, string message) = await QuotaService.AccepterDemandeListesAsync(demandeId);
            _quotaMessage = message;

            if (succes)
                await ChargerDemandesQuota();
        }

        private async Task RefuserDemandeListes(int demandeId)
        {
            if (!_estAutorise) return;

            (bool succes, string message) = await QuotaService.RefuserDemandeListesAsync(demandeId);
            _quotaMessage = message;

            if (succes)
                await ChargerDemandesQuota();
        }

        private void PrevisualiserListe(ListeRef liste)
        {
            ListeSession.SetListe(liste.Id, liste.Nom, liste.DiscordServerUrl);
            NavigationManager.NavigateTo(SeoUtils.LocaliserChemin("/liste", Texte.CodeLangue));
        }
    }
}
