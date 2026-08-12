using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;

namespace MyDemonList.Web.Utils
{
    public class Chargement
    {
        private readonly IMemoryCache _cache;
        private const string KEY_CLASSEMENTS = "ListeEntiereClassements";
        private const string KEY_CREATEURS = "ListeEntiereCreateursNiveaux";
        private const string KEY_UTILISATEURS = "ListeEntiereUtilisateurs";
        private const string KEY_NIVEAUX = "ListeEntiereNiveaux";
        private const string KEY_REUSSITES = "ListeEntiereReussitesNiveaux";
        private const string KEY_FEATURES = "ListeEntiereDifficultes";

        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

        public Chargement(IMemoryCache cache)
        {
            _cache = cache;
        }

        public (List<Classement> ListeEntiereClassements,
                List<CreateurNiveau> ListeEntiereCreateursNiveaux,
                List<Utilisateur> ListeEntiereUtilisateurs,
                List<Niveau> ListeEntiereNiveaux,
                List<ReussiteNiveau> ListeEntiereReussitesNiveaux,
                List<Difficulte> ListeEntiereFeatures)
            Cache(
                List<Classement> ListeEntiereClassements,
                List<CreateurNiveau> ListeEntiereCreateursNiveaux,
                List<Utilisateur> ListeEntiereUtilisateurs,
                List<Niveau> ListeEntiereNiveaux,
                List<ReussiteNiveau> ListeEntiereReussitesNiveaux,
                List<Difficulte> ListeEntiereFeatures,
                int listeId,
                DbContextOptions<MyDemonListWebDbContext> dbContextOptions)
        {
            string keyClassements = $"{KEY_CLASSEMENTS}_{listeId}";
            string keyCreateurs = $"{KEY_CREATEURS}_{listeId}";
            string keyNiveaux = $"{KEY_NIVEAUX}_{listeId}";
            string keyReussites = $"{KEY_REUSSITES}_{listeId}";

            if (_cache.TryGetValue(keyClassements, out List<Classement>? classementsEnCache) &&
                _cache.TryGetValue(keyCreateurs, out List<CreateurNiveau>? createursEnCache) &&
                _cache.TryGetValue(KEY_UTILISATEURS, out List<Utilisateur>? utilisateursEnCache) &&
                _cache.TryGetValue(keyNiveaux, out List<Niveau>? niveauxEnCache) &&
                _cache.TryGetValue(keyReussites, out List<ReussiteNiveau>? reussitesEnCache) &&
                _cache.TryGetValue(KEY_FEATURES, out List<Difficulte>? difficulteFeatures))
            {
                return (classementsEnCache!,
                        createursEnCache!,
                        utilisateursEnCache!,
                        niveauxEnCache!,
                        reussitesEnCache!,
                        difficulteFeatures!);
            }

            if (dbContextOptions is null)
            {
                return (ListeEntiereClassements,
                        ListeEntiereCreateursNiveaux,
                        ListeEntiereUtilisateurs,
                        ListeEntiereNiveaux,
                        ListeEntiereReussitesNiveaux,
                        ListeEntiereFeatures);
            }

            using (MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(dbContextOptions))
            {
                ListeEntiereNiveaux = dbContext.Niveaux
                    .AsNoTracking()
                    .Where(n => n.ListeId == listeId)
                    .Include(n => n.Rating)
                    .Include(n => n.Verifieur)
                    .Include(n => n.Publisher)
                    .ToList();

                HashSet<int> niveauIds = ListeEntiereNiveaux.Select(n => n.Id).ToHashSet();

                ListeEntiereClassements = dbContext.Classements
                    .AsNoTracking()
                    .Where(c => c.ListeId == listeId)
                    .ToList();

                ListeEntiereCreateursNiveaux = dbContext.CreateursNiveaux
                    .AsNoTracking()
                    .Where(cn => niveauIds.Contains(cn.NiveauId))
                    .Include(cn => cn.Createur)
                    .ToList();

                ListeEntiereReussitesNiveaux = dbContext.ReussitesNiveaux
                    .AsNoTracking()
                    .Where(r => niveauIds.Contains(r.NiveauId))
                    .Include(r => r.Utilisateur)
                    .ToList();

                ListeEntiereUtilisateurs = dbContext.Utilisateurs
                    .AsNoTracking()
                    .ToList();

                ListeEntiereFeatures = dbContext.Difficultes
                    .AsNoTracking()
                    .OrderBy(d => d.Id)
                    .ToList();
            }

            _cache.Set(keyClassements, ListeEntiereClassements, DefaultTtl);
            _cache.Set(keyCreateurs, ListeEntiereCreateursNiveaux, DefaultTtl);
            _cache.Set(KEY_UTILISATEURS, ListeEntiereUtilisateurs, DefaultTtl);
            _cache.Set(keyNiveaux, ListeEntiereNiveaux, DefaultTtl);
            _cache.Set(keyReussites, ListeEntiereReussitesNiveaux, DefaultTtl);
            _cache.Set(KEY_FEATURES, ListeEntiereFeatures, DefaultTtl);

            return (ListeEntiereClassements,
                    ListeEntiereCreateursNiveaux,
                    ListeEntiereUtilisateurs,
                    ListeEntiereNiveaux,
                    ListeEntiereReussitesNiveaux,
                    ListeEntiereFeatures);
        }

        public void ClearCache()
        {
            _cache.Remove(KEY_UTILISATEURS);
            _cache.Remove(KEY_FEATURES);
        }

        public void ClearCache(int listeId)
        {
            _cache.Remove($"{KEY_CLASSEMENTS}_{listeId}");
            _cache.Remove($"{KEY_CREATEURS}_{listeId}");
            _cache.Remove($"{KEY_NIVEAUX}_{listeId}");
            _cache.Remove($"{KEY_REUSSITES}_{listeId}");
        }
    }
}
