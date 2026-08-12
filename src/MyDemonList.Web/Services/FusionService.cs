using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;

namespace MyDemonList.Web.Services
{
    public class FusionService
    {
        public record InfosCompteFusion(
            int UtilisateurId,
            string Nom,
            string? DiscordUsername,
            string? DiscordDisplayName,
            int NombreReussitesValidees,
            int NombreListesPossedees,
            int NombreNiveauxPublies,
            int NombreNiveauxVerifies,
            int NombreNiveauxCrees);

        private readonly DbContextOptions<MyDemonListWebDbContext> _dbContextOptions;
        private readonly NotificationService _notificationService;

        public FusionService(DbContextOptions<MyDemonListWebDbContext> dbContextOptions, NotificationService notificationService)
        {
            _dbContextOptions = dbContextOptions;
            _notificationService = notificationService;
        }

        private static int PrioriteStatut(string? statut) => statut switch
        {
            "Validee" => 2,
            "EnAttente" => 1,
            _ => 0
        };

        public async Task<InfosCompteFusion> ObtenirInfosCompteAsync(int utilisateurId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);

            Utilisateur? utilisateur = await dbContext.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.Id == utilisateurId);
            DiscordAccount? discord = await dbContext.DiscordAccounts.AsNoTracking().FirstOrDefaultAsync(d => d.UtilisateurId == utilisateurId);

            int reussites = await dbContext.ReussitesNiveaux.AsNoTracking().CountAsync(r => r.UtilisateurId == utilisateurId && r.Statut == "Validee");
            int listes = await dbContext.Listes.AsNoTracking().CountAsync(l => l.UtilisateurId == utilisateurId);
            int publies = await dbContext.Niveaux.AsNoTracking().CountAsync(n => n.PublisherId == utilisateurId);
            int verifies = await dbContext.Niveaux.AsNoTracking().CountAsync(n => n.VerifieurId == utilisateurId);
            int crees = await dbContext.CreateursNiveaux.AsNoTracking().CountAsync(cn => cn.CreateurId == utilisateurId);

            return new InfosCompteFusion(
                utilisateurId,
                utilisateur?.Nom ?? "?",
                discord?.DiscordUsername,
                discord?.DiscordDisplayName,
                reussites,
                listes,
                publies,
                verifies,
                crees);
        }

        public async Task<(bool Succes, string Message)> AccepterAsync(int fusionId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);
            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

            FusionUtilisateur? fusion = await dbContext.FusionsUtilisateurs.FirstOrDefaultAsync(f => f.Id == fusionId);
            if (fusion is null || fusion.Statut != "EnAttente")
                return (false, "Cette demande n'existe plus ou a déjà été traitée.");

            Utilisateur? demandeur = await dbContext.Utilisateurs.FirstOrDefaultAsync(u => u.Id == fusion.UtilisateurDemandeurId);
            Utilisateur? cible = await dbContext.Utilisateurs.FirstOrDefaultAsync(u => u.Id == fusion.UtilisateurCibleId);

            if (demandeur is null || cible is null)
                return (false, "Un des deux comptes n'existe plus.");

            string nomFinal = string.IsNullOrWhiteSpace(fusion.NomConserve) ? cible.Nom : fusion.NomConserve.Trim();

            bool nomPris = await dbContext.Utilisateurs.AnyAsync(u =>
                u.Id != demandeur.Id && u.Id != cible.Id &&
                (u.Nom ?? string.Empty).ToLower() == nomFinal.ToLower());

            if (nomPris)
                return (false, "Le nom à conserver est déjà utilisé par un autre compte, la fusion est annulée.");

            await dbContext.DiscordAccounts
                .Where(a => a.UtilisateurId == demandeur.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.UtilisateurId, cible.Id));

            await dbContext.Niveaux
                .Where(n => n.PublisherId == demandeur.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.PublisherId, cible.Id));

            await dbContext.Niveaux
                .Where(n => n.VerifieurId == demandeur.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.VerifieurId, cible.Id));

            await dbContext.Listes
                .Where(l => l.UtilisateurId == demandeur.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.UtilisateurId, cible.Id));

            List<CreateurNiveau> createurRows = await dbContext.CreateursNiveaux
                .Where(cn => cn.CreateurId == demandeur.Id)
                .ToListAsync();
            List<int> createurCibleNiveauIds = await dbContext.CreateursNiveaux
                .Where(cn => cn.CreateurId == cible.Id)
                .Select(cn => cn.NiveauId)
                .ToListAsync();

            foreach (CreateurNiveau cn in createurRows)
            {
                dbContext.CreateursNiveaux.Remove(cn);
                if (!createurCibleNiveauIds.Contains(cn.NiveauId))
                    dbContext.CreateursNiveaux.Add(new CreateurNiveau { CreateurId = cible.Id, NiveauId = cn.NiveauId });
            }

            List<ReussiteNiveau> reussitesDemandeur = await dbContext.ReussitesNiveaux
                .Where(r => r.UtilisateurId == demandeur.Id)
                .ToListAsync();
            List<ReussiteNiveau> reussitesCible = await dbContext.ReussitesNiveaux
                .Where(r => r.UtilisateurId == cible.Id)
                .ToListAsync();

            foreach (ReussiteNiveau r in reussitesDemandeur)
            {
                ReussiteNiveau? conflit = reussitesCible.FirstOrDefault(x => x.NiveauId == r.NiveauId);

                dbContext.ReussitesNiveaux.Remove(r);

                if (conflit is null)
                {
                    dbContext.ReussitesNiveaux.Add(new ReussiteNiveau
                    {
                        UtilisateurId = cible.Id,
                        NiveauId = r.NiveauId,
                        Video = r.Video,
                        Statut = r.Statut
                    });
                }
                else if (PrioriteStatut(r.Statut) > PrioriteStatut(conflit.Statut))
                {
                    conflit.Statut = r.Statut;
                    conflit.Video = r.Video;
                }
            }

            List<MembreListe> membresDemandeur = await dbContext.MembresListe
                .Where(m => m.UtilisateurId == demandeur.Id)
                .ToListAsync();
            List<int> listeIdsAvecRoleCible = await dbContext.MembresListe
                .Where(m => m.UtilisateurId == cible.Id)
                .Select(m => m.ListeId)
                .ToListAsync();

            foreach (MembreListe m in membresDemandeur)
            {
                dbContext.MembresListe.Remove(m);
                if (!listeIdsAvecRoleCible.Contains(m.ListeId))
                    dbContext.MembresListe.Add(new MembreListe { ListeId = m.ListeId, UtilisateurId = cible.Id, Role = m.Role });
            }

            AdminSite? adminDemandeur = await dbContext.AdminsSite.FirstOrDefaultAsync(a => a.UtilisateurId == demandeur.Id);
            if (adminDemandeur is not null)
            {
                dbContext.AdminsSite.Remove(adminDemandeur);

                bool cibleEstDejaAdmin = await dbContext.AdminsSite.AnyAsync(a => a.UtilisateurId == cible.Id);
                if (!cibleEstDejaAdmin)
                    dbContext.AdminsSite.Add(new AdminSite { UtilisateurId = cible.Id });
            }

            await dbContext.DemandesNiveauxSupplementaires
                .Where(d => d.UtilisateurDemandeurId == demandeur.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.UtilisateurDemandeurId, cible.Id));

            DemandeListesSupplementaires? demandeurDemandeListesEnAttente = await dbContext.DemandesListesSupplementaires
                .FirstOrDefaultAsync(d => d.UtilisateurId == demandeur.Id && d.Statut == "EnAttente");

            if (demandeurDemandeListesEnAttente is not null)
            {
                bool cibleADejaUneDemandeListesEnAttente = await dbContext.DemandesListesSupplementaires
                    .AnyAsync(d => d.UtilisateurId == cible.Id && d.Statut == "EnAttente");

                if (cibleADejaUneDemandeListesEnAttente)
                {
                    demandeurDemandeListesEnAttente.Statut = "Refusee";
                    demandeurDemandeListesEnAttente.DateTraitement = DateTime.Now;
                }
            }

            await dbContext.DemandesListesSupplementaires
                .Where(d => d.UtilisateurId == demandeur.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.UtilisateurId, cible.Id));

            await dbContext.Notifications
                .Where(n => n.UtilisateurId == demandeur.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.UtilisateurId, cible.Id));

            NotificationService.Ajouter(
                dbContext,
                cible.Id,
                TypesNotification.FusionAcceptee,
                "Comptes fusionnés",
                $"La fusion a été effectuée. Toutes les données sont maintenant réunies sous le nom {nomFinal}.",
                "/profil");

            List<FusionUtilisateur> fusionsLiees = await dbContext.FusionsUtilisateurs
                .Where(f => f.UtilisateurDemandeurId == demandeur.Id || f.UtilisateurCibleId == demandeur.Id)
                .ToListAsync();
            dbContext.FusionsUtilisateurs.RemoveRange(fusionsLiees);
            dbContext.Utilisateurs.Remove(demandeur);
            await dbContext.SaveChangesAsync();

            cible.Nom = nomFinal;
            await dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            _notificationService.Signaler(cible.Id);

            return (true, $"Fusion effectuée. Le compte conserve le nom « {nomFinal} ».");
        }

        public async Task<(bool Succes, string Message)> RefuserAsync(int fusionId)
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(_dbContextOptions);

            FusionUtilisateur? fusion = await dbContext.FusionsUtilisateurs.FirstOrDefaultAsync(f => f.Id == fusionId);
            if (fusion is null || fusion.Statut != "EnAttente")
                return (false, "Cette demande n'existe plus ou a déjà été traitée.");

            fusion.Statut = "Refusee";
            fusion.DateTraitement = DateTime.UtcNow;
            NotificationService.Ajouter(
                dbContext,
                fusion.UtilisateurDemandeurId,
                TypesNotification.FusionRefusee,
                "Fusion de comptes refusée",
                "Votre demande de fusion de comptes n'a pas été acceptée.",
                "/profil");
            await dbContext.SaveChangesAsync();

            _notificationService.Signaler(fusion.UtilisateurDemandeurId);

            return (true, "Demande refusée.");
        }
    }
}
