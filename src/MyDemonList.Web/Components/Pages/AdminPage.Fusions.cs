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

        private static string ResumeActiviteFusion(FusionService.InfosCompteFusion info)
        {
            List<string> parties = [];
            if (info.NombreReussitesValidees > 0) parties.Add($"{info.NombreReussitesValidees} réussite(s)");
            if (info.NombreListesPossedees > 0) parties.Add($"{info.NombreListesPossedees} liste(s) possédée(s)");
            if (info.NombreNiveauxPublies > 0) parties.Add($"{info.NombreNiveauxPublies} niveau(x) publié(s)");
            if (info.NombreNiveauxVerifies > 0) parties.Add($"{info.NombreNiveauxVerifies} niveau(x) vérifié(s)");
            if (info.NombreNiveauxCrees > 0) parties.Add($"{info.NombreNiveauxCrees} niveau(x) créé(s)");

            return parties.Count > 0 ? string.Join(", ", parties) : "Aucune activité sur le site";
        }

        private bool EstImpliqueDansFusion(FusionAffichage demande) =>
            demande.Demandeur.UtilisateurId == _utilisateurId || demande.Cible.UtilisateurId == _utilisateurId;

        private async Task AccepterFusionAdmin(FusionAffichage demande)
        {
            if (!_estAutorise) return;

            if (EstImpliqueDansFusion(demande))
            {
                _fusionMessage = "Vous êtes concerné par cette demande de fusion, un autre admin du site doit la traiter.";
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
                _fusionMessage = "Vous êtes concerné par cette demande de fusion, un autre admin du site doit la traiter.";
                return;
            }

            (bool succes, string message) = await FusionService.RefuserAsync(demande.Id);
            _fusionMessage = message;

            if (succes)
                await ChargerFusions();
        }
    }
}
