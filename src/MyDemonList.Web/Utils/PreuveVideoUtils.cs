using MyDemonList.Web.Entities;

namespace MyDemonList.Web.Utils
{
    public static class PreuveVideoUtils
    {
        private static readonly string[] OrdreDifficultes =
        [
            "Auto",
            "Easy",
            "Normal",
            "Hard",
            "Harder",
            "Insane",
            "Easy Demon",
            "Medium Demon",
            "Hard Demon",
            "Insane Demon",
            "Extreme Demon"
        ];

        private static readonly string[] Suffixes =
        [
            " Featured",
            " Epic",
            " Legendary",
            " Mythic"
        ];

        public static bool EstRequise(
            Liste liste,
            Niveau niveau,
            Classement? classement,
            IReadOnlyCollection<Difficulte> difficultes)
        {
            if (liste.VideoToujoursRequise)
                return true;

            bool critereConfigure = false;
            bool seuilAtteint = false;

            if (liste.VideoDifficulteMinimaleId is int difficulteMinimaleId)
            {
                critereConfigure = true;
                Difficulte? difficulteMinimale = difficultes.FirstOrDefault(d => d.Id == difficulteMinimaleId);
                Difficulte? difficulteNiveau = niveau.Rating ?? difficultes.FirstOrDefault(d => d.Id == niveau.RatingId);

                int rangMinimum = ObtenirRang(difficulteMinimale?.Nom);
                int rangNiveau = ObtenirRang(difficulteNiveau?.Nom);
                if (rangMinimum < 0 || rangNiveau < 0)
                    return true;

                seuilAtteint = rangNiveau >= rangMinimum;
            }

            if (liste.VideoTopStart is int topStart)
            {
                critereConfigure = true;
                if (topStart <= 0 || classement is null)
                    return true;

                seuilAtteint |= classement.ClassementPosition <= topStart;
            }

            return !critereConfigure || seuilAtteint;
        }

        public static string ObtenirNomPrincipal(string nom)
        {
            string? suffixe = Suffixes.FirstOrDefault(s => nom.EndsWith(s, StringComparison.OrdinalIgnoreCase));
            return suffixe is null ? nom : nom[..^suffixe.Length];
        }

        public static bool EstDifficultePrincipaleSelectable(Difficulte difficulte) =>
            !difficulte.Nom.Equals("N/A", StringComparison.OrdinalIgnoreCase) &&
            difficulte.Nom.Equals(ObtenirNomPrincipal(difficulte.Nom), StringComparison.OrdinalIgnoreCase) &&
            ObtenirRang(difficulte.Nom) >= 0;

        private static int ObtenirRang(string? nom)
        {
            if (string.IsNullOrWhiteSpace(nom))
                return -1;

            string nomPrincipal = ObtenirNomPrincipal(nom);
            return Array.FindIndex(OrdreDifficultes, d => d.Equals(nomPrincipal, StringComparison.OrdinalIgnoreCase));
        }
    }
}
