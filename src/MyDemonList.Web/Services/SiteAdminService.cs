using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities.Context;

namespace MyDemonList.Web.Services
{
    public class SiteAdminService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;

        public SiteAdminService(IServiceScopeFactory scopeFactory, IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
        }

        public async Task<bool> EstAdminOuChefDuSiteAsync(int utilisateurId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            MyDemonListWebDbContext dbContext = scope.ServiceProvider.GetRequiredService<MyDemonListWebDbContext>();

            bool estAdmin = await dbContext.AdminsSite
                .AsNoTracking()
                .AnyAsync(a => a.UtilisateurId == utilisateurId);

            if (estAdmin) return true;

            string? adminDiscordId = _configuration["ADMIN_DISCORD_ID"];
            if (string.IsNullOrWhiteSpace(adminDiscordId)) return false;

            return await dbContext.DiscordAccounts
                .AsNoTracking()
                .AnyAsync(d => d.UtilisateurId == utilisateurId && d.DiscordId == adminDiscordId);
        }

        public bool EstChefDuSite(string? discordId)
        {
            string? adminDiscordId = _configuration["ADMIN_DISCORD_ID"];
            return !string.IsNullOrWhiteSpace(adminDiscordId) && !string.IsNullOrWhiteSpace(discordId) && discordId == adminDiscordId;
        }
    }
}
