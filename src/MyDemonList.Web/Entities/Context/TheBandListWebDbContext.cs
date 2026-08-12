using Microsoft.EntityFrameworkCore;

namespace MyDemonList.Web.Entities.Context
{
    public class MyDemonListWebDbContext : DbContext
    {
        public MyDemonListWebDbContext(DbContextOptions<MyDemonListWebDbContext> options)
        : base(options)
        { }

        public DbSet<Utilisateur> Utilisateurs { get; set; }
        public DbSet<Niveau> Niveaux { get; set; }
        public DbSet<Difficulte> Difficultes { get; set; }
        public DbSet<Liste> Listes { get; set; }
        public DbSet<MembreListe> MembresListe { get; set; }
        public DbSet<Classement> Classements { get; set; }
        public DbSet<CreateurNiveau> CreateursNiveaux { get; set; }
        public DbSet<ReussiteNiveau> ReussitesNiveaux { get; set; }
        public DbSet<SoumissionNiveau> SoumissionsNiveaux { get; set; }
        public DbSet<FusionUtilisateur> FusionsUtilisateurs { get; set; }
        public DbSet<DiscordAccount> DiscordAccounts { get; set; } = default!;
        public DbSet<AdminSite> AdminsSite { get; set; }
        public DbSet<DemandeNiveauxSupplementaires> DemandesNiveauxSupplementaires { get; set; }
        public DbSet<DemandeListesSupplementaires> DemandesListesSupplementaires { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CreateurNiveau>()
                .HasKey(cn => new { cn.CreateurId, cn.NiveauId });

            modelBuilder.Entity<ReussiteNiveau>()
                .HasKey(rn => new { rn.UtilisateurId, rn.NiveauId });

            modelBuilder.Entity<MembreListe>()
                .HasKey(m => new { m.ListeId, m.UtilisateurId });

            modelBuilder.Entity<AdminSite>()
                .HasKey(a => a.UtilisateurId);

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Utilisateur>(e =>
            {
                e.Property(x => x.Nom).HasMaxLength(128).IsRequired();
                e.Property(x => x.CodePays).HasMaxLength(2);
                e.Property(x => x.LanguePreferee).HasMaxLength(2);
                e.ToTable(t => t.HasCheckConstraint(
                    "CK_Utilisateurs_LanguePreferee",
                    "\"LanguePreferee\" IS NULL OR \"LanguePreferee\" IN ('fr', 'en', 'es')"));
                e.HasIndex(x => x.Nom).IsUnique();
            });

            modelBuilder.Entity<DiscordAccount>(e =>
            {
                e.HasIndex(x => x.DiscordId).IsUnique();
                e.Property(x => x.DiscordId).HasMaxLength(50).IsRequired();
                e.Property(x => x.DiscordUsername).HasMaxLength(100);
                e.Property(x => x.DiscordDisplayName).HasMaxLength(100);
                e.Property(x => x.AvatarHash).HasMaxLength(100);

                e.HasOne(x => x.Utilisateur)
                 .WithMany(u => u.ComptesDiscord)
                 .HasForeignKey(x => x.UtilisateurId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Classement>(e =>
            {
                e.HasIndex(x => new { x.ListeId, x.ClassementPosition }).IsUnique();
            });

            modelBuilder.Entity<Liste>(e =>
            {
                e.HasOne(x => x.Utilisateur)
                 .WithMany()
                 .HasForeignKey(x => x.UtilisateurId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreateurNiveau>(e =>
            {
                e.HasOne(x => x.Createur)
                 .WithMany()
                 .HasForeignKey(x => x.CreateurId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<MembreListe>(e =>
            {
                e.Property(x => x.DateAjout).HasDefaultValueSql("now()");
                e.HasCheckConstraint("CK_MembresListe_Role", "\"Role\" IN (1, 2, 3)");

                e.HasOne(x => x.Liste)
                 .WithMany()
                 .HasForeignKey(x => x.ListeId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Utilisateur)
                 .WithMany()
                 .HasForeignKey(x => x.UtilisateurId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<FusionUtilisateur>(e =>
            {
                e.Property(x => x.DateDemande).HasDefaultValueSql("now()");
                e.HasCheckConstraint("CK_FusionsUtilisateurs_Statut", "\"Statut\" IN ('EnAttente', 'Validee', 'Refusee')");

                e.HasOne(x => x.UtilisateurCible)
                 .WithMany()
                 .HasForeignKey(x => x.UtilisateurCibleId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.UtilisateurDemandeur)
                 .WithMany()
                 .HasForeignKey(x => x.UtilisateurDemandeurId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Niveau>(e =>
            {
                e.Property(x => x.DateAjout).HasDefaultValueSql("now()");
                e.HasIndex(x => new { x.ListeId, x.IdDuNiveauDansLeJeu })
                    .IsUnique()
                    .HasDatabaseName("IX_Niveaux_Liste_IdJeu");

                e.HasOne(x => x.Publisher)
                 .WithMany()
                 .HasForeignKey(x => x.PublisherId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Verifieur)
                 .WithMany()
                 .HasForeignKey(x => x.VerifieurId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ReussiteNiveau>(e =>
            {
                e.HasCheckConstraint("CK_ReussitesNiveaux_Statut", "\"Statut\" IN ('EnAttente', 'Validee', 'Refusee')");

                e.HasOne(x => x.Utilisateur)
                 .WithMany()
                 .HasForeignKey(x => x.UtilisateurId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SoumissionNiveau>(e =>
            {
                e.Property(x => x.DateSoumission).HasDefaultValueSql("now()");

                e.HasOne(x => x.Utilisateur)
                 .WithMany()
                 .HasForeignKey(x => x.UtilisateurId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Notification>(e =>
            {
                e.Property(x => x.Type).HasMaxLength(50).IsRequired();
                e.Property(x => x.Titre).HasMaxLength(160).IsRequired();
                e.Property(x => x.Message).HasMaxLength(2000).IsRequired();
                e.Property(x => x.Lien).HasMaxLength(500);
                e.Property(x => x.DateCreation).HasDefaultValueSql("now()");
                e.HasIndex(x => new { x.UtilisateurId, x.DateCreation });
                e.HasIndex(x => new { x.UtilisateurId, x.DateLecture });

                e.HasOne(x => x.Utilisateur)
                 .WithMany(u => u.Notifications)
                 .HasForeignKey(x => x.UtilisateurId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AdminSite>(e =>
            {
                e.Property(x => x.DateAjout).HasDefaultValueSql("now()");

                e.HasOne(x => x.Utilisateur)
                 .WithMany()
                 .HasForeignKey(x => x.UtilisateurId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<DemandeNiveauxSupplementaires>(e =>
            {
                e.Property(x => x.DateDemande).HasDefaultValueSql("now()");
                e.HasCheckConstraint("CK_DemandesNiveauxSupplementaires_Statut", "\"Statut\" IN ('EnAttente', 'Validee', 'Refusee')");

                e.HasOne(x => x.Liste)
                 .WithMany()
                 .HasForeignKey(x => x.ListeId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.UtilisateurDemandeur)
                 .WithMany()
                 .HasForeignKey(x => x.UtilisateurDemandeurId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<DemandeListesSupplementaires>(e =>
            {
                e.Property(x => x.DateDemande).HasDefaultValueSql("now()");
                e.HasCheckConstraint("CK_DemandesListesSupplementaires_Statut", "\"Statut\" IN ('EnAttente', 'Validee', 'Refusee')");

                e.HasOne(x => x.Utilisateur)
                 .WithMany()
                 .HasForeignKey(x => x.UtilisateurId)
                 .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
