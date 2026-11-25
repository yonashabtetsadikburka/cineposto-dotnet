using Microsoft.EntityFrameworkCore;

namespace CinePosto.Infrastructure.Persistence;

/// <summary>Entity Framework context holding cinemas, films and showings.</summary>
public sealed class CinePostoDbContext : DbContext
{
    /// <summary>Creates a context with the given options.</summary>
    public CinePostoDbContext(DbContextOptions<CinePostoDbContext> options)
        : base(options)
    {
    }

    internal DbSet<CinemaEntity> Cinemas => Set<CinemaEntity>();

    internal DbSet<FilmEntity> Films => Set<FilmEntity>();

    internal DbSet<ShowingEntity> Showings => Set<ShowingEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CinemaEntity>(entity =>
        {
            entity.ToTable("cinemas");
            entity.HasKey(c => c.Slug);
            entity.Property(c => c.Slug).HasMaxLength(100);
            entity.Property(c => c.Name).HasMaxLength(200);
            entity.Property(c => c.City).HasMaxLength(100);
            entity.Property(c => c.Address).HasMaxLength(300);
            entity.Property(c => c.Region).HasMaxLength(100);
            entity.Property(c => c.Website).HasMaxLength(500);
        });

        modelBuilder.Entity<FilmEntity>(entity =>
        {
            entity.ToTable("films");
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Title).HasMaxLength(300);
            entity.Property(f => f.TitleNormalized).HasMaxLength(300);
            entity.Property(f => f.TitleKey).HasMaxLength(300);
            entity.HasIndex(f => f.TitleKey).IsUnique();
        });

        modelBuilder.Entity<ShowingEntity>(entity =>
        {
            entity.ToTable("showings");
            entity.HasKey(s => s.Id);
            entity.HasOne(s => s.Film).WithMany().HasForeignKey(s => s.FilmId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(s => s.Cinema).WithMany().HasForeignKey(s => s.CinemaSlug).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(s => new { s.CinemaSlug, s.Date });
            entity.HasIndex(s => s.Date);
            entity.HasIndex(s => s.FilmId);
        });
    }
}
