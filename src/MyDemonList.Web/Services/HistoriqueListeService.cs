using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Utils;
using System.Text.Json;

namespace MyDemonList.Web.Services
{
    public sealed record ParametresListeHistorique(
        string Nom,
        string? Description,
        bool EstPublique,
        string? DiscordServerUrl,
        RawFootageMode RawFootageMode,
        int? RawFootageTopStart,
        bool VideoToujoursRequise = true,
        int? VideoDifficulteMinimaleId = null,
        int? VideoTopStart = null);

    public sealed record EtatSuppressionListeHistorique(
        bool EstSupprimee,
        DateTime? DateSuppression,
        int? SupprimeeParUtilisateurId);

    public sealed record ClassementHistorique(int Position, int Points);

    public sealed record ReussiteHistorique(int UtilisateurId, string Video, string Statut);

    public sealed record ReussiteSupprimeeHistorique(int NiveauId, ReussiteHistorique Reussite);

    public sealed record SoumissionHistorique(
        int IdSoumission,
        int NiveauId,
        int? UtilisateurId,
        string UrlVideo,
        string NomUtilisateur,
        string? RawFootageUrl,
        DateTime DateSoumission);

    public sealed record NiveauHistorique(
        int Id,
        string IdDuNiveauDansLeJeu,
        string Nom,
        string UrlVerification,
        int Duree,
        DateTime DateAjout,
        int VerifieurId,
        int PublisherId,
        int RatingId,
        int ListeId,
        ClassementHistorique? Classement,
        List<int> CreateurIds,
        List<ReussiteHistorique> Reussites,
        List<SoumissionHistorique> Soumissions);

    public sealed record DecisionSoumissionHistorique(
        SoumissionHistorique Soumission,
        ReussiteHistorique? ReussiteAvant);

    public sealed record MembreListeHistorique(int UtilisateurId, RoleListe Role);

    public class HistoriqueListeService
    {
        private readonly IDbContextFactory<MyDemonListWebDbContext> _dbContextFactory;
        private static readonly JsonSerializerOptions OptionsJson = new(JsonSerializerDefaults.Web);

        public HistoriqueListeService(IDbContextFactory<MyDemonListWebDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public static string CleParametres(int listeId) => $"liste:{listeId}:parametres";
        public static string CleBackground(int listeId) => $"liste:{listeId}:background";
        public static string CleSuppression(int listeId) => $"liste:{listeId}:suppression";
        public static string CleNiveaux(int listeId) => $"liste:{listeId}:niveaux";
        public static string CleMembre(int listeId, int utilisateurId) => $"liste:{listeId}:membre:{utilisateurId}";
        public static string CleSoumission(int listeId, int soumissionId) => $"liste:{listeId}:soumission:{soumissionId}";
        public static string CleReussite(int listeId, int niveauId, int utilisateurId) => $"liste:{listeId}:reussite:{niveauId}:{utilisateurId}";

        public static bool PeutAnnulerAvecRole(RoleListe role, HistoriqueListe historique)
        {
            if (role == RoleListe.Moderateur)
            {
                return historique.TypeAction is
                    TypesActionHistoriqueListe.SoumissionCreee or
                    TypesActionHistoriqueListe.SoumissionModifiee or
                    TypesActionHistoriqueListe.SoumissionAcceptee or
                    TypesActionHistoriqueListe.SoumissionRefusee or
                    TypesActionHistoriqueListe.ReussiteSupprimee;
            }

            if (role == RoleListe.EditeurNiveaux)
            {
                return historique.TypeAction is
                    TypesActionHistoriqueListe.NiveauCree or
                    TypesActionHistoriqueListe.NiveauModifie or
                    TypesActionHistoriqueListe.ClassementModifie or
                    TypesActionHistoriqueListe.SoumissionCreee or
                    TypesActionHistoriqueListe.SoumissionModifiee or
                    TypesActionHistoriqueListe.SoumissionAcceptee or
                    TypesActionHistoriqueListe.SoumissionRefusee or
                    TypesActionHistoriqueListe.ReussiteSupprimee;
            }

            if (role != RoleListe.Administrateur)
                return false;

            if (historique.TypeAction is
                TypesActionHistoriqueListe.NiveauCree or
                TypesActionHistoriqueListe.NiveauModifie or
                TypesActionHistoriqueListe.NiveauSupprime or
                TypesActionHistoriqueListe.ClassementModifie or
                TypesActionHistoriqueListe.SoumissionCreee or
                    TypesActionHistoriqueListe.SoumissionModifiee or
                    TypesActionHistoriqueListe.SoumissionAcceptee or
                    TypesActionHistoriqueListe.SoumissionRefusee or
                    TypesActionHistoriqueListe.ReussiteSupprimee)
            {
                return true;
            }

            if (historique.TypeAction is not (
                TypesActionHistoriqueListe.MembreAjoute or
                TypesActionHistoriqueListe.RoleModifie or
                TypesActionHistoriqueListe.MembreRetire))
            {
                return false;
            }

            MembreListeHistorique? avant = DeserialiserNullable<MembreListeHistorique>(historique.DonneesAvant);
            MembreListeHistorique? apres = DeserialiserNullable<MembreListeHistorique>(historique.DonneesApres);

            return (avant is null || avant.Role is RoleListe.EditeurNiveaux or RoleListe.Moderateur) &&
                   (apres is null || apres.Role is RoleListe.EditeurNiveaux or RoleListe.Moderateur);
        }

        public static HistoriqueListe Ajouter(
            MyDemonListWebDbContext dbContext,
            int listeId,
            int? utilisateurId,
            string typeAction,
            string description,
            string? cleCible,
            object? donneesAvant,
            object? donneesApres,
            bool peutEtreAnnulee = true)
        {
            HistoriqueListe historique = new()
            {
                ListeId = listeId,
                UtilisateurId = utilisateurId,
                TypeAction = typeAction,
                Description = description,
                CleCible = cleCible,
                DonneesAvant = Serialiser(donneesAvant),
                DonneesApres = Serialiser(donneesApres),
                DateCreation = DateTime.Now,
                PeutEtreAnnulee = peutEtreAnnulee
            };

            dbContext.HistoriquesListes.Add(historique);
            return historique;
        }

        public static ParametresListeHistorique CapturerParametres(Liste liste) => new(
            liste.Nom,
            liste.Description,
            liste.EstPublique,
            liste.DiscordServerUrl,
            liste.RawFootageMode,
            liste.RawFootageTopStart,
            liste.VideoToujoursRequise,
            liste.VideoDifficulteMinimaleId,
            liste.VideoTopStart);

        public static EtatSuppressionListeHistorique CapturerSuppression(Liste liste) => new(
            liste.EstSupprimee,
            liste.DateSuppression,
            liste.SupprimeeParUtilisateurId);

        public static SoumissionHistorique CapturerSoumission(SoumissionNiveau soumission) => new(
            soumission.IdSoumission,
            soumission.NiveauId,
            soumission.UtilisateurId,
            soumission.UrlVideo,
            soumission.NomUtilisateur,
            soumission.RawFootageUrl,
            soumission.DateSoumission);

        public static ReussiteHistorique CapturerReussite(ReussiteNiveau reussite) => new(
            reussite.UtilisateurId,
            reussite.Video,
            reussite.Statut);

        public static async Task<NiveauHistorique?> CapturerNiveauAsync(MyDemonListWebDbContext dbContext, int niveauId)
        {
            Niveau? niveau = await dbContext.Niveaux
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == niveauId);

            if (niveau is null) return null;

            Classement? classement = await dbContext.Classements
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.NiveauId == niveauId);

            List<int> createurIds = await dbContext.CreateursNiveaux
                .AsNoTracking()
                .Where(c => c.NiveauId == niveauId)
                .Select(c => c.CreateurId)
                .ToListAsync();

            List<ReussiteHistorique> reussites = await dbContext.ReussitesNiveaux
                .AsNoTracking()
                .Where(r => r.NiveauId == niveauId)
                .Select(r => new ReussiteHistorique(r.UtilisateurId, r.Video, r.Statut))
                .ToListAsync();

            List<SoumissionHistorique> soumissions = await dbContext.SoumissionsNiveaux
                .AsNoTracking()
                .Where(s => s.NiveauId == niveauId)
                .Select(s => new SoumissionHistorique(
                    s.IdSoumission,
                    s.NiveauId,
                    s.UtilisateurId,
                    s.UrlVideo,
                    s.NomUtilisateur,
                    s.RawFootageUrl,
                    s.DateSoumission))
                .ToListAsync();

            return new NiveauHistorique(
                niveau.Id,
                niveau.IdDuNiveauDansLeJeu,
                niveau.Nom,
                niveau.UrlVerification,
                niveau.Duree,
                niveau.DateAjout,
                niveau.VerifieurId,
                niveau.PublisherId,
                niveau.RatingId,
                niveau.ListeId,
                classement is null ? null : new ClassementHistorique(classement.ClassementPosition, classement.Points),
                createurIds,
                reussites,
                soumissions);
        }

        public async Task<List<HistoriqueListe>> ChargerPourUtilisateurAsync(int listeId, int utilisateurId)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();

            Liste? liste = await dbContext.Listes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == listeId);

            if (liste is null)
                return [];

            IQueryable<HistoriqueListe> requete = dbContext.HistoriquesListes
                .AsNoTracking()
                .Include(h => h.Utilisateur)
                .Include(h => h.AnnuleeParUtilisateur)
                .Where(h => h.ListeId == listeId);

            if (liste.UtilisateurId != utilisateurId)
            {
                RoleListe? role = await dbContext.MembresListe
                    .AsNoTracking()
                    .Where(m => m.ListeId == listeId && m.UtilisateurId == utilisateurId)
                    .Select(m => (RoleListe?)m.Role)
                    .FirstOrDefaultAsync();

                requete = role switch
                {
                    RoleListe.Administrateur => requete.Where(h =>
                        h.TypeAction == TypesActionHistoriqueListe.NiveauCree ||
                        h.TypeAction == TypesActionHistoriqueListe.NiveauModifie ||
                        h.TypeAction == TypesActionHistoriqueListe.NiveauSupprime ||
                        h.TypeAction == TypesActionHistoriqueListe.ClassementModifie ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionCreee ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionModifiee ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionAcceptee ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionRefusee ||
                        h.TypeAction == TypesActionHistoriqueListe.ReussiteSupprimee ||
                        h.TypeAction == TypesActionHistoriqueListe.MembreAjoute ||
                        h.TypeAction == TypesActionHistoriqueListe.RoleModifie ||
                        h.TypeAction == TypesActionHistoriqueListe.MembreRetire ||
                        h.TypeAction == TypesActionHistoriqueListe.DemandeQuotaNiveaux),
                    RoleListe.EditeurNiveaux => requete.Where(h =>
                        h.TypeAction == TypesActionHistoriqueListe.NiveauCree ||
                        h.TypeAction == TypesActionHistoriqueListe.NiveauModifie ||
                        h.TypeAction == TypesActionHistoriqueListe.NiveauSupprime ||
                        h.TypeAction == TypesActionHistoriqueListe.ClassementModifie ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionCreee ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionModifiee ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionAcceptee ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionRefusee ||
                        h.TypeAction == TypesActionHistoriqueListe.ReussiteSupprimee ||
                        h.TypeAction == TypesActionHistoriqueListe.DemandeQuotaNiveaux),
                    RoleListe.Moderateur => requete.Where(h =>
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionCreee ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionModifiee ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionAcceptee ||
                        h.TypeAction == TypesActionHistoriqueListe.SoumissionRefusee ||
                        h.TypeAction == TypesActionHistoriqueListe.ReussiteSupprimee),
                    _ => requete.Where(_ => false)
                };
            }

            return await requete
                .OrderByDescending(h => h.Id)
                .ToListAsync();
        }

        public async Task<(bool Succes, string Message)> SupprimerListeAsync(int listeId, int utilisateurId)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

            Liste? liste = await dbContext.Listes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(l => l.Id == listeId);

            if (liste is null) return (false, "ListeIntrouvable");
            if (liste.UtilisateurId != utilisateurId) return (false, "SuppressionListeReserveeProprietaire");
            if (liste.EstSupprimee) return (false, "ListeDejaSupprimee");

            EtatSuppressionListeHistorique avant = CapturerSuppression(liste);
            liste.EstSupprimee = true;
            liste.DateSuppression = DateTime.Now;
            liste.SupprimeeParUtilisateurId = utilisateurId;
            EtatSuppressionListeHistorique apres = CapturerSuppression(liste);

            Ajouter(
                dbContext,
                liste.Id,
                utilisateurId,
                TypesActionHistoriqueListe.ListeSupprimee,
                $"La liste {liste.Nom} a été supprimée.",
                CleSuppression(liste.Id),
                avant,
                apres);

            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return (true, "ListeSupprimeeSucces");
        }

        public async Task<(bool Succes, string Message)> RestaurerListeAsync(int listeId, int utilisateurId)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

            Liste? liste = await dbContext.Listes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(l => l.Id == listeId);

            if (liste is null) return (false, "ListeIntrouvable");
            if (liste.UtilisateurId != utilisateurId) return (false, "RestaurationListeReserveeProprietaire");
            if (!liste.EstSupprimee) return (false, "ListeDejaActive");

            EtatSuppressionListeHistorique avant = CapturerSuppression(liste);
            liste.EstSupprimee = false;
            liste.DateSuppression = null;
            liste.SupprimeeParUtilisateurId = null;
            EtatSuppressionListeHistorique apres = CapturerSuppression(liste);

            Ajouter(
                dbContext,
                liste.Id,
                utilisateurId,
                TypesActionHistoriqueListe.ListeRestauree,
                $"La liste {liste.Nom} a été restaurée.",
                CleSuppression(liste.Id),
                avant,
                apres);

            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return (true, "ListeRestaureeSucces");
        }

        public async Task<(bool Succes, string Message)> AnnulerAsync(int historiqueId, int utilisateurId)
        {
            await using MyDemonListWebDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync();

            HistoriqueListe? historique = await dbContext.HistoriquesListes
                .FirstOrDefaultAsync(h => h.Id == historiqueId);

            if (historique is null) return (false, "ActionIntrouvable");

            Liste? liste = await dbContext.Listes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(l => l.Id == historique.ListeId);

            if (liste is null) return (false, "ListeIntrouvable");
            if (liste.UtilisateurId != utilisateurId)
            {
                if (liste.EstSupprimee)
                    return (false, "HistoriqueListeSupprimeeReserveProprietaire");

                RoleListe? role = await dbContext.MembresListe
                    .AsNoTracking()
                    .Where(m => m.ListeId == liste.Id && m.UtilisateurId == utilisateurId)
                    .Select(m => (RoleListe?)m.Role)
                    .FirstOrDefaultAsync();

                if (role is null || !PeutAnnulerAvecRole(role.Value, historique))
                    return (false, "PermissionAnnulerActionRefusee");
            }
            if (!historique.PeutEtreAnnulee) return (false, "ActionNonAnnulable");
            if (historique.DateAnnulation is not null) return (false, "ActionDejaAnnulee");

            if (!string.IsNullOrWhiteSpace(historique.CleCible))
            {
                bool actionPlusRecente = await dbContext.HistoriquesListes.AnyAsync(h =>
                    h.ListeId == historique.ListeId &&
                    h.CleCible == historique.CleCible &&
                    h.Id > historique.Id &&
                    h.DateAnnulation == null);

                if (actionPlusRecente)
                    return (false, "ActionRecenteDabord");
            }

            (bool succes, string message) = await AppliquerAnnulationAsync(dbContext, historique, liste);
            if (!succes) return (false, message);

            historique.DateAnnulation = DateTime.Now;
            historique.AnnuleeParUtilisateurId = utilisateurId;

            Ajouter(
                dbContext,
                historique.ListeId,
                utilisateurId,
                TypesActionHistoriqueListe.ActionAnnulee,
                $"L'action « {historique.Description} » a été annulée.",
                null,
                null,
                new { historiqueId = historique.Id },
                false);

            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return (true, "ActionAnnuleeSucces");
        }

        private static async Task<(bool Succes, string Message)> AppliquerAnnulationAsync(
            MyDemonListWebDbContext dbContext,
            HistoriqueListe historique,
            Liste liste)
        {
            switch (historique.TypeAction)
            {
                case TypesActionHistoriqueListe.ListeCreee:
                    liste.EstSupprimee = true;
                    liste.DateSuppression = DateTime.Now;
                    liste.SupprimeeParUtilisateurId = historique.UtilisateurId;
                    return (true, string.Empty);

                case TypesActionHistoriqueListe.ListeModifiee:
                    ParametresListeHistorique parametres = Deserialiser<ParametresListeHistorique>(historique.DonneesAvant);
                    AppliquerParametres(liste, parametres);
                    return (true, string.Empty);

                case TypesActionHistoriqueListe.ListeSupprimee:
                case TypesActionHistoriqueListe.ListeRestauree:
                    EtatSuppressionListeHistorique suppression = Deserialiser<EtatSuppressionListeHistorique>(historique.DonneesAvant);
                    AppliquerSuppression(liste, suppression);
                    return (true, string.Empty);

                case TypesActionHistoriqueListe.NiveauCree:
                    NiveauHistorique niveauCree = Deserialiser<NiveauHistorique>(historique.DonneesApres);
                    return await AnnulerCreationNiveauAsync(dbContext, niveauCree);

                case TypesActionHistoriqueListe.NiveauModifie:
                    NiveauHistorique niveauModifie = Deserialiser<NiveauHistorique>(historique.DonneesAvant);
                    return await RestaurerNiveauAsync(dbContext, niveauModifie, false);

                case TypesActionHistoriqueListe.NiveauSupprime:
                    NiveauHistorique niveauSupprime = Deserialiser<NiveauHistorique>(historique.DonneesAvant);
                    return await RestaurerNiveauAsync(dbContext, niveauSupprime, true);

                case TypesActionHistoriqueListe.ClassementModifie:
                    Dictionary<int, int> positions = Deserialiser<Dictionary<int, int>>(historique.DonneesAvant);
                    return await RestaurerPositionsAsync(dbContext, historique.ListeId, positions);

                case TypesActionHistoriqueListe.SoumissionAcceptee:
                    DecisionSoumissionHistorique decision = Deserialiser<DecisionSoumissionHistorique>(historique.DonneesAvant);
                    return await AnnulerAcceptationSoumissionAsync(dbContext, decision);

                case TypesActionHistoriqueListe.SoumissionRefusee:
                    SoumissionHistorique soumission = Deserialiser<SoumissionHistorique>(historique.DonneesAvant);
                    RestaurerSoumission(dbContext, soumission);
                    return (true, string.Empty);

                case TypesActionHistoriqueListe.SoumissionCreee:
                    SoumissionHistorique soumissionCreee = Deserialiser<SoumissionHistorique>(historique.DonneesApres);
                    SoumissionNiveau? soumissionActuelle = await dbContext.SoumissionsNiveaux
                        .FirstOrDefaultAsync(s => s.IdSoumission == soumissionCreee.IdSoumission);
                    if (soumissionActuelle is null)
                        return (false, "SoumissionPlusEnAttente");
                    dbContext.SoumissionsNiveaux.Remove(soumissionActuelle);
                    return (true, string.Empty);

                case TypesActionHistoriqueListe.SoumissionModifiee:
                    SoumissionHistorique soumissionAvant = Deserialiser<SoumissionHistorique>(historique.DonneesAvant);
                    SoumissionNiveau? soumissionAModifier = await dbContext.SoumissionsNiveaux
                        .FirstOrDefaultAsync(s => s.IdSoumission == soumissionAvant.IdSoumission);
                    if (soumissionAModifier is null)
                        return (false, "SoumissionPlusEnAttente");
                    AppliquerSoumission(soumissionAModifier, soumissionAvant);
                    return (true, string.Empty);

                case TypesActionHistoriqueListe.ReussiteSupprimee:
                    ReussiteSupprimeeHistorique reussiteSupprimee = Deserialiser<ReussiteSupprimeeHistorique>(historique.DonneesAvant);
                    return await RestaurerReussiteSupprimeeAsync(dbContext, historique.ListeId, reussiteSupprimee);

                case TypesActionHistoriqueListe.MembreAjoute:
                case TypesActionHistoriqueListe.RoleModifie:
                case TypesActionHistoriqueListe.MembreRetire:
                    MembreListeHistorique? membre = DeserialiserNullable<MembreListeHistorique>(historique.DonneesAvant);
                    int membreId = membre?.UtilisateurId ?? Deserialiser<MembreListeHistorique>(historique.DonneesApres).UtilisateurId;
                    await RestaurerMembreAsync(dbContext, historique.ListeId, membreId, membre);
                    return (true, string.Empty);

                default:
                    return (false, "TypeActionNonPrisEnCharge");
            }
        }

        private static async Task<(bool Succes, string Message)> RestaurerReussiteSupprimeeAsync(
            MyDemonListWebDbContext dbContext,
            int listeId,
            ReussiteSupprimeeHistorique snapshot)
        {
            bool niveauValide = await dbContext.Niveaux.AnyAsync(n => n.Id == snapshot.NiveauId && n.ListeId == listeId);
            if (!niveauValide) return (false, "NiveauExistePlus");

            bool existe = await dbContext.ReussitesNiveaux.AnyAsync(r =>
                r.NiveauId == snapshot.NiveauId &&
                r.UtilisateurId == snapshot.Reussite.UtilisateurId);
            if (existe) return (false, "ReussiteExisteDeja");

            dbContext.ReussitesNiveaux.Add(new ReussiteNiveau
            {
                NiveauId = snapshot.NiveauId,
                UtilisateurId = snapshot.Reussite.UtilisateurId,
                Video = snapshot.Reussite.Video,
                Statut = snapshot.Reussite.Statut
            });

            return (true, string.Empty);
        }

        private static async Task<(bool Succes, string Message)> AnnulerCreationNiveauAsync(
            MyDemonListWebDbContext dbContext,
            NiveauHistorique snapshot)
        {
            Niveau? niveau = await dbContext.Niveaux.FirstOrDefaultAsync(n => n.Id == snapshot.Id);
            if (niveau is null) return (false, "NiveauExistePlus");

            bool aDesReussites = await dbContext.ReussitesNiveaux.AnyAsync(r => r.NiveauId == snapshot.Id);
            bool aDesSoumissions = await dbContext.SoumissionsNiveaux.AnyAsync(s => s.NiveauId == snapshot.Id);
            if (aDesReussites || aDesSoumissions)
                return (false, "NiveauDependancesNonRetirable");

            dbContext.Niveaux.Remove(niveau);
            await dbContext.SaveChangesAsync();
            await NormaliserClassementAsync(dbContext, snapshot.ListeId);
            return (true, string.Empty);
        }

        private static async Task<(bool Succes, string Message)> RestaurerNiveauAsync(
            MyDemonListWebDbContext dbContext,
            NiveauHistorique snapshot,
            bool restaurerDependances)
        {
            Niveau? niveau = await dbContext.Niveaux.FirstOrDefaultAsync(n => n.Id == snapshot.Id);

            if (niveau is null)
            {
                niveau = new Niveau
                {
                    Id = snapshot.Id,
                    IdDuNiveauDansLeJeu = snapshot.IdDuNiveauDansLeJeu,
                    Nom = snapshot.Nom,
                    UrlVerification = snapshot.UrlVerification,
                    Duree = snapshot.Duree,
                    DateAjout = snapshot.DateAjout,
                    VerifieurId = snapshot.VerifieurId,
                    PublisherId = snapshot.PublisherId,
                    RatingId = snapshot.RatingId,
                    ListeId = snapshot.ListeId
                };
                dbContext.Niveaux.Add(niveau);
                await dbContext.SaveChangesAsync();
                await dbContext.Database.ExecuteSqlRawAsync(
                    "SELECT setval(pg_get_serial_sequence('public.\"Niveaux\"', 'Id'), GREATEST((SELECT MAX(\"Id\") FROM public.\"Niveaux\"), 1), true)");
            }
            else
            {
                niveau.IdDuNiveauDansLeJeu = snapshot.IdDuNiveauDansLeJeu;
                niveau.Nom = snapshot.Nom;
                niveau.UrlVerification = snapshot.UrlVerification;
                niveau.Duree = snapshot.Duree;
                niveau.DateAjout = snapshot.DateAjout;
                niveau.VerifieurId = snapshot.VerifieurId;
                niveau.PublisherId = snapshot.PublisherId;
                niveau.RatingId = snapshot.RatingId;
            }

            List<CreateurNiveau> createursActuels = await dbContext.CreateursNiveaux
                .Where(c => c.NiveauId == snapshot.Id)
                .ToListAsync();
            dbContext.CreateursNiveaux.RemoveRange(createursActuels);
            foreach (int createurId in snapshot.CreateurIds.Distinct())
                dbContext.CreateursNiveaux.Add(new CreateurNiveau { NiveauId = snapshot.Id, CreateurId = createurId });

            Classement? classement = await dbContext.Classements.FirstOrDefaultAsync(c => c.NiveauId == snapshot.Id);
            if (snapshot.Classement is not null)
            {
                if (classement is null)
                {
                    await DecalerClassementPourInsertionAsync(dbContext, snapshot.ListeId, snapshot.Classement.Position);
                    dbContext.Classements.Add(new Classement
                    {
                        NiveauId = snapshot.Id,
                        ListeId = snapshot.ListeId,
                        ClassementPosition = snapshot.Classement.Position,
                        Points = snapshot.Classement.Points
                    });
                }
                else
                {
                    classement.Points = snapshot.Classement.Points;
                }
            }

            if (restaurerDependances)
            {
                foreach (ReussiteHistorique reussite in snapshot.Reussites)
                {
                    bool existe = await dbContext.ReussitesNiveaux.AnyAsync(r =>
                        r.NiveauId == snapshot.Id && r.UtilisateurId == reussite.UtilisateurId);
                    if (!existe)
                    {
                        dbContext.ReussitesNiveaux.Add(new ReussiteNiveau
                        {
                            NiveauId = snapshot.Id,
                            UtilisateurId = reussite.UtilisateurId,
                            Video = reussite.Video,
                            Statut = reussite.Statut
                        });
                    }
                }

                foreach (SoumissionHistorique soumission in snapshot.Soumissions)
                    RestaurerSoumission(dbContext, soumission);
            }

            await dbContext.SaveChangesAsync();
            await RecalculerPointsAsync(dbContext, snapshot.ListeId);
            return (true, string.Empty);
        }

        private static async Task<(bool Succes, string Message)> RestaurerPositionsAsync(
            MyDemonListWebDbContext dbContext,
            int listeId,
            Dictionary<int, int> positions)
        {
            List<Classement> classements = await dbContext.Classements
                .Where(c => c.ListeId == listeId)
                .ToListAsync();

            if (positions.Count != classements.Count || positions.Keys.Any(id => classements.All(c => c.Id != id)))
                return (false, "ClassementChangeNonRestaurable");

            foreach (Classement classement in classements)
                classement.ClassementPosition = -1_000_000 - classement.Id;
            await dbContext.SaveChangesAsync();

            foreach (Classement classement in classements)
            {
                if (positions.TryGetValue(classement.Id, out int position))
                    classement.ClassementPosition = position;
            }
            await dbContext.SaveChangesAsync();
            await RecalculerPointsAsync(dbContext, listeId);
            return (true, string.Empty);
        }

        private static async Task<(bool Succes, string Message)> AnnulerAcceptationSoumissionAsync(
            MyDemonListWebDbContext dbContext,
            DecisionSoumissionHistorique decision)
        {
            int? utilisateurId = decision.Soumission.UtilisateurId;
            if (utilisateurId is null)
                return (false, "SoumissionUtilisateurAbsentNonRestaurable");

            ReussiteNiveau? reussite = await dbContext.ReussitesNiveaux.FirstOrDefaultAsync(r =>
                r.NiveauId == decision.Soumission.NiveauId && r.UtilisateurId == utilisateurId.Value);

            if (decision.ReussiteAvant is null)
            {
                if (reussite is not null) dbContext.ReussitesNiveaux.Remove(reussite);
            }
            else if (reussite is null)
            {
                dbContext.ReussitesNiveaux.Add(new ReussiteNiveau
                {
                    NiveauId = decision.Soumission.NiveauId,
                    UtilisateurId = decision.ReussiteAvant.UtilisateurId,
                    Video = decision.ReussiteAvant.Video,
                    Statut = decision.ReussiteAvant.Statut
                });
            }
            else
            {
                reussite.Video = decision.ReussiteAvant.Video;
                reussite.Statut = decision.ReussiteAvant.Statut;
            }

            RestaurerSoumission(dbContext, decision.Soumission);
            return (true, string.Empty);
        }

        private static async Task RestaurerMembreAsync(
            MyDemonListWebDbContext dbContext,
            int listeId,
            int utilisateurId,
            MembreListeHistorique? avant)
        {
            MembreListe? membre = await dbContext.MembresListe.FirstOrDefaultAsync(m =>
                m.ListeId == listeId && m.UtilisateurId == utilisateurId);

            if (avant is null)
            {
                if (membre is not null) dbContext.MembresListe.Remove(membre);
                return;
            }

            if (membre is null)
            {
                dbContext.MembresListe.Add(new MembreListe
                {
                    ListeId = listeId,
                    UtilisateurId = avant.UtilisateurId,
                    Role = avant.Role
                });
            }
            else
            {
                membre.Role = avant.Role;
            }
        }

        private static void RestaurerSoumission(MyDemonListWebDbContext dbContext, SoumissionHistorique soumission)
        {
            dbContext.SoumissionsNiveaux.Add(new SoumissionNiveau
            {
                IdSoumission = soumission.IdSoumission,
                NiveauId = soumission.NiveauId,
                UtilisateurId = soumission.UtilisateurId,
                UrlVideo = soumission.UrlVideo,
                NomUtilisateur = soumission.NomUtilisateur,
                RawFootageUrl = soumission.RawFootageUrl,
                DateSoumission = soumission.DateSoumission
            });
        }

        private static void AppliquerSoumission(SoumissionNiveau cible, SoumissionHistorique source)
        {
            cible.NiveauId = source.NiveauId;
            cible.UtilisateurId = source.UtilisateurId;
            cible.UrlVideo = source.UrlVideo;
            cible.NomUtilisateur = source.NomUtilisateur;
            cible.RawFootageUrl = source.RawFootageUrl;
            cible.DateSoumission = source.DateSoumission;
        }

        private static async Task DecalerClassementPourInsertionAsync(
            MyDemonListWebDbContext dbContext,
            int listeId,
            int position)
        {
            List<Classement> aDecaler = await dbContext.Classements
                .Where(c => c.ListeId == listeId && c.ClassementPosition >= position)
                .OrderByDescending(c => c.ClassementPosition)
                .ToListAsync();

            Dictionary<int, int> nouvellesPositions = aDecaler
                .ToDictionary(c => c.Id, c => c.ClassementPosition + 1);

            foreach (Classement classement in aDecaler)
                classement.ClassementPosition = -1_000_000 - classement.Id;
            await dbContext.SaveChangesAsync();

            foreach (Classement classement in aDecaler)
                classement.ClassementPosition = nouvellesPositions[classement.Id];
            await dbContext.SaveChangesAsync();
        }

        private static async Task NormaliserClassementAsync(MyDemonListWebDbContext dbContext, int listeId)
        {
            List<Classement> classements = await dbContext.Classements
                .Where(c => c.ListeId == listeId)
                .OrderBy(c => c.ClassementPosition)
                .ToListAsync();

            foreach (Classement classement in classements)
                classement.ClassementPosition = -1_000_000 - classement.Id;
            await dbContext.SaveChangesAsync();

            for (int i = 0; i < classements.Count; i++)
                classements[i].ClassementPosition = i + 1;
            await dbContext.SaveChangesAsync();
            await RecalculerPointsAsync(dbContext, listeId);
        }

        private static async Task RecalculerPointsAsync(MyDemonListWebDbContext dbContext, int listeId)
        {
            List<Classement> classements = await dbContext.Classements
                .Where(c => c.ListeId == listeId)
                .ToListAsync();

            int total = classements.Count;
            foreach (Classement classement in classements)
                classement.Points = PointsCalculator.CalculerPoints(classement.ClassementPosition, total);
            await dbContext.SaveChangesAsync();
        }

        private static void AppliquerParametres(Liste liste, ParametresListeHistorique parametres)
        {
            bool ancienneConfigurationSansSeuil =
                !parametres.VideoToujoursRequise &&
                parametres.VideoDifficulteMinimaleId is null &&
                parametres.VideoTopStart is null;

            liste.Nom = parametres.Nom;
            liste.Description = parametres.Description;
            liste.EstPublique = parametres.EstPublique;
            liste.DiscordServerUrl = parametres.DiscordServerUrl;
            liste.RawFootageMode = parametres.RawFootageMode;
            liste.RawFootageTopStart = parametres.RawFootageTopStart;
            liste.VideoToujoursRequise = parametres.VideoToujoursRequise || ancienneConfigurationSansSeuil;
            liste.VideoDifficulteMinimaleId = parametres.VideoDifficulteMinimaleId;
            liste.VideoTopStart = parametres.VideoTopStart;
        }

        private static void AppliquerSuppression(Liste liste, EtatSuppressionListeHistorique suppression)
        {
            liste.EstSupprimee = suppression.EstSupprimee;
            liste.DateSuppression = suppression.DateSuppression;
            liste.SupprimeeParUtilisateurId = suppression.SupprimeeParUtilisateurId;
        }

        private static string? Serialiser(object? valeur) =>
            valeur is null ? null : JsonSerializer.Serialize(valeur, OptionsJson);

        private static T Deserialiser<T>(string? json) =>
            JsonSerializer.Deserialize<T>(json ?? "null", OptionsJson)
            ?? throw new InvalidOperationException("Les données nécessaires à l'annulation sont absentes.");

        private static T? DeserialiserNullable<T>(string? json) where T : class =>
            string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, OptionsJson);
    }
}
