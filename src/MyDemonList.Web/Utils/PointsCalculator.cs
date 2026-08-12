namespace MyDemonList.Web.Utils
{
    public static class PointsCalculator
    {
        private const int SeuilBas = 10;
        private const int SeuilHaut = 20;

        public static int CalculerPoints(int classementPosition, int totalNiveaux, double pivot = 0.30, double fast = 7.0, double slow = 0.55, double top = 1000.0, double targetAtPivot = 200.0)
        {
            if (totalNiveaux <= 1) return (int)top;
            if (classementPosition <= 1) return (int)top;

            if (totalNiveaux <= SeuilBas)
                return Arrondir(CalculerPointsPetiteListe(classementPosition, totalNiveaux, top));

            if (totalNiveaux >= SeuilHaut)
                return Arrondir(CalculerPointsGrandeListe(classementPosition, totalNiveaux, pivot, fast, slow, top, targetAtPivot));

            double pointsPetite = CalculerPointsPetiteListe(classementPosition, totalNiveaux, top);
            double pointsGrande = CalculerPointsGrandeListe(classementPosition, totalNiveaux, pivot, fast, slow, top, targetAtPivot);
            double w = (totalNiveaux - SeuilBas) / (double)(SeuilHaut - SeuilBas);

            return Arrondir((1.0 - w) * pointsPetite + w * pointsGrande);
        }

        private static double CalculerPointsPetiteListe(int classementPosition, int totalNiveaux, double top, double floor = 500.0)
        {
            double proportion = (classementPosition - 1.0) / (totalNiveaux - 1.0);
            return top - proportion * (top - floor);
        }

        private static double CalculerPointsGrandeListe(int classementPosition, int totalNiveaux, double pivot, double fast, double slow, double top, double targetAtPivot)
        {
            int L = totalNiveaux - 1;
            double t = classementPosition - 1.0;
            double tp = Math.Round(1 + pivot * L) - 1.0;

            double NormExp(double tt, double rate)
            {
                double num = Math.Exp(-rate * tt) - Math.Exp(-rate * L);
                double den = 1.0 - Math.Exp(-rate * L);
                return num / den;
            }

            double r1 = fast / totalNiveaux;
            double r2 = slow / totalNiveaux;

            double g1 = NormExp(tp, r1);
            double g2 = NormExp(tp, r2);
            double A = (targetAtPivot - 1.0) / (top - 1.0);
            double w = (Math.Abs(g1 - g2) < 1e-9) ? 0.5 : (A - g2) / (g1 - g2);
            w = Math.Max(0.0, Math.Min(1.0, w));

            double G = w * NormExp(t, r1) + (1.0 - w) * NormExp(t, r2);
            return 1.0 + (top - 1.0) * G;
        }

        private static int Arrondir(double points) => Math.Max(1, (int)Math.Round(points));
    }
}
