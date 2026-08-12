namespace MyDemonList.Web.Entities
{
    public class DiscordAccount
    {
        public int Id { get; set; }

        public string DiscordId { get; set; } = default!;

        public string? DiscordUsername { get; set; }
        public string? DiscordDisplayName { get; set; }
        public string? AvatarHash { get; set; }

        public int UtilisateurId { get; set; }
        public Utilisateur Utilisateur { get; set; } = default!;
    }
}
