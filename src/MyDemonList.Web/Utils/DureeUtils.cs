namespace MyDemonList.Web.Utils
{
    public static class DureeUtils
    {
        public static string Formater(TimeSpan duree)
        {
            string langue = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (duree.TotalDays >= 1)
                return FormaterUnite(Math.Ceiling(duree.TotalDays), langue, "jour", "jours", "day", "days", "día", "días");
            if (duree.TotalHours >= 1)
                return FormaterUnite(Math.Ceiling(duree.TotalHours), langue, "heure", "heures", "hour", "hours", "hora", "horas");
            return FormaterUnite(Math.Max(1, Math.Ceiling(duree.TotalMinutes)), langue, "minute", "minutes", "minute", "minutes", "minuto", "minutos");
        }

        private static string FormaterUnite(double valeur, string langue, string frSingulier, string frPluriel, string enSingulier, string enPluriel, string esSingulier, string esPluriel)
        {
            bool singulier = valeur == 1;
            string unite = langue switch
            {
                "en" => singulier ? enSingulier : enPluriel,
                "es" => singulier ? esSingulier : esPluriel,
                _ => singulier ? frSingulier : frPluriel
            };
            return $"{valeur:0} {unite}";
        }
    }
}
