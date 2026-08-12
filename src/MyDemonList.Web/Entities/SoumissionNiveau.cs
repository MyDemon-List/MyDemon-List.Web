using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public class SoumissionNiveau
    {
        [Key]
        public int IdSoumission { get; set; }

        [ForeignKey("NiveauId")]
        public int NiveauId { get; set; }
        public virtual Niveau Niveau { get; set; }

        [ForeignKey(nameof(UtilisateurId))]
        public int? UtilisateurId { get; set; }
        public virtual Utilisateur? Utilisateur { get; set; }

        public string NomUtilisateur { get; set; }
        public string UrlVideo { get; set; }
        public string? RawFootageUrl { get; set; }
        public DateTime DateSoumission { get; set; } = DateTime.Now;
    }
}
