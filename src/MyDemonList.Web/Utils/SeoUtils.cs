using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MyDemonList.Web.Utils
{
    public static class SeoUtils
    {
        public const string DescriptionSite = "Créez, partagez et explorez des demon lists Geometry Dash avec classements, niveaux, joueurs et preuves de réussite.";

        public static string CreerSlug(string valeur)
        {
            if (string.IsNullOrWhiteSpace(valeur)) return "demon-list";

            string normalisee = valeur.Normalize(NormalizationForm.FormD);
            StringBuilder slug = new StringBuilder(normalisee.Length);
            bool separateurPrecedent = false;

            foreach (char caractere in normalisee)
            {
                UnicodeCategory categorie = CharUnicodeInfo.GetUnicodeCategory(caractere);
                if (categorie == UnicodeCategory.NonSpacingMark) continue;

                char minuscule = char.ToLowerInvariant(caractere);
                if (char.IsLetterOrDigit(minuscule))
                {
                    slug.Append(minuscule);
                    separateurPrecedent = false;
                }
                else if (!separateurPrecedent && slug.Length > 0)
                {
                    slug.Append('-');
                    separateurPrecedent = true;
                }
            }

            string resultat = slug.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(resultat) ? "demon-list" : resultat;
        }

        public static string CheminListe(int id, string nom) => $"/liste/{id}/{CreerSlug(nom)}";

        public static string CheminClassement(int id, string nom) => $"{CheminListe(id, nom)}/classement";

        public static string CheminSoumission(int id, string nom) => $"{CheminListe(id, nom)}/soumettre-une-reussite";

        public static string CheminGestion(int id, string nom) => $"{CheminListe(id, nom)}/gerer";

        public static string LocaliserChemin(string chemin, string langue)
        {
            string code = langue.ToLowerInvariant();
            if (!MyDemonList.Web.Localization.Traductions.LanguesSupportees.Contains(code, StringComparer.OrdinalIgnoreCase))
                code = "en";
            if (code == "fr") return chemin;
            return $"{chemin}{(chemin.Contains('?') ? '&' : '?')}lang={Uri.EscapeDataString(code)}";
        }

        public static string LimiterDescription(string? description, string valeurParDefaut, int longueurMax = 160)
        {
            string valeur = string.IsNullOrWhiteSpace(description) ? valeurParDefaut : description.Trim();
            if (valeur.Length <= longueurMax) return valeur;

            int coupure = valeur.LastIndexOf(' ', longueurMax - 1);
            if (coupure < longueurMax / 2) coupure = longueurMax - 1;
            return $"{valeur[..coupure].TrimEnd(' ', ',', '.', ';', ':')}…";
        }

        public static string CreerJsonLdAccueil(string urlBase, string? description = null, string langue = "en")
        {
            string racine = urlBase.TrimEnd('/') + "/";
            string urlLocalisee = LocaliserChemin(racine, langue);
            Dictionary<string, object?> organisation = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["@id"] = $"{racine}#organization",
                ["name"] = "My Demon List",
                ["url"] = racine,
                ["logo"] = $"{racine}Pictures/LogoMyDemonList.png"
            };

            Dictionary<string, object?> site = new Dictionary<string, object?>
            {
                ["@type"] = "WebSite",
                ["@id"] = $"{racine}#website",
                ["name"] = "My Demon List",
                ["alternateName"] = "MDL",
                ["url"] = urlLocalisee,
                ["description"] = description ?? DescriptionSite,
                ["inLanguage"] = langue,
                ["publisher"] = new Dictionary<string, object?> { ["@id"] = $"{racine}#organization" }
            };

            Dictionary<string, object?> donnees = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@graph"] = new object[] { organisation, site }
            };

            return JsonSerializer.Serialize(donnees);
        }

        public static string CreerJsonLdItemList(
            string url,
            string nom,
            string description,
            IEnumerable<(int Position, string Nom)> elementsSource,
            IEnumerable<(string Nom, string Url)> filArianeSource)
        {
            List<Dictionary<string, object?>> elements = elementsSource
                .OrderBy(n => n.Position)
                .Select(n => new Dictionary<string, object?>
                {
                    ["@type"] = "ListItem",
                    ["position"] = n.Position,
                    ["name"] = n.Nom
                })
                .ToList();

            Dictionary<string, object?> donnees = new Dictionary<string, object?>
            {
                ["@type"] = "ItemList",
                ["name"] = nom,
                ["description"] = description,
                ["url"] = url,
                ["numberOfItems"] = elements.Count,
                ["itemListOrder"] = "https://schema.org/ItemListOrderAscending",
                ["itemListElement"] = elements
            };

            List<Dictionary<string, object?>> filAriane = filArianeSource
                .GroupBy(element => element.Url, StringComparer.OrdinalIgnoreCase)
                .Select(groupe => groupe.First())
                .Select((element, index) => new Dictionary<string, object?>
                {
                    ["@type"] = "ListItem",
                    ["position"] = index + 1,
                    ["name"] = element.Nom,
                    ["item"] = element.Url
                })
                .ToList();

            Dictionary<string, object?> fil = new Dictionary<string, object?>
            {
                ["@type"] = "BreadcrumbList",
                ["itemListElement"] = filAriane
            };

            Dictionary<string, object?> graphe = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@graph"] = new object[] { donnees, fil }
            };

            return JsonSerializer.Serialize(graphe);
        }
    }
}
