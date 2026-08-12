using System.Globalization;

namespace MyDemonList.Web.Utils;

public static class PaysUtils
{
    public sealed record Pays(string Code, string Nom, string UrlDrapeau);

    private static readonly IReadOnlyDictionary<string, string> PaysParLangue =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ar"] = "SA",
            ["de"] = "DE",
            ["en"] = "US",
            ["es"] = "ES",
            ["fr"] = "FR",
            ["hi"] = "IN",
            ["id"] = "ID",
            ["it"] = "IT",
            ["ja"] = "JP",
            ["ko"] = "KR",
            ["nl"] = "NL",
            ["pl"] = "PL",
            ["pt"] = "BR",
            ["ro"] = "RO",
            ["ru"] = "RU",
            ["sv"] = "SE",
            ["tr"] = "TR",
            ["uk"] = "UA",
            ["vi"] = "VN",
            ["zh"] = "CN"
        };

    private static readonly string[] CodesPays = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(ObtenirCodeRegion)
        .Where(code => code is not null)
        .Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static readonly HashSet<string> CodesPaysValides = new(CodesPays, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<Pays> ObtenirPays()
    {
        return CodesPays
            .Select(code =>
            {
                RegionInfo region = new(code);
                return new Pays(code, region.DisplayName, ObtenirUrlDrapeau(code)!);
            })
            .OrderBy(pays => pays.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string? NormaliserCode(string? code)
    {
        string? normalise = code?.Trim().ToUpperInvariant();
        return normalise is not null && CodesPaysValides.Contains(normalise) ? normalise : null;
    }

    public static string? ObtenirUrlDrapeau(string? code) =>
        NormaliserCode(code) is string normalise ? $"/flags/{normalise.ToLowerInvariant()}.svg" : null;

    public static string? ObtenirNom(string? code)
    {
        string? normalise = NormaliserCode(code);
        return normalise is null ? null : new RegionInfo(normalise).DisplayName;
    }

    public static string? DevinerCodePays(string? languesAcceptees)
    {
        if (string.IsNullOrWhiteSpace(languesAcceptees)) return null;

        foreach (string valeur in languesAcceptees.Split(','))
        {
            string etiquette = valeur.Split(';', 2)[0].Trim().Replace('_', '-');
            if (string.IsNullOrWhiteSpace(etiquette) || etiquette == "*") continue;

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(etiquette);
                string? codeRegion = ObtenirCodeRegion(culture);
                if (codeRegion is not null) return codeRegion;

                if (PaysParLangue.TryGetValue(culture.TwoLetterISOLanguageName, out string? code))
                    return code;
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return null;
    }

    private static string? ObtenirCodeRegion(CultureInfo culture)
    {
        try
        {
            string code = new RegionInfo(culture.Name).TwoLetterISORegionName.ToUpperInvariant();
            return code.Length == 2 && code.All(char.IsLetter) ? code : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
