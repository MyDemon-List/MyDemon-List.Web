using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public class Difficulte
    {
        [Key]
        public int Id { get; set; }
        public string Nom { get; set; }

        [NotMapped]
        public string ImageUrl => $"/Pictures/DemonsFaces/{Id}";
    }
}
