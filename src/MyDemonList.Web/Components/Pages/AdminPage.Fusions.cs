using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using MyDemonList.Web.Services;

namespace MyDemonList.Web.Components.Pages
{
    public partial class AdminPage
    {
        public record FusionAffichage(
            int Id,
            FusionService.InfosCompteFusion Demandeur,
            FusionService.InfosCompteFusion Cible,
            string NomConserve,
            string? Motif,
            DateTime DateDemande);

        private List<FusionAffichage> _fusionsEnAttente = [];
        private string? _fusionMessage;

        private async Task ChargerFusions()
        {
            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            List<FusionUtilisateur> demandes = await dbContext.FusionsUtilisateurs
                .AsNoTracking()
                .Where(f => f.Statut == "EnAttente")
                .OrderBy(f => f.DateDemande)
                .ToListAsync();

            List<FusionAffichage> resultat = [];
            foreach (FusionUtilisateur f in demandes)
            {
                resultat.Add(new FusionAffichage(
                    f.Id,
                    await FusionService.ObtenirInfosCompteAsync(f.UtilisateurDemandeurId),
                    await FusionService.ObtenirInfosCompteAsync(f.UtilisateurCibleId),
                    f.NomConserve,
                    f.Motif,
                    f.DateDemande));
            }
            _fusionsEnAttente = resultat;
        }

        private string ResumeActiviteFusion(FusionService.InfosCompteFusion info)
        {
            List<string> parties = [];
            if (info.NombreReussitesValidees > 0) parties.Add(Texte.Formater("ReussitesResume", "{0} réussite(s)", info.NombreReussitesValidees));
            if (info.NombreListesPossedees > 0) parties.Add(Texte.Formater("ListesPossedeesResume", "{0} liste(s) possédée(s)", info.NombreListesPossedees));
            if (info.NombreNiveauxPublies > 0) parties.Add(Texte.Formater("NiveauxPubliesResume", "{0} niveau(x) publié(s)", info.NombreNiveauxPublies));
            if (info.NombreNiveauxVerifies > 0) parties.Add(Texte.Formater("NiveauxVerifiesResume", "{0} niveau(x) vérifié(s)", info.NombreNiveauxVerifies));
            if (info.NombreNiveauxCrees > 0) parties.Add(Texte.Formater("NiveauxCreesResume", "{0} niveau(x) créé(s)", info.NombreNiveauxCrees));

            return parties.Count > 0 ? string.Join(", ", parties) : Texte["AucuneActiviteSite", "Aucune activité sur le site"];
        }

        private bool EstImpliqueDansFusion(FusionAffichage demande) =>
            demande.Demandeur.UtilisateurId == _utilisateurId || demande.Cible.UtilisateurId == _utilisateurId;

        private async Task AccepterFusionAdmin(FusionAffichage demande)
        {
            if (!_estAutorise) return;

            if (EstImpliqueDansFusion(demande))
            {
                _fusionMessage = Texte["FusionAdminConcerne", "Vous êtes concerné par cette demande de fusion, un autre admin du site doit la traiter."];
                return;
            }

            (bool succes, string message) = await FusionService.AccepterAsync(demande.Id);
            _fusionMessage = message;

            if (succes)
                await ChargerFusions();
        }

        private async Task RefuserFusionAdmin(FusionAffichage demande)
        {
            if (!_estAutorise) return;

            if (EstImpliqueDansFusion(demande))
            {
                _fusionMessage = Texte["FusionAdminConcerne", "Vous êtes concerné par cette demande de fusion, un autre admin du site doit la traiter."];
                return;
            }

            (bool succes, string message) = await FusionService.RefuserAsync(demande.Id);
            _fusionMessage = message;

            if (succes)
                await ChargerFusions();
        }
    }
}
