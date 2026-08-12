using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public class Classement
    {
        [Key]
        public int Id { get; set; }
        public int ClassementPosition { get; set; }
        public int Points { get; set; }

        [ForeignKey("NiveauId")]
        public int NiveauId { get; set; }
        public virtual Niveau Niveau { get; set; }

        [ForeignKey("ListeId")]
        public int ListeId { get; set; }
        public virtual Liste Liste { get; set; }
    }
}
