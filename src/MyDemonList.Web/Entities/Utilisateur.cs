using System.ComponentModel.DataAnnotations;

namespace MyDemonList.Web.Entities
{
    public class Utilisateur
    {
        [Key]
        public int Id { get; set; }
        public string Nom { get; set; }
        public string? CodePays { get; set; }
        public string? LanguePreferee { get; set; }

        public ICollection<DiscordAccount> ComptesDiscord { get; set; } = new List<DiscordAccount>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }
}
