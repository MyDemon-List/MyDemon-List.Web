using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;
using MyDemonList.Web.Utils;

namespace MyDemonList.Web.Components.Pages
{
    public partial class GererListePage
    {
        private const int DureeAnimationFermetureMs = 350;

        private int? _soumissionEnCoursId;
        private string? _soumissionErreur;
        private int? _soumissionOuverteId;
        private int? _soumissionEnFermetureId;

        private void ToggleSoumission(int idSoumission)
        {
            _soumissionOuverteId = _soumissionOuverteId == idSoumission ? null : idSoumission;
        }

        private async Task JouerAnimationFermeture(int idSoumission)
        {
            _soumissionEnFermetureId = idSoumission;
            if (_soumissionOuverteId == idSoumission)
                _soumissionOuverteId = null;

            StateHasChanged();
            await Task.Delay(DureeAnimationFermetureMs);
        }

        private async Task AccepterSoumission(int idSoumission)
        {
            if (!PeutGererSoumissions) return;

            _soumissionErreur = null;
            _soumissionEnCoursId = idSoumission;

            try
            {
                await JouerAnimationFermeture(idSoumission);

                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                SoumissionNiveau? soumission = await dbContext.SoumissionsNiveaux
                    .Include(s => s.Niveau)
                    .ThenInclude(n => n.Liste)
                    .FirstOrDefaultAsync(s => s.IdSoumission == idSoumission);

                if (soumission is null) return;

                Utilisateur? utilisateur = soumission.UtilisateurId is int utilisateurId
                    ? await dbContext.Utilisateurs.FirstOrDefaultAsync(u => u.Id == utilisateurId)
                    : null;
                utilisateur ??= await ResoudreOuCreerUtilisateurAsync(dbContext, soumission.NomUtilisateur);

                ReussiteNiveau? reussite = await dbContext.ReussitesNiveaux
                    .FirstOrDefaultAsync(r => r.UtilisateurId == utilisateur.Id && r.NiveauId == soumission.NiveauId);

                SoumissionHistorique soumissionAvant = HistoriqueListeService.CapturerSoumission(soumission) with
                {
                    UtilisateurId = utilisateur.Id
                };
                ReussiteHistorique? reussiteAvant = reussite is null
                    ? null
                    : HistoriqueListeService.CapturerReussite(reussite);

                if (reussite is null)
                {
                    dbContext.ReussitesNiveaux.Add(new ReussiteNiveau
                    {
                        UtilisateurId = utilisateur.Id,
                        NiveauId = soumission.NiveauId,
                        Video = soumission.UrlVideo,
                        Statut = "Validee"
                    });
                }
                else
                {
                    reussite.Video = soumission.UrlVideo;
                    reussite.Statut = "Validee";
                }

                if (soumission.UtilisateurId is int destinataireId)
                {
                    string lien = $"{SeoUtils.CheminClassement(soumission.Niveau.ListeId, soumission.Niveau.Liste.Nom)}?joueur={utilisateur.Id}";
                    MyDemonList.Web.Services.NotificationService.Ajouter(
                        dbContext,
                        destinataireId,
                        TypesNotification.SoumissionAcceptee,
                        "Soumission acceptée",
                        $"Votre réussite de {soumission.Niveau.Nom} dans {soumission.Niveau.Liste.Nom} a été validée.",
                        lien);
                }

                dbContext.SoumissionsNiveaux.Remove(soumission);

                HistoriqueListeService.Ajouter(
                    dbContext,
                    _listeId,
                    _utilisateurId,
                    TypesActionHistoriqueListe.SoumissionAcceptee,
                    $"La soumission de {soumission.NomUtilisateur} pour {soumission.Niveau.Nom} a été acceptée.",
                    HistoriqueListeService.CleSoumission(_listeId, idSoumission),
                    new DecisionSoumissionHistorique(soumissionAvant, reussiteAvant),
                    null);
                await dbContext.SaveChangesAsync();

                if (soumission.UtilisateurId is int destinataireAccepteId)
                    NotificationService.Signaler(destinataireAccepteId);

                Chargement.ClearCache(_listeId);
                await ChargerDonnees();
            }
            catch (Exception ex)
            {
                _soumissionErreur = $"Erreur lors de l'acceptation : {ex.Message}";
            }
            finally
            {
                _soumissionEnCoursId = null;
                _soumissionEnFermetureId = null;
            }
        }

        private async Task RefuserSoumission(int idSoumission)
        {
            if (!PeutGererSoumissions) return;

            _soumissionErreur = null;
            _soumissionEnCoursId = idSoumission;

            try
            {
                await JouerAnimationFermeture(idSoumission);

                using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

                SoumissionNiveau? soumission = await dbContext.SoumissionsNiveaux
                    .Include(s => s.Niveau)
                    .ThenInclude(n => n.Liste)
                    .FirstOrDefaultAsync(s => s.IdSoumission == idSoumission);

                if (soumission is null) return;

                SoumissionHistorique soumissionAvant = HistoriqueListeService.CapturerSoumission(soumission);

                if (soumission.UtilisateurId is int destinataireId)
                {
                    string lien = $"{SeoUtils.CheminSoumission(soumission.Niveau.ListeId, soumission.Niveau.Liste.Nom)}?niveau={Uri.EscapeDataString(soumission.Niveau.IdDuNiveauDansLeJeu)}";
                    MyDemonList.Web.Services.NotificationService.Ajouter(
                        dbContext,
                        destinataireId,
                        TypesNotification.SoumissionRefusee,
                        "Soumission refusée",
                        $"Votre soumission pour {soumission.Niveau.Nom} dans {soumission.Niveau.Liste.Nom} n'a pas été validée.",
                        lien);
                }

                dbContext.SoumissionsNiveaux.Remove(soumission);
                HistoriqueListeService.Ajouter(
                    dbContext,
                    _listeId,
                    _utilisateurId,
                    TypesActionHistoriqueListe.SoumissionRefusee,
                    $"La soumission de {soumission.NomUtilisateur} pour {soumission.Niveau.Nom} a été refusée.",
                    HistoriqueListeService.CleSoumission(_listeId, idSoumission),
                    soumissionAvant,
                    null);
                await dbContext.SaveChangesAsync();

                if (soumission.UtilisateurId is int destinataireRefuseId)
                    NotificationService.Signaler(destinataireRefuseId);

                await ChargerDonnees();
            }
            catch (Exception ex)
            {
                _soumissionErreur = $"Erreur lors du refus : {ex.Message}";
            }
            finally
            {
                _soumissionEnCoursId = null;
                _soumissionEnFermetureId = null;
            }
        }
    }
}
