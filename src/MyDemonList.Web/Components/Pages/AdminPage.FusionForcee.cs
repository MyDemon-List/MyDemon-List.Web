using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;

namespace MyDemonList.Web.Components.Pages
{
    public partial class AdminPage
    {
        public record CompteFusionDirecte(int UtilisateurId, string NomSite, string? DiscordUsername, string? DiscordDisplayName, bool ADiscord);

        private string _rechercheFusionDirecte1 = string.Empty;
        private string _rechercheFusionDirecte2 = string.Empty;
        private List<CompteFusionDirecte> _suggestionsFusionDirecte1 = [];
        private List<CompteFusionDirecte> _suggestionsFusionDirecte2 = [];
        private CompteFusionDirecte? _compteFusionDirecte1;
        private CompteFusionDirecte? _compteFusionDirecte2;
        private string? _fusionDirecteMessage;
        private bool _fusionDirecteEnCours;
        private bool _afficherConfirmationFusionDirecte;

        private bool ComptesFusionDirecteIdentiques =>
            _compteFusionDirecte1 is not null && _compteFusionDirecte2 is not null &&
            _compteFusionDirecte1.UtilisateurId == _compteFusionDirecte2.UtilisateurId;

        private bool ConditionDiscordFusionDirecteInvalide =>
            _compteFusionDirecte1 is not null && _compteFusionDirecte2 is not null &&
            !ComptesFusionDirecteIdentiques &&
            _compteFusionDirecte1.ADiscord == _compteFusionDirecte2.ADiscord;

        private bool PeutConfirmerFusionDirecte =>
            _compteFusionDirecte1 is not null && _compteFusionDirecte2 is not null &&
            !ComptesFusionDirecteIdentiques && !ConditionDiscordFusionDirecteInvalide;

        private async Task RechercherCompteFusionDirecte(ChangeEventArgs e, bool premierCompte)
        {
            string texte = e.Value?.ToString() ?? string.Empty;

            if (premierCompte)
            {
                _rechercheFusionDirecte1 = texte;
                _compteFusionDirecte1 = null;
            }
            else
            {
                _rechercheFusionDirecte2 = texte;
                _compteFusionDirecte2 = null;
            }

            _fusionDirecteMessage = null;

            if (string.IsNullOrWhiteSpace(texte))
            {
                if (premierCompte) _suggestionsFusionDirecte1 = [];
                else _suggestionsFusionDirecte2 = [];
                return;
            }

            string recherche = texte.Trim().ToLower();
            int? idExclu = premierCompte ? _compteFusionDirecte2?.UtilisateurId : _compteFusionDirecte1?.UtilisateurId;

            using MyDemonListWebDbContext dbContext = new MyDemonListWebDbContext(DbContextOptions);

            List<Utilisateur> parNom = await dbContext.Utilisateurs
                .AsNoTracking()
                .Where(u => u.Nom.ToLower().Contains(recherche))
                .OrderBy(u => u.Nom)
                .Take(10)
                .ToListAsync();

            List<DiscordAccount> parDiscord = await dbContext.DiscordAccounts
                .AsNoTracking()
                .Include(d => d.Utilisateur)
                .Where(d =>
                    (d.DiscordUsername != null && d.DiscordUsername.ToLower().Contains(recherche)) ||
                    (d.DiscordDisplayName != null && d.DiscordDisplayName.ToLower().Contains(recherche)))
                .OrderBy(d => d.Utilisateur.Nom)
                .Take(10)
                .ToListAsync();

            Dictionary<int, Utilisateur> utilisateursTrouves = parNom.ToDictionary(u => u.Id);
            foreach (DiscordAccount d in parDiscord)
                utilisateursTrouves.TryAdd(d.UtilisateurId, d.Utilisateur);

            List<int> ids = [.. utilisateursTrouves.Keys];
            List<DiscordAccount> comptesDiscord = await dbContext.DiscordAccounts
                .AsNoTracking()
                .Where(d => ids.Contains(d.UtilisateurId))
                .ToListAsync();

            List<CompteFusionDirecte> resultat = utilisateursTrouves.Values
                .Where(u => idExclu is null || u.Id != idExclu)
                .OrderBy(u => u.Nom)
                .Take(8)
                .Select(u =>
                {
                    DiscordAccount? compte = comptesDiscord.FirstOrDefault(d => d.UtilisateurId == u.Id);
                    return new CompteFusionDirecte(u.Id, u.Nom, compte?.DiscordUsername, compte?.DiscordDisplayName, compte is not null);
                })
                .ToList();

            if (premierCompte) _suggestionsFusionDirecte1 = resultat;
            else _suggestionsFusionDirecte2 = resultat;
        }

        private void SelectCompteFusionDirecte(CompteFusionDirecte candidat, bool premierCompte)
        {
            if (premierCompte)
            {
                _compteFusionDirecte1 = candidat;
                _rechercheFusionDirecte1 = candidat.NomSite;
                _suggestionsFusionDirecte1 = [];
            }
            else
            {
                _compteFusionDirecte2 = candidat;
                _rechercheFusionDirecte2 = candidat.NomSite;
                _suggestionsFusionDirecte2 = [];
            }
        }

        private void DemanderFusionDirecte()
        {
            if (!_estChef || !PeutConfirmerFusionDirecte) return;

            _fusionDirecteMessage = null;
            _afficherConfirmationFusionDirecte = true;
        }

        private void AnnulerFusionDirecte()
        {
            _afficherConfirmationFusionDirecte = false;
        }

        private async Task ConfirmerFusionDirecteAsync()
        {
            if (!_estChef || _compteFusionDirecte1 is null || _compteFusionDirecte2 is null || !PeutConfirmerFusionDirecte) return;

            _afficherConfirmationFusionDirecte = false;
            _fusionDirecteEnCours = true;
            _fusionDirecteMessage = null;

            try
            {
                (bool succes, string message) = await FusionService.FusionnerDirectementAsync(_compteFusionDirecte1.UtilisateurId, _compteFusionDirecte2.UtilisateurId);
                _fusionDirecteMessage = message;

                if (succes)
                {
                    _rechercheFusionDirecte1 = string.Empty;
                    _rechercheFusionDirecte2 = string.Empty;
                    _compteFusionDirecte1 = null;
                    _compteFusionDirecte2 = null;
                    _suggestionsFusionDirecte1 = [];
                    _suggestionsFusionDirecte2 = [];
                    await ChargerFusions();
                }
            }
            finally
            {
                _fusionDirecteEnCours = false;
            }
        }
    }
}
