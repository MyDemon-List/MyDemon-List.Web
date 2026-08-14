using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;

namespace MyDemonList.Web.Services
{
    public sealed class NotificationService
    {
        private readonly IDbContextFactory<MyDemonListWebDbContext> _dbContextFactory;
        private readonly NotificationSignalService _signal;

        public NotificationService(
            IDbContextFactory<MyDemonListWebDbContext> dbContextFactory,
            NotificationSignalService signal)
        {
            _dbContextFactory = dbContextFactory;
            _signal = signal;
        }

        public static Notification Ajouter(
            MyDemonListWebDbContext dbContext,
            int utilisateurId,
            string type,
            string titre,
            string message,
            string? lien = null)
        {
            Notification notification = new Notification
            {
                UtilisateurId = utilisateurId,
                Type = Limiter(type, 50),
                Titre = Limiter(titre, 160),
                Message = Limiter(message, 2000),
                Lien = NormaliserLien(lien),
                DateCreation = DateTime.Now
            };

            dbContext.Notifications.Add(notification);
            return notification;
        }

        public async Task EnvoyerAsync(
            int utilisateurId,
            string titre,
            string message,
            string? lien = null,
            string type = TypesNotification.Information)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            Ajouter(dbContext, utilisateurId, type, titre, message, lien);
            await dbContext.SaveChangesAsync();
            Signaler(utilisateurId);
        }

        public async Task<int> EnvoyerATousAsync(string titre, string message, string? lien = null)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            List<int> utilisateurIds = await dbContext.DiscordAccounts
                .AsNoTracking()
                .Select(d => d.UtilisateurId)
                .Distinct()
                .ToListAsync();

            foreach (int utilisateurId in utilisateurIds)
                Ajouter(dbContext, utilisateurId, TypesNotification.Information, titre, message, lien);

            await dbContext.SaveChangesAsync();

            _signal.SignalerTous();

            return utilisateurIds.Count;
        }

        public async Task<List<Notification>> ObtenirAsync(int utilisateurId)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            return await dbContext.Notifications
                .AsNoTracking()
                .Where(n => n.UtilisateurId == utilisateurId)
                .OrderByDescending(n => n.DateCreation)
                .ToListAsync();
        }

        public async Task<int> CompterNonLuesAsync(int utilisateurId)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            return await dbContext.Notifications
                .AsNoTracking()
                .CountAsync(n => n.UtilisateurId == utilisateurId && n.DateLecture == null);
        }

        public async Task<bool> MarquerCommeLueAsync(int utilisateurId, int notificationId)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            int nombre = await dbContext.Notifications
                .Where(n => n.Id == notificationId && n.UtilisateurId == utilisateurId && n.DateLecture == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.DateLecture, DateTime.Now));

            if (nombre > 0)
                Signaler(utilisateurId);

            return nombre > 0;
        }

        public async Task<int> MarquerToutesCommeLuesAsync(int utilisateurId)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            int nombre = await dbContext.Notifications
                .Where(n => n.UtilisateurId == utilisateurId && n.DateLecture == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.DateLecture, DateTime.Now));

            if (nombre > 0)
                Signaler(utilisateurId);

            return nombre;
        }

        public void Signaler(int utilisateurId) => _signal.Signaler(utilisateurId);

        private static string Limiter(string valeur, int longueurMax)
        {
            string contenu = valeur.Trim();
            return contenu.Length <= longueurMax ? contenu : contenu[..longueurMax];
        }

        private static string? NormaliserLien(string? lien)
        {
            string? valeur = lien?.Trim();
            if (string.IsNullOrWhiteSpace(valeur)) return null;
            if (!valeur.StartsWith('/') || valeur.StartsWith("//", StringComparison.Ordinal)) return null;
            return Limiter(valeur, 500);
        }
    }
}
