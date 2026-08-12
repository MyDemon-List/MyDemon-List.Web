using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public class Niveau
    {
        [Key]
        public int Id { get; set; }
        public string IdDuNiveauDansLeJeu { get; set; }
        public string Nom { get; set; }
        public string UrlVerification { get; set; }
        public int Duree { get; set; }
        public DateTime DateAjout { get; set; }

        [ForeignKey("VerifieurId")]
        public int VerifieurId { get; set; }
        public virtual Utilisateur Verifieur { get; set; }

        [ForeignKey("PublisherId")]
        public int PublisherId { get; set; }
        public virtual Utilisateur Publisher { get; set; }

        [ForeignKey("RatingId")]
        public int RatingId { get; set; }
        public virtual Difficulte Rating { get; set; }

        [ForeignKey("ListeId")]
        public int ListeId { get; set; }
        public virtual Liste Liste { get; set; }
    }
}
