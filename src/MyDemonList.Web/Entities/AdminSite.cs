using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public class AdminSite
    {
        [ForeignKey("UtilisateurId")]
        public int UtilisateurId { get; set; }
        public virtual Utilisateur Utilisateur { get; set; }

        public DateTime DateAjout { get; set; } = DateTime.Now;
    }
}
