using DotNetEnv;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using MyDemonList.Web.Auth;
using MyDemonList.Web.Components;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Localization;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;
using System.Globalization;
using System.Xml.Linq;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

Env.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

string dbHost = builder.Configuration["DB_HOST"] ?? "localhost";
string dbPort = builder.Configuration["DB_PORT"] ?? "21555";
string dbUser = builder.Configuration["DB_USERNAME"] ?? "postgres";
string dbPass = builder.Configuration["DB_PASSWORD"] ?? "password";
string dbName = builder.Configuration["DB_NAME"] ?? "database";

string connectionString = builder.Environment.IsDevelopment()
    ? "Host=localhost;Port=21555;Username=postgres;Password=password;Database=database;Include Error Detail=true"
    : $"Host={dbHost};Port={dbPort};Username={dbUser};Password={dbPass};Database={dbName}";

builder.Services.AddDbContext<MyDemonListWebDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddMemoryCache();
builder.Services.AddScoped<NiveauService>();
builder.Services.AddScoped<FusionService>();
builder.Services.AddScoped<SiteAdminService>();
builder.Services.AddScoped<QuotaService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<NotificationSignalService>();
builder.Services.AddScoped<ProfilUtilisateurSignalService>();
builder.Services.AddScoped<ListeSessionService>();
builder.Services.AddScoped<Chargement>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<Traductions>();
builder.Services.AddHttpClient<GdBrowserService>(client =>
{
    client.BaseAddress = new Uri("https://gdbrowser.com/api/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<LevelThumbnailService>(client =>
{
    client.BaseAddress = new Uri("https://levelthumbs.prevter.me/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    string[] culturesConfigurees = builder.Configuration
        .GetSection("Localization:SupportedCultures")
        .Get<string[]>() ?? Traductions.LanguesSupportees;
    string[] codesCultures = culturesConfigurees
        .Append("en")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    string cultureConfiguree = builder.Configuration["Localization:DefaultCulture"] ?? "en";
    string cultureParDefaut = codesCultures.Contains(cultureConfiguree, StringComparer.OrdinalIgnoreCase)
        ? cultureConfiguree
        : "en";
    CultureInfo[] cultures = codesCultures
        .Select(CultureInfo.GetCultureInfo)
        .ToArray();

    options.DefaultRequestCulture = new RequestCulture(cultureParDefaut);
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
    options.ApplyCurrentCultureToResponseHeaders = true;
    options.RequestCultureProviders =
    [
        new CustomRequestCultureProvider(httpContext =>
        {
            if (!httpContext.Request.Query.ContainsKey("lang"))
                return Task.FromResult<ProviderCultureResult?>(null);

            string? langueDemandee = httpContext.Request.Query["lang"].FirstOrDefault()?.Trim().ToLowerInvariant();
            string langue = langueDemandee is not null
                && Traductions.LanguesSupportees.Contains(langueDemandee, StringComparer.OrdinalIgnoreCase)
                    ? langueDemandee
                    : "en";

            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(langue));
        }),
        new CustomRequestCultureProvider(httpContext =>
        {
            string? langue = SeoUtils.ObtenirLangueDuChemin(httpContext.Request.Path.Value ?? "/");
            return Task.FromResult<ProviderCultureResult?>(langue is null ? null : new ProviderCultureResult(langue));
        }),
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    ];
});
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddDiscordAuthentication(builder.Configuration);
builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo("/var/mydemonlist/keys"))
                .SetApplicationName("MyDemonList.Web");
builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o =>
{
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.SameSite = SameSiteMode.Lax;
});

WebApplication app = builder.Build();

ForwardedHeadersOptions fwd = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
fwd.KnownIPNetworks.Clear();
fwd.KnownProxies.Clear();
app.UseForwardedHeaders(fwd);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAuthentication();
app.UseRequestLocalization();
app.Use(async (httpContext, suivant) =>
{
    string chemin = httpContext.Request.Path.Value ?? "/";
    string cheminSansLangue = SeoUtils.RetirerPrefixeLangue(chemin);
    bool estAliasProfil = cheminSansLangue.Equals("/profile", StringComparison.OrdinalIgnoreCase);
    if (estAliasProfil) cheminSansLangue = "/profil";
    bool estPageLocalisable = cheminSansLangue == "/"
        || cheminSansLangue.Equals("/classement", StringComparison.OrdinalIgnoreCase)
        || cheminSansLangue.Equals("/soumettre-une-reussite", StringComparison.OrdinalIgnoreCase)
        || cheminSansLangue.Equals("/profil", StringComparison.OrdinalIgnoreCase)
        || cheminSansLangue.Equals("/profile", StringComparison.OrdinalIgnoreCase)
        || cheminSansLangue.Equals("/admin", StringComparison.OrdinalIgnoreCase)
        || cheminSansLangue.Equals("/404", StringComparison.OrdinalIgnoreCase)
        || cheminSansLangue.Equals("/Error", StringComparison.OrdinalIgnoreCase)
        || cheminSansLangue.Equals("/liste", StringComparison.OrdinalIgnoreCase)
        || cheminSansLangue.StartsWith("/liste/", StringComparison.OrdinalIgnoreCase);

    if ((HttpMethods.IsGet(httpContext.Request.Method) || HttpMethods.IsHead(httpContext.Request.Method)) && estPageLocalisable)
    {
        bool parametreLanguePresent = httpContext.Request.Query.ContainsKey("lang");
        string? langueDemandee = httpContext.Request.Query["lang"].FirstOrDefault()?.Trim().ToLowerInvariant();
        string? langueDuChemin = SeoUtils.ObtenirLangueDuChemin(chemin);
        string? languePreferee = null;
        string? discordId = httpContext.User.FindFirst("discord:id")?.Value;
        if (!string.IsNullOrWhiteSpace(discordId))
        {
            MyDemonListWebDbContext dbContext = httpContext.RequestServices.GetRequiredService<MyDemonListWebDbContext>();
            languePreferee = await dbContext.DiscordAccounts
                .AsNoTracking()
                .Where(compte => compte.DiscordId == discordId)
                .Select(compte => compte.Utilisateur.LanguePreferee)
                .SingleOrDefaultAsync(httpContext.RequestAborted);
            languePreferee = languePreferee?.ToLowerInvariant();

            if (languePreferee is not null
                && !Traductions.LanguesSupportees.Contains(languePreferee, StringComparer.OrdinalIgnoreCase))
                languePreferee = null;
        }

        string? langueDuNavigateur = httpContext.Request.GetTypedHeaders().AcceptLanguage?
            .Where(valeur => (valeur.Quality ?? 1) > 0)
            .OrderByDescending(valeur => valeur.Quality ?? 1)
            .Select(valeur => valeur.Value.Value?.Split('-', 2)[0].ToLowerInvariant())
            .FirstOrDefault(code => code is not null
                && Traductions.LanguesSupportees.Contains(code, StringComparer.OrdinalIgnoreCase));
        string langueEffective = languePreferee ?? (parametreLanguePresent
            ? langueDemandee is not null && Traductions.LanguesSupportees.Contains(langueDemandee, StringComparer.OrdinalIgnoreCase)
                ? langueDemandee
                : "en"
            : langueDuChemin ?? langueDuNavigateur ?? "en");

        if (!Traductions.LanguesSupportees.Contains(langueEffective, StringComparer.OrdinalIgnoreCase))
            langueEffective = "en";

        if (languePreferee is not null)
        {
            httpContext.Response.Cookies.Append(
                "mdl_langue_initialisee",
                "1",
                new CookieOptions
                {
                    IsEssential = true,
                    HttpOnly = false,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromDays(365),
                    Path = "/"
                });
        }

        string valeurCookieCulture = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(langueEffective));
        if (!httpContext.Request.Cookies.TryGetValue(CookieRequestCultureProvider.DefaultCookieName, out string? valeurCookieActuelle)
            || !valeurCookieActuelle.Equals(valeurCookieCulture, StringComparison.Ordinal))
        {
            httpContext.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                valeurCookieCulture,
                new CookieOptions
                {
                    IsEssential = true,
                    HttpOnly = true,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });
        }

        bool racineLangueSansBarre = langueDuChemin is not null
            && chemin.Equals($"/{langueDuChemin}", StringComparison.OrdinalIgnoreCase);

        if (estAliasProfil || parametreLanguePresent || langueDuChemin is null || racineLangueSansBarre || !langueDuChemin.Equals(langueEffective, StringComparison.OrdinalIgnoreCase))
        {
            List<KeyValuePair<string, string?>> parametres = [];
            foreach ((string cle, Microsoft.Extensions.Primitives.StringValues valeurs) in httpContext.Request.Query)
            {
                if (cle.Equals("lang", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string? valeur in valeurs)
                    parametres.Add(new KeyValuePair<string, string?>(cle, valeur));
            }

            string destination = SeoUtils.LocaliserChemin(cheminSansLangue, langueEffective)
                + QueryString.Create(parametres);
            bool redirectionPreference = languePreferee is not null;
            bool redirectionAutomatique = redirectionPreference || langueDuChemin is null && !parametreLanguePresent;
            if (redirectionAutomatique)
            {
                httpContext.Response.Headers.Vary = redirectionPreference ? "Cookie" : "Accept-Language";
                if (redirectionPreference)
                    httpContext.Response.Headers.CacheControl = "private, no-store";
            }

            httpContext.Response.Redirect(destination, permanent: !redirectionAutomatique);
            return;
        }
    }

    await suivant();
});

app.UseHttpsRedirection();
app.UseStatusCodePagesWithReExecute("/404");

#if DEBUG
string imagesRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "PicturesDev");
#else
    string imagesRoot = "/var/mydemonlist/images";
#endif

if (!Directory.Exists(Path.Combine(imagesRoot, "MiniaturesNiveaux")))
{
    Directory.CreateDirectory(Path.Combine(imagesRoot, "MiniaturesNiveaux"));
}

if (!Directory.Exists(Path.Combine(imagesRoot, "BackgroundsListes")))
{
    Directory.CreateDirectory(Path.Combine(imagesRoot, "BackgroundsListes"));
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
    Path.Combine(imagesRoot, "MiniaturesNiveaux")),
    RequestPath = "/MiniaturesNiveaux",
    OnPrepareResponse = contexte =>
        contexte.Context.Response.Headers.CacheControl = "public, max-age=3600"
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
    Path.Combine(imagesRoot, "BackgroundsListes")),
    RequestPath = "/BackgroundsListes",
    OnPrepareResponse = contexte =>
        contexte.Context.Response.Headers.CacheControl = "public, max-age=3600"
});

app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapGet("/Pictures/DemonsFaces/{id:int}", (int id, IWebHostEnvironment environment) =>
{
    if (id <= 0)
        return (IResult)Results.NotFound();

    string dossierImages = Path.Combine(environment.WebRootPath, "Pictures", "DemonsFaces");
    string? cheminImage = new[] { ".gif", ".png", ".GIF", ".PNG" }
        .Select(extension => Path.Combine(dossierImages, $"{id}{extension}"))
        .FirstOrDefault(File.Exists);

    if (cheminImage is null)
        return Results.NotFound();

    string typeContenu = string.Equals(Path.GetExtension(cheminImage), ".gif", StringComparison.OrdinalIgnoreCase)
        ? "image/gif"
        : "image/png";

    return Results.File(cheminImage, typeContenu);
});

app.MapGet("/robots.txt", (IConfiguration configuration, HttpContext httpContext) =>
{
    string urlBase = (configuration["Seo:BaseUrl"] ?? "https://mydemonlist.com").TrimEnd('/');
    string contenu = $"User-agent: *\nAllow: /\nDisallow: /login/\nDisallow: /logout\nDisallow: /signin-discord\nSitemap: {urlBase}/sitemap.xml\n";
    httpContext.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Text(contenu, "text/plain");
});

app.MapGet("/sitemap.xml", async (MyDemonListWebDbContext dbContext, IConfiguration configuration, HttpContext httpContext, IMemoryCache cache) =>
{
    string urlBase = (configuration["Seo:BaseUrl"] ?? "https://mydemonlist.com").TrimEnd('/');
    string cleCache = $"seo:sitemap:{urlBase}";
    string contenu = await cache.GetOrCreateAsync(cleCache, async entree =>
    {
        entree.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);

        List<Liste> listes = await dbContext.Listes
            .AsNoTracking()
            .Where(l => l.EstPublique && dbContext.Niveaux.Any(n => n.ListeId == l.Id))
            .OrderBy(l => l.Id)
            .ToListAsync(httpContext.RequestAborted);

        List<int> ids = listes.Select(l => l.Id).ToList();
        Dictionary<int, DateTime> dernieresModifications = await dbContext.Niveaux
            .AsNoTracking()
            .Where(n => ids.Contains(n.ListeId))
            .GroupBy(n => n.ListeId)
            .Select(g => new { ListeId = g.Key, Date = g.Max(n => n.DateAjout) })
            .ToDictionaryAsync(x => x.ListeId, x => x.Date, httpContext.RequestAborted);

        XNamespace espace = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";

        string ObtenirUrlLangue(string chemin, string langue)
        {
            return $"{urlBase}{SeoUtils.LocaliserChemin(chemin, langue)}";
        }

        IEnumerable<XElement> CreerEntreesLocalisees(string chemin, string? date = null)
        {
            foreach (string langueCourante in Traductions.LanguesSupportees)
            {
                XElement entree = new XElement(espace + "url",
                    new XElement(espace + "loc", ObtenirUrlLangue(chemin, langueCourante)));

                foreach (string langueAlternative in Traductions.LanguesSupportees)
                {
                    entree.Add(new XElement(xhtml + "link",
                        new XAttribute("rel", "alternate"),
                        new XAttribute("hreflang", langueAlternative),
                        new XAttribute("href", ObtenirUrlLangue(chemin, langueAlternative))));
                }

                entree.Add(new XElement(xhtml + "link",
                    new XAttribute("rel", "alternate"),
                    new XAttribute("hreflang", "x-default"),
                    new XAttribute("href", ObtenirUrlLangue(chemin, "en"))));

                if (date is not null)
                    entree.Add(new XElement(espace + "lastmod", date));

                yield return entree;
            }
        }

        XElement ensemble = new XElement(espace + "urlset",
            new XAttribute(XNamespace.Xmlns + "xhtml", xhtml),
            CreerEntreesLocalisees("/"));

        foreach (Liste liste in listes)
        {
            string cheminListe = SeoUtils.CheminListe(liste.Id, liste.Nom);
            string cheminClassement = SeoUtils.CheminClassement(liste.Id, liste.Nom);
            DateTime modification = dernieresModifications.GetValueOrDefault(liste.Id, liste.DateCreation);
            string date = modification.ToUniversalTime().ToString("yyyy-MM-dd");

            ensemble.Add(CreerEntreesLocalisees(cheminListe, date));
            ensemble.Add(CreerEntreesLocalisees(cheminClassement, date));
        }

        XDocument document = new XDocument(new XDeclaration("1.0", "utf-8", null), ensemble);
        return document.ToString(SaveOptions.DisableFormatting);
    }) ?? string.Empty;

    httpContext.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Text(contenu, "application/xml");
});

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapDiscordAuthEndpoints();

app.Run();
