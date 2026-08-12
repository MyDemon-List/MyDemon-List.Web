using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public enum RoleListe
    {
        Administrateur = 1,
        EditeurNiveaux = 2,
        Moderateur = 3
    }

    public class MembreListe
    {
        [ForeignKey("ListeId")]
        public int ListeId { get; set; }
        public virtual Liste Liste { get; set; }

        [ForeignKey("UtilisateurId")]
        public int UtilisateurId { get; set; }
        public virtual Utilisateur Utilisateur { get; set; }

        public RoleListe Role { get; set; }
        public DateTime DateAjout { get; set; } = DateTime.Now;
    }
}
