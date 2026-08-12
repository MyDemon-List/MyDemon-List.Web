using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Utils;

namespace MyDemonList.Web.Services
{
    public class QuotaService
    {
        private static readonly int[] PaliersNiveaux = [50, 150, 500, 1000, int.MaxValue];
        private static readonly int[] PaliersListes = [3, 10, 30, int.MaxValue];
        public static readonly TimeSpan CooldownApresRefus = TimeSpan.FromDays(3);

        private readonly DbContextOptions<MyDemonListWebDbContext> _dbContextOptions;
        private readonly NotificationService _notificationService;

        public QuotaService(DbContextOptions<MyDemonListWebDbContext> dbContextOptions, NotificationService notificationService)
        {
            _dbContextOptions = dbContextOptions;
            _notificationService = notificationService;
        }

        private static int Palier(int[] paliers, int nombreApprobations) =>
            paliers[Math.Min(nombreApprobations, paliers.Length - 1)];

        public int LimiteNiveauxActuelle(int nombreApprobations) => Palier(PaliersNiveaux, nombreApprobations);
        public int ProchaineLimiteNiveaux(int nombreApprobations) => Palier(PaliersNiveaux, nombreApprobations + 1);

        public int LimiteListesActuelle(int nombreApprobations) => Palier(PaliersListes, nombreApprobations);
        public int ProchaineLimiteListes(int nombreApprobations) => Palier(PaliersListes, nombreApprobations + 1);

        public async Task<int> CompterApprobationsNiveauxAsync(int listeId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            return await dbContext.DemandesNiveauxSupplementaires
                .AsNoTracking()
                .CountAsync(d => d.ListeId == listeId && d.Statut == "Validee");
        }

        public async Task<DemandeNiveauxSupplementaires?> DerniereDemandeNiveauxAsync(int listeId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            return await dbContext.DemandesNiveauxSupplementaires
                .AsNoTracking()
                .Where(d => d.ListeId == listeId)
                .OrderByDescending(d => d.DateDemande)
                .FirstOrDefaultAsync();
        }

        public async Task DemanderNiveauxSupplementairesAsync(int listeId, int utilisateurDemandeurId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            dbContext.DemandesNiveauxSupplementaires.Add(new DemandeNiveauxSupplementaires
            {
                ListeId = listeId,
                UtilisateurDemandeurId = utilisateurDemandeurId,
                Statut = "EnAttente"
            });
            await dbContext.SaveChangesAsync();
        }

        public async Task<(bool Succes, string Message)> AccepterDemandeNiveauxAsync(int demandeId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            DemandeNiveauxSupplementaires? demande = await dbContext.DemandesNiveauxSupplementaires
                .Include(d => d.Liste)
                .FirstOrDefaultAsync(d => d.Id == demandeId);
            if (demande is null || demande.Statut != "EnAttente")
                return (false, "Cette demande n'existe plus ou a déjà été traitée.");

            int approbations = await dbContext.DemandesNiveauxSupplementaires
                .CountAsync(d => d.ListeId == demande.ListeId && d.Statut == "Validee");
            int nouvelleLimite = ProchaineLimiteNiveaux(approbations);
            demande.Statut = "Validee";
            demande.DateTraitement = DateTime.Now;
            NotificationService.Ajouter(
                dbContext,
                demande.UtilisateurDemandeurId,
                TypesNotification.QuotaAccepte,
                "Limite de niveaux augmentée",
                $"La limite de la liste {demande.Liste.Nom} est maintenant de {nouvelleLimite} niveaux.",
                SeoUtils.CheminGestion(demande.ListeId, demande.Liste.Nom));
            await dbContext.SaveChangesAsync();
            _notificationService.Signaler(demande.UtilisateurDemandeurId);
            return (true, "Demande acceptée.");
        }

        public async Task<(bool Succes, string Message)> RefuserDemandeNiveauxAsync(int demandeId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            DemandeNiveauxSupplementaires? demande = await dbContext.DemandesNiveauxSupplementaires
                .Include(d => d.Liste)
                .FirstOrDefaultAsync(d => d.Id == demandeId);
            if (demande is null || demande.Statut != "EnAttente")
                return (false, "Cette demande n'existe plus ou a déjà été traitée.");

            demande.Statut = "Refusee";
            demande.DateTraitement = DateTime.Now;
            NotificationService.Ajouter(
                dbContext,
                demande.UtilisateurDemandeurId,
                TypesNotification.QuotaRefuse,
                "Demande de limite refusée",
                $"L'augmentation de la limite de niveaux pour {demande.Liste.Nom} n'a pas été acceptée.",
                SeoUtils.CheminGestion(demande.ListeId, demande.Liste.Nom));
            await dbContext.SaveChangesAsync();
            _notificationService.Signaler(demande.UtilisateurDemandeurId);
            return (true, "Demande refusée.");
        }

        public async Task<List<DemandeNiveauxSupplementaires>> ObtenirDemandesEnAttenteNiveauxAsync()
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            return await dbContext.DemandesNiveauxSupplementaires
                .AsNoTracking()
                .Include(d => d.Liste)
                .Include(d => d.UtilisateurDemandeur)
                .Where(d => d.Statut == "EnAttente")
                .OrderBy(d => d.DateDemande)
                .ToListAsync();
        }

        public async Task<int> CompterApprobationsListesAsync(int utilisateurId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            return await dbContext.DemandesListesSupplementaires
                .AsNoTracking()
                .CountAsync(d => d.UtilisateurId == utilisateurId && d.Statut == "Validee");
        }

        public async Task<DemandeListesSupplementaires?> DerniereDemandeListesAsync(int utilisateurId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            return await dbContext.DemandesListesSupplementaires
                .AsNoTracking()
                .Where(d => d.UtilisateurId == utilisateurId)
                .OrderByDescending(d => d.DateDemande)
                .FirstOrDefaultAsync();
        }

        public async Task DemanderListesSupplementairesAsync(int utilisateurId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            dbContext.DemandesListesSupplementaires.Add(new DemandeListesSupplementaires
            {
                UtilisateurId = utilisateurId,
                Statut = "EnAttente"
            });
            await dbContext.SaveChangesAsync();
        }

        public async Task<(bool Succes, string Message)> AccepterDemandeListesAsync(int demandeId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            DemandeListesSupplementaires? demande = await dbContext.DemandesListesSupplementaires.FirstOrDefaultAsync(d => d.Id == demandeId);
            if (demande is null || demande.Statut != "EnAttente")
                return (false, "Cette demande n'existe plus ou a déjà été traitée.");

            int approbations = await dbContext.DemandesListesSupplementaires
                .CountAsync(d => d.UtilisateurId == demande.UtilisateurId && d.Statut == "Validee");
            int nouvelleLimite = ProchaineLimiteListes(approbations);
            demande.Statut = "Validee";
            demande.DateTraitement = DateTime.Now;
            NotificationService.Ajouter(
                dbContext,
                demande.UtilisateurId,
                TypesNotification.QuotaAccepte,
                "Limite de listes augmentée",
                $"Vous pouvez maintenant créer jusqu'à {nouvelleLimite} demon lists.",
                "/");
            await dbContext.SaveChangesAsync();
            _notificationService.Signaler(demande.UtilisateurId);
            return (true, "Demande acceptée.");
        }

        public async Task<(bool Succes, string Message)> RefuserDemandeListesAsync(int demandeId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            DemandeListesSupplementaires? demande = await dbContext.DemandesListesSupplementaires.FirstOrDefaultAsync(d => d.Id == demandeId);
            if (demande is null || demande.Statut != "EnAttente")
                return (false, "Cette demande n'existe plus ou a déjà été traitée.");

            demande.Statut = "Refusee";
            demande.DateTraitement = DateTime.Now;
            NotificationService.Ajouter(
                dbContext,
                demande.UtilisateurId,
                TypesNotification.QuotaRefuse,
                "Demande de limite refusée",
                "Votre demande d'augmentation du nombre de demon lists n'a pas été acceptée.",
                "/");
            await dbContext.SaveChangesAsync();
            _notificationService.Signaler(demande.UtilisateurId);
            return (true, "Demande refusée.");
        }

        public async Task<List<DemandeListesSupplementaires>> ObtenirDemandesEnAttenteListesAsync()
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            return await dbContext.DemandesListesSupplementaires
                .AsNoTracking()
                .Include(d => d.Utilisateur)
                .Where(d => d.Statut == "EnAttente")
                .OrderBy(d => d.DateDemande)
                .ToListAsync();
        }

        public TimeSpan? CooldownRestant(DemandeNiveauxSupplementaires? derniereDemande) =>
            CooldownRestantInterne(derniereDemande?.Statut, derniereDemande?.DateTraitement);

        public TimeSpan? CooldownRestant(DemandeListesSupplementaires? derniereDemande) =>
            CooldownRestantInterne(derniereDemande?.Statut, derniereDemande?.DateTraitement);

        private static TimeSpan? CooldownRestantInterne(string? statut, DateTime? dateTraitement)
        {
            if (statut != "Refusee" || dateTraitement is null) return null;

            DateTime finCooldown = dateTraitement.Value.Add(CooldownApresRefus);
            TimeSpan restant = finCooldown - DateTime.Now;
            return restant > TimeSpan.Zero ? restant : null;
        }
    }
}
