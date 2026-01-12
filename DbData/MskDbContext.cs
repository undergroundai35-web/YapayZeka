using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using UniCP.Models.MsK;

namespace UniCP.DbData;

public partial class MskDbContext : DbContext
{
    public MskDbContext()
    {
    }

    public MskDbContext(DbContextOptions<MskDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AspNetRole> AspNetRoles { get; set; }

    public virtual DbSet<AspNetRoleClaim> AspNetRoleClaims { get; set; }

    public virtual DbSet<AspNetUser> AspNetUsers { get; set; }

    public virtual DbSet<AspNetUserClaim> AspNetUserClaims { get; set; }

    public virtual DbSet<AspNetUserLogin> AspNetUserLogins { get; set; }

    public virtual DbSet<AspNetUserToken> AspNetUserTokens { get; set; }

    public virtual DbSet<PARAMETRELER> PARAMETRELERs { get; set; }

    public virtual DbSet<TBL_KULLANICI> TBL_KULLANICIs { get; set; }

    public virtual DbSet<TBL_N4BISSSEFILE> TBL_N4BISSSEFILEs { get; set; }

    public virtual DbSet<TBL_N4BISSUE> TBL_N4BISSUEs { get; set; }

    public virtual DbSet<TBL_TALEP> TBL_TALEPs { get; set; }

    public virtual DbSet<TBL_TALEP_AKISDURUMLARI> TBL_TALEP_AKISDURUMLARIs { get; set; }

    public virtual DbSet<TBL_TALEP_AKIS_LOG> TBL_TALEP_AKIS_LOGs { get; set; }

    public virtual DbSet<TBL_TALEP_FILE> TBL_TALEP_FILEs { get; set; }

    public virtual DbSet<TBL_TALEP_NOTLAR> TBL_TALEP_NOTLARs { get; set; }

    public virtual DbSet<VIEW_N4BISSUE> VIEW_N4BISSUEs { get; set; }

    public virtual DbSet<VIEW_N4BISSUESLIFECYCLE> VIEW_N4BISSUESLIFECYCLEs { get; set; }

    public virtual DbSet<WWDENEME> WWDENEMEs { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Name=ConnectionStrings:MsKConnection");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AspNetRole>(entity =>
        {
            entity.HasIndex(e => e.NormalizedName, "RoleNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedName] IS NOT NULL)");
        });

        modelBuilder.Entity<AspNetUser>(entity =>
        {
            entity.HasIndex(e => e.NormalizedUserName, "UserNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedUserName] IS NOT NULL)");

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "AspNetUserRole",
                    r => r.HasOne<AspNetRole>().WithMany().HasForeignKey("RoleId"),
                    l => l.HasOne<AspNetUser>().WithMany().HasForeignKey("UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.ToTable("AspNetUserRoles");
                        j.HasIndex(new[] { "RoleId" }, "IX_AspNetUserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<PARAMETRELER>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_dbo.Parametrelers");
        });

        modelBuilder.Entity<TBL_N4BISSSEFILE>(entity =>
        {
            entity.HasOne(d => d.LNGTBLISSUEKODNavigation).WithMany(p => p.TBL_N4BISSSEFILEs).HasConstraintName("FK_TBL_N4BISSSEFILES_TBL_N4BISSUES");
        });

        modelBuilder.Entity<TBL_TALEP>(entity =>
        {
            entity.Property(e => e.BYTDURUM).IsFixedLength();
        });

        modelBuilder.Entity<TBL_TALEP_AKIS_LOG>(entity =>
        {
            entity.HasOne(d => d.LNGDURUMKODNavigation).WithMany(p => p.TBL_TALEP_AKIS_LOGs).HasConstraintName("FK_TBL_TALEP_AKIS_LOG_TBL_TALEP_AKISDURUMLARI");
        });

        modelBuilder.Entity<TBL_TALEP_FILE>(entity =>
        {
            entity.HasOne(d => d.LNGTALEPKODNavigation).WithMany(p => p.TBL_TALEP_FILEs).HasConstraintName("FK_TBL_TALEP_FILES_TBL_TALEP");
        });

        modelBuilder.Entity<TBL_TALEP_NOTLAR>(entity =>
        {
            entity.HasOne(d => d.LNGTALEPKODNavigation).WithMany(p => p.TBL_TALEP_NOTLARs).HasConstraintName("FK_TBL_TALEP_NOTLAR_TBL_TALEP");
        });

        modelBuilder.Entity<VIEW_N4BISSUE>(entity =>
        {
            entity.ToView("VIEW_N4BISSUES");
        });

        modelBuilder.Entity<VIEW_N4BISSUESLIFECYCLE>(entity =>
        {
            entity.ToView("VIEW_N4BISSUESLIFECYCLE");
        });

        modelBuilder.Entity<WWDENEME>(entity =>
        {
            entity.ToView("WWDENEME");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
