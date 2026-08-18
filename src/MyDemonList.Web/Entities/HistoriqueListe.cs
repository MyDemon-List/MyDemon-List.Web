using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public static class TypesActionHistoriqueListe
    {
        public const string ListeCreee = "ListeCreee";
        public const string ListeModifiee = "ListeModifiee";
        public const string ListeSupprimee = "ListeSupprimee";
        public const string ListeRestauree = "ListeRestauree";
        public const string NiveauCree = "NiveauCree";
        public const string NiveauModifie = "NiveauModifie";
        public const string NiveauSupprime = "NiveauSupprime";
        public const string ClassementModifie = "ClassementModifie";
        public const string SoumissionAcceptee = "SoumissionAcceptee";
        public const string SoumissionRefusee = "SoumissionRefusee";
        public const string SoumissionCreee = "SoumissionCreee";
        public const string SoumissionModifiee = "SoumissionModifiee";
        public const string ReussiteSupprimee = "ReussiteSupprimee";
        public const string MembreAjoute = "MembreAjoute";
        public const string RoleModifie = "RoleModifie";
        public const string MembreRetire = "MembreRetire";
        public const string DemandeQuotaNiveaux = "DemandeQuotaNiveaux";
        public const string ActionAnnulee = "ActionAnnulee";
    }

    public class HistoriqueListe
    {
        [Key]
        public int Id { get; set; }
        public int ListeId { get; set; }
        public int? UtilisateurId { get; set; }
        public string TypeAction { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? CleCible { get; set; }
        public string? DonneesAvant { get; set; }
        public string? DonneesApres { get; set; }
        public DateTime DateCreation { get; set; } = DateTime.Now;
        public bool PeutEtreAnnulee { get; set; }
        public DateTime? DateAnnulation { get; set; }
        public int? AnnuleeParUtilisateurId { get; set; }

        [ForeignKey(nameof(ListeId))]
        public Liste Liste { get; set; } = default!;

        [ForeignKey(nameof(UtilisateurId))]
        public Utilisateur? Utilisateur { get; set; }

        [ForeignKey(nameof(AnnuleeParUtilisateurId))]
        public Utilisateur? AnnuleeParUtilisateur { get; set; }
    }
}
