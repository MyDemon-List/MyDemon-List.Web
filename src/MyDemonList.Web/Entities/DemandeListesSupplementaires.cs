using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public class DemandeListesSupplementaires
    {
        public int Id { get; set; }

        [ForeignKey("UtilisateurId")]
        public int UtilisateurId { get; set; }
        public virtual Utilisateur Utilisateur { get; set; }

        public string Statut { get; set; } = "EnAttente";
        public DateTime DateDemande { get; set; } = DateTime.Now;
        public DateTime? DateTraitement { get; set; }
    }
}
