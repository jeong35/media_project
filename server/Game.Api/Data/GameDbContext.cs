using Microsoft.EntityFrameworkCore;

namespace Game.Api.Data;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<GameSession> Sessions => Set<GameSession>();
    public DbSet<GameEvent> GameEvents => Set<GameEvent>();
    public DbSet<NpcState> NpcStates => Set<NpcState>();
    public DbSet<TraitScore> TraitScores => Set<TraitScore>();
    public DbSet<CareerAffinity> CareerAffinities => Set<CareerAffinity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameEvent>().HasIndex(e => new { e.SessionId, e.Sequence }).IsUnique();
        modelBuilder.Entity<GameEvent>().HasIndex(e => new { e.SessionId, e.IdempotencyKey }).IsUnique();
        modelBuilder.Entity<NpcState>().HasKey(e => new { e.SessionId, e.NpcId });
        modelBuilder.Entity<TraitScore>().HasKey(e => new { e.SessionId, e.TraitId });
        modelBuilder.Entity<CareerAffinity>().HasKey(e => new { e.SessionId, e.AffinityId });
    }
}
