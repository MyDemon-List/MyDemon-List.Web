namespace MyDemonList.Web.Entities
{
    public class FusionUtilisateur
    {
        public int Id { get; set; }

        public int UtilisateurDemandeurId { get; set; }
        public Utilisateur UtilisateurDemandeur { get; set; }

        public int UtilisateurCibleId { get; set; }
        public Utilisateur UtilisateurCible { get; set; }

        public string NomConserve { get; set; } = string.Empty;

        public string? Motif { get; set; }

        public string Statut { get; set; } = "EnAttente";

        public DateTime DateDemande { get; set; } = DateTime.UtcNow;
        public DateTime? DateTraitement { get; set; }
    }
}
