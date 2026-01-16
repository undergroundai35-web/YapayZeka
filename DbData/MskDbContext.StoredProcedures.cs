using Microsoft.EntityFrameworkCore;
using UniCP.Models.MsK.SpModels;

namespace UniCP.DbData
{
    public partial class MskDbContext
    {
        public List<Sp_Deneme> GetDeneme(int p1, string p2)
        {
            return Set<Sp_Deneme>()
                .FromSqlRaw("EXEC DENEME @P1 = @p0, @P2 = @p1", p1, p2)
                .AsNoTracking()
                .ToList();
        }
        public List<Fn_Deneme> Fon_Deneme(int p)
        {
            return Set<Fn_Deneme>()
               .FromSqlRaw("SELECT * FROM dbo.FN_DENEME({0})", p)
               .AsNoTracking()
               .ToList();
        }

        public List<SSP_N4B_TICKETLARI> SP_N4B_TICKETLARI(int ORTAKPROJEKOD, string EMAIL ,int BILDIRIMTIP)
        {
            return Set<SSP_N4B_TICKETLARI>()
                .FromSqlRaw("EXEC SSP_N4B_TICKETLARI @ORTAKPROJEKOD = @P0, @EMAIL = @P1,@BILDIRIMTIP = @P2", ORTAKPROJEKOD, EMAIL, BILDIRIMTIP)
                .AsNoTracking()
                .ToList();
        }
        public List<SSP_N4B_TICKET_DURUM_SAYILARI> SP_N4B_TICKET_DURUM_SAYILARI(int ORTAKPROJEKOD, string EMAIL,DateTime TARIH )
        {
            return Set<SSP_N4B_TICKET_DURUM_SAYILARI>()
                .FromSqlRaw("EXEC SSP_N4B_TICKET_DURUM_SAYILARI @ORTAKPROJEKOD = @P0,@EMAIL = @P1,@TARIH = @p2", ORTAKPROJEKOD, EMAIL , TARIH)
                .AsNoTracking()
                .ToList();
        }

        public List<SSP_N4B_SLA_ORAN> SP_N4B_SLA_ORAN(int ORTAKPROJEKOD)
        {
            return Set<SSP_N4B_SLA_ORAN>()
                .FromSqlRaw("EXEC SSP_N4B_SLA_ORAN @ORTAKPROJEKOD = @P0", ORTAKPROJEKOD)
                .AsNoTracking()
                .ToList();
        }

        public List<SSP_TFS_GELISTIRME> SP_TFS_GELISTIRME(int LNGORTAKFIRMAKOD)
        {
            return Set<SSP_TFS_GELISTIRME>()
                .FromSqlRaw("EXEC SSP_TFS_GELISTIRME @ORTAKPROJEKOD = @P0", LNGORTAKFIRMAKOD)
                .AsNoTracking()
                .ToList();
        }

        public List<SSP_VARUNA_SIPARIS> SP_VARUNA_SIPARIS(int LNGORTAKFIRMAKOD)
        {
            return Set<SSP_VARUNA_SIPARIS>()
                .FromSqlRaw("EXEC SSP_VARUNA_SIPARIS @ORTAKPROJEKOD = @P0", LNGORTAKFIRMAKOD)
                .AsNoTracking()
                .ToList();
        }
        public List<SSP_VARUNA_SIPARIS_DETAY> SP_VARUNA_SIPARIS_DETAY(string ORDERID)
        {
            return Set<SSP_VARUNA_SIPARIS_DETAY>()
                .FromSqlRaw("EXEC SSP_VARUNA_SIPARIS_DETAY @ORDERID = @P0", ORDERID)
                .AsNoTracking()
                .ToList();
        }
         public List<SSP_VARUNA_CHART_DATA> SP_VARUNA_CHART_DATA(int LNGORTAKFIRMAKOD)
        {
            return Set<SSP_VARUNA_CHART_DATA>()
                .FromSqlRaw("EXEC SSP_VARUNA_CHART_DATA @ORTAKPROJEKOD = @P0", LNGORTAKFIRMAKOD)
                .AsNoTracking()
                .ToList();
        }

    }
}
