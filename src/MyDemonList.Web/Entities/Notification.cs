using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public static class TypesNotification
    {
        public const string Information = "Information";
        public const string SoumissionAcceptee = "SoumissionAcceptee";
        public const string SoumissionRefusee = "SoumissionRefusee";
        public const string QuotaAccepte = "QuotaAccepte";
        public const string QuotaRefuse = "QuotaRefuse";
        public const string FusionAcceptee = "FusionAcceptee";
        public const string FusionRefusee = "FusionRefusee";
        public const string RoleModifie = "RoleModifie";
    }

    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey(nameof(UtilisateurId))]
        public int UtilisateurId { get; set; }
        public virtual Utilisateur Utilisateur { get; set; } = default!;

        public string Type { get; set; } = TypesNotification.Information;
        public string Titre { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Lien { get; set; }
        public DateTime DateCreation { get; set; } = DateTime.Now;
        public DateTime? DateLecture { get; set; }
    }
}
