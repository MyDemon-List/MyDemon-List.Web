using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Localization;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;

namespace MyDemonList.Web.Components.Layout
{
    public partial class NavMenu : IAsyncDisposable
    {
        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        [Inject]
        private DiscordPresenceService Presence { get; set; } = default!;

        [Inject]
        private IServiceScopeFactory ScopeFactory { get; set; } = default!;

        [Inject]
        private ListeSessionService ListeSession { get; set; } = default!;

        [Inject]
        private SiteAdminService SiteAdminService { get; set; } = default!;

        [Inject]
        private NotificationService NotificationService { get; set; } = default!;

        [Inject]
        private NotificationSignalService NotificationSignal { get; set; } = default!;

        [Inject]
        private ProfilUtilisateurSignalService ProfilSignal { get; set; } = default!;

        [Inject]
        private IJSRuntime JsRuntime { get; set; } = default!;

        [CascadingParameter]
        private Task<AuthenticationState>? AuthStateTask { get; set; }

        private string? _displayName;
        private string? _codePays;
        private bool _peutGererListe;
        private bool _peutAccederAdmin;
        private int? _utilisateurId;
        private int _notificationsNonLues;
        private List<Notification> _notifications = [];
        private bool _panneauNotificationsOuvert;
        private bool _chargementNotifications;

        private enum FiltreNotifications { NonLues, Lues }

        private FiltreNotifications _filtreNotifications = FiltreNotifications.NonLues;

        private IEnumerable<Notification> NotificationsAffichees => _filtreNotifications switch
        {
            FiltreNotifications.Lues => _notifications.Where(n => n.DateLecture is not null),
            _ => _notifications.Where(n => n.DateLecture is null)
        };

        private string _currentUri = "/";
        private string _hoveredItem = "/liste";
        private string? _discordId;
        private ulong _listenId;
        private bool _isHovering;
        private string _statusClass = "etat--offline";
        private CancellationTokenSource? _warmupCts;
        private IJSObjectReference? _jsModule;
        private bool _subscribed;
        private bool _notificationsSubscribed;
        private bool _rechargementCultureEnCours;
        private bool _initialisationTerminee;
        private bool _componentDetruit;
        private string? _urlRechargementCulture;

        protected override void OnInitialized()
        {
            _currentUri = NavigationManager.Uri;
            _hoveredItem = new Uri(_currentUri).AbsolutePath;
            NavigationManager.LocationChanged += HandleLocationChanged;
            ListeSession.OnChanged += OnListeSessionChanged;
            ProfilSignal.DrapeauModifie += OnDrapeauModifie;
        }

        private void OnDrapeauModifie(int utilisateurId, string? codePays)
        {
            if (_utilisateurId != utilisateurId || _componentDetruit) return;

            _codePays = codePays;
            _ = InvokeAsync(StateHasChanged);
        }

        private void HandleLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            _currentUri = e.Location;
            _panneauNotificationsOuvert = false;
            string? langueDemandee = ObtenirLangueDemandee(e.Location);
            if (!_rechargementCultureEnCours && langueDemandee is not null && langueDemandee != Texte.CodeLangue)
            {
                if (!_initialisationTerminee)
                {
                    _urlRechargementCulture = e.Location;
                    return;
                }

                RechargerPourCulture(e.Location);
                return;
            }

            if (!_isHovering) _hoveredItem = new Uri(_currentUri).AbsolutePath;
            StateHasChanged();
        }

        private static string? ObtenirLangueDemandee(string url)
        {
            string? langue = Microsoft.AspNetCore.WebUtilities.QueryHelpers
                .ParseQuery(new Uri(url).Query)
                .GetValueOrDefault("lang")
                .FirstOrDefault()?
                .Trim()
                .ToLowerInvariant();

            return langue is not null && Traductions.LanguesSupportees.Contains(langue, StringComparer.OrdinalIgnoreCase)
                ? langue
                : null;
        }

        protected override async Task OnInitializedAsync()
        {
            if (AuthStateTask is not null)
            {
                AuthenticationState authState = await AuthStateTask;
                _discordId = authState.User.FindFirst("discord:id")?.Value;

                string? fallbackName = authState.User.FindFirst("discord:global_name")?.Value
                    ?? authState.User.Identity?.Name;

                if (!string.IsNullOrEmpty(_discordId))
                {
                    using IServiceScope scope = ScopeFactory.CreateScope();
                    MyDemonListWebDbContext dbContext = scope.ServiceProvider.GetRequiredService<MyDemonListWebDbContext>();
                    DiscordAccount? compte = await dbContext.DiscordAccounts
                        .Include(a => a.Utilisateur)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(a => a.DiscordId == _discordId);

                    if (_componentDetruit) return;

                    _displayName = compte?.Utilisateur?.Nom ?? fallbackName;
                    _utilisateurId = compte?.Utilisateur?.Id;
                    _codePays = compte?.Utilisateur?.CodePays;

                    if (_utilisateurId is int utilisateurId)
                    {
                        NotificationSignal.NotificationsModifiees += OnNotificationsModifiees;
                        NotificationSignal.NotificationsGlobalesModifiees += OnNotificationsGlobalesModifiees;
                        _notificationsSubscribed = true;
                        _notificationsNonLues = await NotificationService.CompterNonLuesAsync(utilisateurId);
                    }

                    await CheckIfUserIsCreatorAsync();

                    if (_componentDetruit) return;

                    _peutAccederAdmin = _utilisateurId is int uidAdmin && await SiteAdminService.EstAdminOuChefDuSiteAsync(uidAdmin);

                    if (_componentDetruit) return;
                }
                else
                {
                    _displayName = fallbackName;
                }
            }

            if (ulong.TryParse(_discordId, out _listenId))
            {
                if (!_subscribed)
                {
                    Presence.StatusChanged += OnPresenceChanged;
                    _subscribed = true;
                }

                if (Presence.TryGetCachedStatus(_listenId, out string cached))
                    ApplyStatus(cached);

                _warmupCts = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    DateTime end = DateTime.UtcNow.AddSeconds(20);
                    while (DateTime.UtcNow < end && !_warmupCts.IsCancellationRequested)
                    {
                        string s = await Presence.GetStatusAsync(_listenId, _warmupCts.Token);
                        await InvokeAsync(() => { ApplyStatus(s); StateHasChanged(); });
                        if (s != "offline") break;
                        await Task.Delay(2000, _warmupCts.Token);
                    }
                }, _warmupCts.Token);
            }

            _initialisationTerminee = true;
            if (!_componentDetruit && _urlRechargementCulture is not null)
                RechargerPourCulture(_urlRechargementCulture);
        }

        private void RechargerPourCulture(string url)
        {
            if (_rechargementCultureEnCours || _componentDetruit) return;

            _rechargementCultureEnCours = true;
            NavigationManager.NavigateTo(url, forceLoad: true, replace: true);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender)
                return;

            try
            {
                _jsModule = await JsRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./Components/Layout/NavMenu.razor.js");
                await _jsModule.InvokeVoidAsync("initialiserNavMenu");
            }
            catch (JSDisconnectedException)
            {
            }
        }

        private void OnPresenceChanged(ulong userId, string s)
        {
            if (userId != _listenId) return;
            _ = InvokeAsync(() =>
            {
                ApplyStatus(s);
                StateHasChanged();
            });
        }

        private void ApplyStatus(string s)
        {
            _statusClass = s switch
            {
                "online" => "etat--online",
                "idle" => "etat--idle",
                "dnd" => "etat--dnd",
                _ => "etat--offline"
            };
        }

        private async void OnListeSessionChanged()
        {
            if (_componentDetruit) return;

            await CheckIfUserIsCreatorAsync();
            if (_componentDetruit) return;

            StateHasChanged();
        }

        private void OnNotificationsModifiees(int utilisateurId)
        {
            if (_componentDetruit || _utilisateurId != utilisateurId) return;

            _ = InvokeAsync(async () =>
            {
                _notificationsNonLues = await NotificationService.CompterNonLuesAsync(utilisateurId);
                if (_panneauNotificationsOuvert)
                    await ChargerNotificationsAsync();
                if (!_componentDetruit)
                    StateHasChanged();
            });
        }

        private void OnNotificationsGlobalesModifiees()
        {
            if (_componentDetruit || _utilisateurId is not int utilisateurId) return;
            OnNotificationsModifiees(utilisateurId);
        }

        private async Task BasculerPanneauNotificationsAsync()
        {
            if (_panneauNotificationsOuvert)
            {
                _panneauNotificationsOuvert = false;
                return;
            }

            _panneauNotificationsOuvert = true;
            _chargementNotifications = true;
            StateHasChanged();
            await ChargerNotificationsAsync();
        }

        private void FermerPanneauNotifications() => _panneauNotificationsOuvert = false;

        private async Task ChargerNotificationsAsync()
        {
            if (_utilisateurId is not int utilisateurId) return;

            List<Notification> notifications = await NotificationService.ObtenirAsync(utilisateurId);
            if (_componentDetruit) return;

            _notifications = notifications;
            _notificationsNonLues = notifications.Count(n => n.DateLecture is null);
            _chargementNotifications = false;
        }

        private async Task MarquerNotificationCommeLueAsync(Notification notification)
        {
            if (_utilisateurId is not int utilisateurId || notification.DateLecture is not null) return;

            if (await NotificationService.MarquerCommeLueAsync(utilisateurId, notification.Id))
            {
                notification.DateLecture = DateTime.Now;
                _notificationsNonLues = Math.Max(0, _notificationsNonLues - 1);
            }
        }

        private async Task MarquerToutesNotificationsCommeLuesAsync()
        {
            if (_utilisateurId is not int utilisateurId) return;
            if (await NotificationService.MarquerToutesCommeLuesAsync(utilisateurId) == 0) return;

            DateTime dateLecture = DateTime.Now;
            foreach (Notification notification in _notifications.Where(n => n.DateLecture is null))
                notification.DateLecture = dateLecture;

            _notificationsNonLues = 0;
        }

        private async Task OuvrirNotificationAsync(Notification notification)
        {
            if (string.IsNullOrWhiteSpace(notification.Lien)) return;

            await MarquerNotificationCommeLueAsync(notification);
            _panneauNotificationsOuvert = false;
            NavigationManager.NavigateTo(SeoUtils.LocaliserChemin(notification.Lien, Texte.CodeLangue));
        }

        private string ClasseFiltreNotifications(FiltreNotifications filtre) =>
            _filtreNotifications == filtre ? "filtre-notifications-nav actif" : "filtre-notifications-nav";

        private static string ClasseTypeNotification(string type) => type switch
        {
            TypesNotification.SoumissionAcceptee or TypesNotification.QuotaAccepte or TypesNotification.FusionAcceptee => "type-succes",
            TypesNotification.SoumissionRefusee or TypesNotification.QuotaRefuse or TypesNotification.FusionRefusee => "type-refus",
            TypesNotification.RoleModifie => "type-role",
            _ => "type-information"
        };

        private static string SymboleTypeNotification(string type) => type switch
        {
            TypesNotification.SoumissionAcceptee or TypesNotification.QuotaAccepte or TypesNotification.FusionAcceptee => "✓",
            TypesNotification.SoumissionRefusee or TypesNotification.QuotaRefuse or TypesNotification.FusionRefusee => "×",
            TypesNotification.RoleModifie => "◆",
            _ => "i"
        };

        private static string FormaterDateNotification(DateTime date) =>
            date.ToString("g", System.Globalization.CultureInfo.CurrentCulture);

        private async Task CheckIfUserIsCreatorAsync()
        {
            if (_componentDetruit) return;

            if (ListeSession.ListeId is not int listeId || _utilisateurId is null)
            {
                _peutGererListe = false;
                return;
            }

            using IServiceScope scope = ScopeFactory.CreateScope();
            MyDemonListWebDbContext dbContext = scope.ServiceProvider.GetRequiredService<MyDemonListWebDbContext>();

            bool estProprietaire = await dbContext.Listes
                .AsNoTracking()
                .AnyAsync(l => l.Id == listeId && l.UtilisateurId == _utilisateurId);

            _peutGererListe = estProprietaire || await dbContext.MembresListe
                .AsNoTracking()
                .AnyAsync(m => m.ListeId == listeId && m.UtilisateurId == _utilisateurId);
        }

        private void SetHoveredItem(string href)
        {
            _isHovering = true;
            _hoveredItem = href;
            StateHasChanged();
        }

        private void HandleNavMouseLeave()
        {
            _isHovering = false;
            _hoveredItem = new Uri(_currentUri).AbsolutePath;
            StateHasChanged();
        }

        private bool ShouldShowIndicator()
        {
            string path = NormaliserCheminNavigation(_hoveredItem ?? new Uri(_currentUri).AbsolutePath);
            return path switch
            {
                "/" => true,
                "/liste/gerer" => _peutGererListe,
                "/liste" or "/classement" or "/soumettre-une-reussite" => ListeSession.ListeId is not null,
                _ => false
            };
        }

        private string GetIndicatorClass()
        {
            return ShouldShowIndicator() ? "indicator indicator--visible" : "indicator indicator--hidden";
        }

        private string GetIndicatorKey() => ShouldShowIndicator()
            ? NormaliserCheminNavigation(_hoveredItem ?? new Uri(_currentUri).AbsolutePath)
            : string.Empty;

        private string ObtenirCheminListe() => ListeSession.ListeId is int id
            ? SeoUtils.CheminListe(id, ListeSession.ListeNom ?? "demon-list")
            : "/liste";

        private string ObtenirCheminGestion() => ListeSession.ListeId is int id
            ? SeoUtils.CheminGestion(id, ListeSession.ListeNom ?? "demon-list")
            : "/liste/gerer";

        private string ObtenirCheminClassement() => ListeSession.ListeId is int id
            ? SeoUtils.CheminClassement(id, ListeSession.ListeNom ?? "demon-list")
            : "/classement";

        private string ObtenirCheminSoumission() => ListeSession.ListeId is int id
            ? SeoUtils.CheminSoumission(id, ListeSession.ListeNom ?? "demon-list")
            : "/soumettre-une-reussite";

        private static string NormaliserCheminNavigation(string chemin)
        {
            string valeur = chemin.TrimEnd('/');
            if (valeur.EndsWith("/soumettre-une-reussite", StringComparison.OrdinalIgnoreCase)) return "/soumettre-une-reussite";
            if (valeur.EndsWith("/classement", StringComparison.OrdinalIgnoreCase)) return "/classement";
            if (valeur.EndsWith("/gerer", StringComparison.OrdinalIgnoreCase)) return "/liste/gerer";
            if (valeur.StartsWith("/liste/", StringComparison.OrdinalIgnoreCase)) return "/liste";
            return string.IsNullOrEmpty(valeur) ? "/" : valeur;
        }

        public async ValueTask DisposeAsync()
        {
            _componentDetruit = true;
            NavigationManager.LocationChanged -= HandleLocationChanged;
            ProfilSignal.DrapeauModifie -= OnDrapeauModifie;
            if (_subscribed) Presence.StatusChanged -= OnPresenceChanged;
            if (_notificationsSubscribed)
            {
                NotificationSignal.NotificationsModifiees -= OnNotificationsModifiees;
                NotificationSignal.NotificationsGlobalesModifiees -= OnNotificationsGlobalesModifiees;
            }
            ListeSession.OnChanged -= OnListeSessionChanged;
            _warmupCts?.Cancel();
            _warmupCts?.Dispose();

            if (_jsModule is not null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("detruireNavMenu");
                    await _jsModule.DisposeAsync();
                }
                catch (JSDisconnectedException)
                {
                }
            }
        }
    }
}
