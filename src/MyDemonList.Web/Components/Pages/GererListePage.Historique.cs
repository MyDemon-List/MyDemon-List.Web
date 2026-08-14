using MyDemonList.Web.Entities;
using MyDemonList.Web.Services;

namespace MyDemonList.Web.Components.Pages
{
    public partial class GererListePage
    {
        private List<HistoriqueListe> _historiques = [];
        private HistoriqueListe? _historiqueAAnnuler;
        private bool _afficherConfirmationSuppressionListe;
        private bool _afficherConfirmationRestaurationListe;
        private bool _afficherConfirmationAnnulation;
        private bool _actionHistoriqueEnCours;
        private string _confirmationNomListe = string.Empty;
        private string? _historiqueErreur;
        private string? _historiqueSucces;

        private bool NomListeConfirme => string.Equals(
            _confirmationNomListe.Trim(),
            _listeNom,
            StringComparison.Ordinal);

        private async Task ChargerHistorique()
        {
            if (!PeutVoirOngletHistorique) return;
            _historiques = await HistoriqueService.ChargerPourUtilisateurAsync(_listeId, _utilisateurId);
        }

        private void OuvrirSuppressionListe()
        {
            _confirmationNomListe = string.Empty;
            _historiqueErreur = null;
            _afficherConfirmationSuppressionListe = true;
        }

        private void FermerSuppressionListe()
        {
            if (_actionHistoriqueEnCours) return;
            _afficherConfirmationSuppressionListe = false;
            _confirmationNomListe = string.Empty;
        }

        private void OuvrirRestaurationListe()
        {
            _historiqueErreur = null;
            _afficherConfirmationRestaurationListe = true;
        }

        private void FermerRestaurationListe()
        {
            if (_actionHistoriqueEnCours) return;
            _afficherConfirmationRestaurationListe = false;
        }

        private void OuvrirAnnulation(HistoriqueListe historique)
        {
            _historiqueAAnnuler = historique;
            _historiqueErreur = null;
            _afficherConfirmationAnnulation = true;
        }

        private void FermerAnnulation()
        {
            if (_actionHistoriqueEnCours) return;
            _afficherConfirmationAnnulation = false;
            _historiqueAAnnuler = null;
        }

        private async Task SupprimerListe()
        {
            if (!PeutSupprimerOuRestaurerListe || !NomListeConfirme || _actionHistoriqueEnCours) return;

            _actionHistoriqueEnCours = true;
            _historiqueErreur = null;
            _historiqueSucces = null;

            try
            {
                (bool succes, string message) = await HistoriqueService.SupprimerListeAsync(_listeId, _utilisateurId);
                if (!succes)
                {
                    _historiqueErreur = message;
                    return;
                }

                _listeEstSupprimee = true;
                _ongletActuel = Onglet.Historique;
                _afficherConfirmationSuppressionListe = false;
                _confirmationNomListe = string.Empty;
                _historiqueSucces = message;
                Chargement.ClearCache(_listeId);
                await ChargerHistorique();
            }
            finally
            {
                _actionHistoriqueEnCours = false;
            }
        }

        private async Task RestaurerListe()
        {
            if (!PeutSupprimerOuRestaurerListe || _actionHistoriqueEnCours) return;

            _actionHistoriqueEnCours = true;
            _historiqueErreur = null;
            _historiqueSucces = null;

            try
            {
                (bool succes, string message) = await HistoriqueService.RestaurerListeAsync(_listeId, _utilisateurId);
                if (!succes)
                {
                    _historiqueErreur = message;
                    return;
                }

                _listeEstSupprimee = false;
                _afficherConfirmationRestaurationListe = false;
                _historiqueSucces = message;
                Chargement.ClearCache(_listeId);
                await ChargerHistorique();
            }
            finally
            {
                _actionHistoriqueEnCours = false;
            }
        }

        private async Task AnnulerAction()
        {
            if (!PeutAnnulerHistorique || _historiqueAAnnuler is null || _actionHistoriqueEnCours) return;

            _actionHistoriqueEnCours = true;
            _historiqueErreur = null;
            _historiqueSucces = null;

            try
            {
                (bool succes, string message) = await HistoriqueService.AnnulerAsync(_historiqueAAnnuler.Id, _utilisateurId);
                if (!succes)
                {
                    _historiqueErreur = message;
                    return;
                }

                _afficherConfirmationAnnulation = false;
                _historiqueAAnnuler = null;
                _historiqueSucces = message;
                Chargement.ClearCache(_listeId);
                await ChargerDonnees();

                if (_listeEstSupprimee)
                    _ongletActuel = Onglet.Historique;
            }
            finally
            {
                _actionHistoriqueEnCours = false;
            }
        }

        private static string InitialeAuteur(HistoriqueListe historique)
        {
            string nom = historique.Utilisateur?.Nom ?? "Système";
            return nom.Length == 0 ? "?" : nom[..1].ToUpperInvariant();
        }

        private bool PeutAnnulerSelonRole(HistoriqueListe historique)
        {
            return _roleEffectif switch
            {
                RoleEffectif.Proprietaire => true,
                RoleEffectif.Administrateur => HistoriqueListeService.PeutAnnulerAvecRole(RoleListe.Administrateur, historique),
                RoleEffectif.EditeurNiveaux => HistoriqueListeService.PeutAnnulerAvecRole(RoleListe.EditeurNiveaux, historique),
                RoleEffectif.Moderateur => HistoriqueListeService.PeutAnnulerAvecRole(RoleListe.Moderateur, historique),
                _ => false
            };
        }

        private bool PeutAnnulerMaintenant(HistoriqueListe historique) =>
            PeutAnnulerSelonRole(historique) &&
                   historique.PeutEtreAnnulee &&
                   historique.DateAnnulation is null &&
                   (string.IsNullOrWhiteSpace(historique.CleCible) || !_historiques.Any(h =>
                       h.Id > historique.Id &&
                       h.CleCible == historique.CleCible &&
                       h.DateAnnulation is null));

        private static string LibelleAction(string typeAction) => typeAction switch
        {
            TypesActionHistoriqueListe.ListeCreee => "Création",
            TypesActionHistoriqueListe.ListeModifiee => "Paramètres",
            TypesActionHistoriqueListe.ListeSupprimee => "Suppression",
            TypesActionHistoriqueListe.ListeRestauree => "Restauration",
            TypesActionHistoriqueListe.NiveauCree => "Niveau ajouté",
            TypesActionHistoriqueListe.NiveauModifie => "Niveau modifié",
            TypesActionHistoriqueListe.NiveauSupprime => "Niveau supprimé",
            TypesActionHistoriqueListe.ClassementModifie => "Classement",
            TypesActionHistoriqueListe.SoumissionAcceptee => "Soumission acceptée",
            TypesActionHistoriqueListe.SoumissionRefusee => "Soumission refusée",
            TypesActionHistoriqueListe.SoumissionCreee => "Soumission créée",
            TypesActionHistoriqueListe.SoumissionModifiee => "Soumission modifiée",
            TypesActionHistoriqueListe.MembreAjoute => "Membre ajouté",
            TypesActionHistoriqueListe.RoleModifie => "Rôle modifié",
            TypesActionHistoriqueListe.MembreRetire => "Membre retiré",
            TypesActionHistoriqueListe.DemandeQuotaNiveaux => "Demande de quota",
            TypesActionHistoriqueListe.ActionAnnulee => "Annulation",
            _ => typeAction
        };
    }
}
