using Microsoft.EntityFrameworkCore;
using UniCP.Models.MsK.SpModels;

namespace UniCP.DbData
{
    public partial class MskDbContext
    {
        partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Sp_Deneme>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });

            modelBuilder.Entity<Fn_Deneme>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);   // EF bu nesnenin tablo olmadığını anlasın
            });

            modelBuilder.Entity<SSP_N4B_TICKETLARI>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });

            modelBuilder.Entity<SSP_N4B_TICKET_DURUM_SAYILARI>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });
            modelBuilder.Entity<SSP_N4B_SLA_ORAN>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });

            modelBuilder.Entity<SSP_TFS_GELISTIRME>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });
            modelBuilder.Entity<SSP_VARUNA_SIPARIS>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });
            modelBuilder.Entity<SSP_VARUNA_SIPARIS_DETAY>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });
             modelBuilder.Entity<SSP_VARUNA_CHART_DATA>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });
        }
    }
}
