using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;

namespace MyDemonList.Web.Components.Pages
{
    public partial class GererListePage
    {
        private const int DureeAnimationClassementMs = 480;
        private readonly Dictionary<int, int> _decalagesAnimationClassement = [];
        private bool _deplacementEnCours;

        private string ObtenirClasseLigneNiveau(int classementId) =>
            _decalagesAnimationClassement.ContainsKey(classementId)
                ? "ligne-niveau en-deplacement"
                : "ligne-niveau";

        private string? ObtenirStyleLigneNiveau(int classementId)
        {
            if (!_decalagesAnimationClassement.TryGetValue(classementId, out int decalage) || decalage == 0)
                return null;

            int amplitude = Math.Abs(decalage);
            string depart = decalage > 0
                ? $"calc({amplitude * 100}% + {amplitude * 12}px)"
                : $"calc(-{amplitude * 100}% - {amplitude * 12}px)";

            return $"--position-depart: {depart};";
        }

        private async Task JouerAnimationClassement(Dictionary<int, int> decalages)
        {
            _decalagesAnimationClassement.Clear();

            foreach ((int classementId, int decalage) in decalages)
            {
                if (decalage != 0)
                    _decalagesAnimationClassement[classementId] = decalage;
            }

            StateHasChanged();
            await Task.Delay(DureeAnimationClassementMs);

            _decalagesAnimationClassement.Clear();
            if (!_estDispose)
                StateHasChanged();
        }

        private static async Task RecalculerPointsListeAsync(MyDemonListWebDbContext dbContext, int listeId)
        {
            List<Classement> classements = await dbContext.Classements
                .Where(c => c.ListeId == listeId)
                .ToListAsync();

            int total = classements.Count;

            foreach (Classement c in classements)
                c.Points = PointsCalculator.CalculerPoints(c.ClassementPosition, total);

            await dbContext.SaveChangesAsync();
        }

        private async Task DeplacerNiveau(int classementId, int direction)
        {
            if (!PeutModifierNiveaux || _deplacementEnCours) return;

            _deplacementEnCours = true;

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);
                await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

                Dictionary<int, int> positionsAvant = await dbContext.Classements
                    .Where(c => c.ListeId == _listeId)
                    .ToDictionaryAsync(c => c.Id, c => c.ClassementPosition);

                Classement courant = await dbContext.Classements.FirstAsync(c => c.Id == classementId);
                Classement? voisin = await dbContext.Classements
                    .FirstOrDefaultAsync(c => c.ListeId == courant.ListeId && c.ClassementPosition == courant.ClassementPosition + direction);

                if (voisin is null)
                {
                    await transaction.RollbackAsync();
                    return;
                }

                int posCourant = courant.ClassementPosition;
                int posVoisin = voisin.ClassementPosition;
                Dictionary<int, int> decalages = new()
                {
                    [courant.Id] = posCourant - posVoisin,
                    [voisin.Id] = posVoisin - posCourant
                };

                courant.ClassementPosition = -1;
                await dbContext.SaveChangesAsync();

                voisin.ClassementPosition = posCourant;
                await dbContext.SaveChangesAsync();

                courant.ClassementPosition = posVoisin;
                await dbContext.SaveChangesAsync();

                await RecalculerPointsListeAsync(dbContext, courant.ListeId);

                Dictionary<int, int> positionsApres = await dbContext.Classements
                    .Where(c => c.ListeId == _listeId)
                    .ToDictionaryAsync(c => c.Id, c => c.ClassementPosition);
                string nomNiveau = await dbContext.Niveaux
                    .Where(n => n.Id == courant.NiveauId)
                    .Select(n => n.Nom)
                    .FirstAsync();

                HistoriqueListeService.Ajouter(
                    dbContext,
                    _listeId,
                    _utilisateurId,
                    TypesActionHistoriqueListe.ClassementModifie,
                    $"Le niveau {nomNiveau} a été déplacé de la position {posCourant} à la position {posVoisin}.",
                    HistoriqueListeService.CleNiveaux(_listeId),
                    positionsAvant,
                    positionsApres);
                await dbContext.SaveChangesAsync();

                await transaction.CommitAsync();

                Chargement.ClearCache(_listeId);
                await ChargerDonnees();
                await JouerAnimationClassement(decalages);
            }
            finally
            {
                _deplacementEnCours = false;
            }
        }

        private Task MonterNiveau(int classementId) => DeplacerNiveau(classementId, -1);
        private Task DescendreNiveau(int classementId) => DeplacerNiveau(classementId, 1);

        private async Task ValiderPosition(int classementId)
        {
            bool aUneSaisie = _positionsSaisies.TryGetValue(classementId, out string? saisie);
            _positionsSaisies.Remove(classementId);

            if (!aUneSaisie || !int.TryParse(saisie, out int nouvellePosition))
                return;

            await DeplacerNiveauVersPosition(classementId, nouvellePosition);
        }

        private async Task DeplacerNiveauVersPosition(int classementId, int nouvellePosition)
        {
            if (!PeutModifierNiveaux || _deplacementEnCours) return;

            _deplacementEnCours = true;

            try
            {
                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);
                await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

                Dictionary<int, int> positionsAvant = await dbContext.Classements
                    .Where(c => c.ListeId == _listeId)
                    .ToDictionaryAsync(c => c.Id, c => c.ClassementPosition);

                Classement courant = await dbContext.Classements.FirstAsync(c => c.Id == classementId);

                List<Classement> autres = await dbContext.Classements
                    .Where(c => c.ListeId == courant.ListeId && c.Id != courant.Id)
                    .ToListAsync();

                int total = autres.Count + 1;
                int positionActuelle = courant.ClassementPosition;
                int positionCible = Math.Clamp(nouvellePosition, 1, total);

                if (positionCible == positionActuelle)
                {
                    await transaction.RollbackAsync();
                    return;
                }

                int delta = positionCible < positionActuelle ? 1 : -1;
                List<(Classement Entite, int NouvellePosition)> aDeplacer = autres
                    .Where(c => delta == 1
                        ? c.ClassementPosition >= positionCible && c.ClassementPosition < positionActuelle
                        : c.ClassementPosition > positionActuelle && c.ClassementPosition <= positionCible)
                    .Select(c => (c, c.ClassementPosition + delta))
                    .ToList();

                Dictionary<int, int> decalages = aDeplacer.ToDictionary(
                    x => x.Entite.Id,
                    x => x.Entite.ClassementPosition - x.NouvellePosition);
                decalages[courant.Id] = positionActuelle - positionCible;

                courant.ClassementPosition = -1;
                await dbContext.SaveChangesAsync();

                foreach ((Classement entite, int _) in aDeplacer) entite.ClassementPosition = -1000 - entite.Id;
                await dbContext.SaveChangesAsync();

                foreach ((Classement entite, int position) in aDeplacer) entite.ClassementPosition = position;
                await dbContext.SaveChangesAsync();

                courant.ClassementPosition = positionCible;
                await dbContext.SaveChangesAsync();

                await RecalculerPointsListeAsync(dbContext, courant.ListeId);

                Dictionary<int, int> positionsApres = await dbContext.Classements
                    .Where(c => c.ListeId == _listeId)
                    .ToDictionaryAsync(c => c.Id, c => c.ClassementPosition);
                string nomNiveau = await dbContext.Niveaux
                    .Where(n => n.Id == courant.NiveauId)
                    .Select(n => n.Nom)
                    .FirstAsync();

                HistoriqueListeService.Ajouter(
                    dbContext,
                    _listeId,
                    _utilisateurId,
                    TypesActionHistoriqueListe.ClassementModifie,
                    $"Le niveau {nomNiveau} a été déplacé de la position {positionActuelle} à la position {positionCible}.",
                    HistoriqueListeService.CleNiveaux(_listeId),
                    positionsAvant,
                    positionsApres);
                await dbContext.SaveChangesAsync();

                await transaction.CommitAsync();

                Chargement.ClearCache(_listeId);
                await ChargerDonnees();
                await JouerAnimationClassement(decalages);
            }
            finally
            {
                _deplacementEnCours = false;
            }
        }

        private void DemanderSuppression(int niveauId)
        {
            if (!PeutSupprimerNiveaux) return;

            _niveauASupprimerId = niveauId;
            _afficherConfirmationSuppression = true;
        }

        private void AnnulerSuppression()
        {
            _niveauASupprimerId = null;
            _afficherConfirmationSuppression = false;
        }

        private async Task ConfirmerSuppression()
        {
            if (!PeutSupprimerNiveaux) return;
            if (_niveauASupprimerId is not int id) return;

            using (MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions))
            {
                await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();
                Niveau? niveau = await dbContext.Niveaux.FirstOrDefaultAsync(n => n.Id == id);
                if (niveau is not null)
                {
                    NiveauHistorique? avant = await HistoriqueListeService.CapturerNiveauAsync(dbContext, niveau.Id);
                    string nomNiveau = niveau.Nom;
                    dbContext.Niveaux.Remove(niveau);
                    await dbContext.SaveChangesAsync();

                    HistoriqueListeService.Ajouter(
                        dbContext,
                        _listeId,
                        _utilisateurId,
                        TypesActionHistoriqueListe.NiveauSupprime,
                        $"Le niveau {nomNiveau} a été supprimé.",
                        HistoriqueListeService.CleNiveaux(_listeId),
                        avant,
                        null);
                }

                List<Classement> restants = await dbContext.Classements
                    .Where(c => c.ListeId == _listeId)
                    .OrderBy(c => c.ClassementPosition)
                    .ToListAsync();

                foreach (Classement c in restants) c.ClassementPosition += 1_000_000;
                await dbContext.SaveChangesAsync();

                for (int i = 0; i < restants.Count; i++) restants[i].ClassementPosition = i + 1;
                await dbContext.SaveChangesAsync();

                await RecalculerPointsListeAsync(dbContext, _listeId);
                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            Chargement.ClearCache(_listeId);
            _afficherConfirmationSuppression = false;
            _niveauASupprimerId = null;

            if (_niveauEnEditionId == id)
                ReinitialiserFormulaire();

            await ChargerDonnees();
        }
    }
}
