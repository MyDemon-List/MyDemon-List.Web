using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyDemonList.Web.Entities
{
    public enum RawFootageMode
    {
        None = 0,
        All = 1,
        FromTop = 2
    }

    public class Liste
    {
        [Key]
        public int Id { get; set; }
        public string Nom { get; set; }
        public string? Description { get; set; }
        public DateTime DateCreation { get; set; } = DateTime.Now;
        public bool EstPublique { get; set; } = true;
        public bool EstSupprimee { get; set; }
        public DateTime? DateSuppression { get; set; }
        public int? SupprimeeParUtilisateurId { get; set; }
        public string? DiscordServerUrl { get; set; }

        public RawFootageMode RawFootageMode { get; set; } = RawFootageMode.None;
        public int? RawFootageTopStart { get; set; }
        public bool VideoToujoursRequise { get; set; } = true;
        public int? VideoDifficulteMinimaleId { get; set; }
        public int? VideoTopStart { get; set; }

        [ForeignKey("UtilisateurId")]
        public int UtilisateurId { get; set; }
        public virtual Utilisateur Utilisateur { get; set; }
    }
}
