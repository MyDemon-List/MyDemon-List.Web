using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;
using System.Security.Claims;

namespace MyDemonList.Web.Components.Pages
{
    public partial class AdminPage : ComponentBase
    {
        [Inject]
        private DbContextOptions<MyDemonListWebDbContext> DbContextOptions { get; set; } = default!;

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        [Inject]
        private AuthenticationStateProvider AuthProvider { get; set; } = default!;

        [Inject]
        private SiteAdminService SiteAdminService { get; set; } = default!;

        [Inject]
        private QuotaService QuotaService { get; set; } = default!;

        [Inject]
        private FusionService FusionService { get; set; } = default!;

        [Inject]
        private ListeSessionService ListeSession { get; set; } = default!;

        [Inject]
        private NotificationService NotificationService { get; set; } = default!;

        private enum Onglet { Fusions, Quotas, Admins, FusionForcee, Notifications }
        private Onglet _ongletActuel = Onglet.Fusions;

        private bool _isLoading = true;
        private bool _estAutorise;
        private bool _estChef;
        private int _utilisateurId;

        protected override async Task OnInitializedAsync()
        {
            AuthenticationState authState = await AuthProvider.GetAuthenticationStateAsync();
            ClaimsPrincipal user = authState.User;

            if (user.Identity?.IsAuthenticated != true)
            {
                NavigationManager.NavigateTo(SeoUtils.LocaliserChemin("/", Texte.CodeLangue));
                return;
            }

            string? discordId = user.FindFirst("discord:id")?.Value;
            _estChef = SiteAdminService.EstChefDuSite(discordId);

            using (MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions))
            {
                DiscordAccount? compte = string.IsNullOrWhiteSpace(discordId)
                    ? null
                    : await dbContext.DiscordAccounts
                        .Include(a => a.Utilisateur)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(a => a.DiscordId == discordId);

                if (compte?.Utilisateur is null)
                {
                    NavigationManager.NavigateTo(SeoUtils.LocaliserChemin("/", Texte.CodeLangue));
                    return;
                }

                _utilisateurId = compte.Utilisateur.Id;
            }

            _estAutorise = _estChef || await SiteAdminService.EstAdminOuChefDuSiteAsync(_utilisateurId);

            if (!_estAutorise)
            {
                NavigationManager.NavigateTo(SeoUtils.LocaliserChemin("/", Texte.CodeLangue));
                return;
            }

            await ChargerFusions();
            await ChargerDemandesQuota();
            if (_estChef)
                await ChargerAdmins();

            _isLoading = false;
        }
    }
}
