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
                    _historiqueErreur = TraduireMessageHistorique(message);
                    return;
                }

                _listeEstSupprimee = true;
                _ongletActuel = Onglet.Historique;
                _afficherConfirmationSuppressionListe = false;
                _confirmationNomListe = string.Empty;
                _historiqueSucces = TraduireMessageHistorique(message);
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
                    _historiqueErreur = TraduireMessageHistorique(message);
                    return;
                }

                _listeEstSupprimee = false;
                _afficherConfirmationRestaurationListe = false;
                _historiqueSucces = TraduireMessageHistorique(message);
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
                    _historiqueErreur = TraduireMessageHistorique(message);
                    return;
                }

                _afficherConfirmationAnnulation = false;
                _historiqueAAnnuler = null;
                _historiqueSucces = TraduireMessageHistorique(message);
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

        private string InitialeAuteur(HistoriqueListe historique)
        {
            string nom = historique.Utilisateur?.Nom ?? Texte["Systeme", "Système"];
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

        private string LibelleAction(string typeAction) => typeAction switch
        {
            TypesActionHistoriqueListe.ListeCreee => Texte["ActionCreation", "Création"],
            TypesActionHistoriqueListe.ListeModifiee => Texte["ActionParametres", "Paramètres"],
            TypesActionHistoriqueListe.ListeSupprimee => Texte["ActionSuppression", "Suppression"],
            TypesActionHistoriqueListe.ListeRestauree => Texte["ActionRestauration", "Restauration"],
            TypesActionHistoriqueListe.NiveauCree => Texte["ActionNiveauAjoute", "Niveau ajouté"],
            TypesActionHistoriqueListe.NiveauModifie => Texte["ActionNiveauModifie", "Niveau modifié"],
            TypesActionHistoriqueListe.NiveauSupprime => Texte["ActionNiveauSupprime", "Niveau supprimé"],
            TypesActionHistoriqueListe.ClassementModifie => Texte["ActionClassement", "Classement"],
            TypesActionHistoriqueListe.SoumissionAcceptee => Texte["ActionSoumissionAcceptee", "Soumission acceptée"],
            TypesActionHistoriqueListe.SoumissionRefusee => Texte["ActionSoumissionRefusee", "Soumission refusée"],
            TypesActionHistoriqueListe.SoumissionCreee => Texte["ActionSoumissionCreee", "Soumission créée"],
            TypesActionHistoriqueListe.SoumissionModifiee => Texte["ActionSoumissionModifiee", "Soumission modifiée"],
            TypesActionHistoriqueListe.ReussiteSupprimee => Texte["ActionReussiteSupprimee", "Réussite supprimée"],
            TypesActionHistoriqueListe.MembreAjoute => Texte["ActionMembreAjoute", "Membre ajouté"],
            TypesActionHistoriqueListe.RoleModifie => Texte["ActionRoleModifie", "Rôle modifié"],
            TypesActionHistoriqueListe.MembreRetire => Texte["ActionMembreRetire", "Membre retiré"],
            TypesActionHistoriqueListe.DemandeQuotaNiveaux => Texte["ActionDemandeQuota", "Demande de quota"],
            TypesActionHistoriqueListe.ActionAnnulee => Texte["ActionAnnulation", "Annulation"],
            _ => typeAction
        };

        private string DescriptionAction(HistoriqueListe historique)
        {
            if (Texte.CodeLangue == "fr") return historique.Description;

            return historique.TypeAction switch
            {
                TypesActionHistoriqueListe.ListeCreee => Texte["DescriptionListeCreee", "La liste a été créée."],
                TypesActionHistoriqueListe.ListeModifiee => Texte["DescriptionListeModifiee", "Les paramètres de la liste ont été modifiés."],
                TypesActionHistoriqueListe.ListeSupprimee => Texte["DescriptionListeSupprimee", "La liste a été supprimée."],
                TypesActionHistoriqueListe.ListeRestauree => Texte["DescriptionListeRestauree", "La liste a été restaurée."],
                TypesActionHistoriqueListe.NiveauCree => Texte["DescriptionNiveauCree", "Un niveau a été ajouté à la liste."],
                TypesActionHistoriqueListe.NiveauModifie => Texte["DescriptionNiveauModifie", "Un niveau a été modifié."],
                TypesActionHistoriqueListe.NiveauSupprime => Texte["DescriptionNiveauSupprime", "Un niveau a été supprimé."],
                TypesActionHistoriqueListe.ClassementModifie => Texte["DescriptionClassementModifie", "Le classement des niveaux a été modifié."],
                TypesActionHistoriqueListe.SoumissionAcceptee => Texte["DescriptionSoumissionAcceptee", "Une soumission a été acceptée."],
                TypesActionHistoriqueListe.SoumissionRefusee => Texte["DescriptionSoumissionRefusee", "Une soumission a été refusée."],
                TypesActionHistoriqueListe.SoumissionCreee => Texte["DescriptionSoumissionCreee", "Une soumission a été créée."],
                TypesActionHistoriqueListe.SoumissionModifiee => Texte["DescriptionSoumissionModifiee", "Une soumission a été modifiée."],
                TypesActionHistoriqueListe.ReussiteSupprimee => Texte["DescriptionReussiteSupprimee", "Une réussite validée a été supprimée."],
                TypesActionHistoriqueListe.MembreAjoute => Texte["DescriptionMembreAjoute", "Un membre a été ajouté à la liste."],
                TypesActionHistoriqueListe.RoleModifie => Texte["DescriptionRoleModifie", "Le rôle d'un membre a été modifié."],
                TypesActionHistoriqueListe.MembreRetire => Texte["DescriptionMembreRetire", "Un membre a été retiré de la liste."],
                TypesActionHistoriqueListe.DemandeQuotaNiveaux => Texte["DescriptionDemandeQuota", "Une augmentation de la limite de niveaux a été demandée."],
                TypesActionHistoriqueListe.ActionAnnulee => Texte["DescriptionActionAnnulee", "Une action précédente a été annulée."],
                _ => historique.Description
            };
        }

        private string TraduireMessageHistorique(string cle) => cle switch
        {
            "ListeIntrouvable" => Texte[cle, "Liste introuvable."],
            "SuppressionListeReserveeProprietaire" => Texte[cle, "Seul le propriétaire peut supprimer cette liste."],
            "ListeDejaSupprimee" => Texte[cle, "Cette liste est déjà supprimée."],
            "ListeSupprimeeSucces" => Texte[cle, "La liste a été supprimée et peut être restaurée."],
            "RestaurationListeReserveeProprietaire" => Texte[cle, "Seul le propriétaire peut restaurer cette liste."],
            "ListeDejaActive" => Texte[cle, "Cette liste est déjà active."],
            "ListeRestaureeSucces" => Texte[cle, "La liste a été restaurée."],
            "ActionIntrouvable" => Texte[cle, "Action introuvable."],
            "HistoriqueListeSupprimeeReserveProprietaire" => Texte[cle, "Seul le propriétaire peut modifier l'historique d'une liste supprimée."],
            "PermissionAnnulerActionRefusee" => Texte[cle, "Vous n'avez pas la permission d'annuler cette action."],
            "ActionNonAnnulable" => Texte[cle, "Cette action ne peut pas être annulée."],
            "ActionDejaAnnulee" => Texte[cle, "Cette action a déjà été annulée."],
            "ActionRecenteDabord" => Texte[cle, "Annulez d'abord les actions plus récentes qui concernent le même élément."],
            "ActionAnnuleeSucces" => Texte[cle, "L'action a été annulée."],
            "SoumissionPlusEnAttente" => Texte[cle, "Cette soumission n'est plus en attente."],
            "ReussiteExisteDeja" => Texte[cle, "Cette réussite existe déjà et ne peut pas être restaurée."],
            "TypeActionNonPrisEnCharge" => Texte[cle, "Le type de cette action n'est pas pris en charge pour l'annulation."],
            "NiveauExistePlus" => Texte[cle, "Le niveau n'existe plus."],
            "NiveauDependancesNonRetirable" => Texte[cle, "Ce niveau possède désormais des réussites ou des soumissions et ne peut plus être retiré automatiquement."],
            "ClassementChangeNonRestaurable" => Texte[cle, "Le classement a changé et ne peut plus être restauré automatiquement."],
            "SoumissionUtilisateurAbsentNonRestaurable" => Texte[cle, "La soumission n'était pas reliée à un utilisateur et ne peut pas être restaurée automatiquement."],
            _ => cle
        };
    }
}
