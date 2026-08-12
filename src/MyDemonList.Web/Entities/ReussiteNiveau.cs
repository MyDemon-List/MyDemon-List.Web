using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public class ReussiteNiveau
    {
        [ForeignKey("UtilisateurId")]
        public int UtilisateurId { get; set; }
        public virtual Utilisateur Utilisateur { get; set; }

        [ForeignKey("NiveauId")]
        public int NiveauId { get; set; }
        public virtual Niveau Niveau { get; set; }

        public string Video { get; set; }
        public string Statut { get; set; }
    }
}
