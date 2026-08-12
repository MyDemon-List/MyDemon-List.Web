using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public class DemandeNiveauxSupplementaires
    {
        public int Id { get; set; }

        [ForeignKey("ListeId")]
        public int ListeId { get; set; }
        public virtual Liste Liste { get; set; }

        [ForeignKey("UtilisateurDemandeurId")]
        public int UtilisateurDemandeurId { get; set; }
        public virtual Utilisateur UtilisateurDemandeur { get; set; }

        public string Statut { get; set; } = "EnAttente";
        public DateTime DateDemande { get; set; } = DateTime.Now;
        public DateTime? DateTraitement { get; set; }
    }
}
