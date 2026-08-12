namespace MyDemonList.Web.Services
{
    public class ListeSessionService
    {
        public int? ListeId { get; private set; }
        public string? ListeNom { get; private set; }
        public string? ListeDiscordUrl { get; private set; }

        public event Action? OnChanged;

        public void SetListe(int listeId, string? nom = null, string? discordUrl = null)
        {
            ListeId = listeId;
            ListeNom = nom;
            ListeDiscordUrl = discordUrl;
            OnChanged?.Invoke();
        }

        public void Clear()
        {
            ListeId = null;
            ListeNom = null;
            ListeDiscordUrl = null;
            OnChanged?.Invoke();
        }
    }
}
