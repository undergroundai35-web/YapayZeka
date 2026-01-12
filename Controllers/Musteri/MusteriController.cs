using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UniCP.DbData;
using UniCP.Models;
using UniCP.Models.Kullanici;

namespace UniCP.Controllers.Musteri
{
    [Authorize]
    public class MusteriController : Controller
    {
        private readonly MskDbContext _mskDb;

        public MusteriController(MskDbContext mskDb)
        {
            _mskDb = mskDb;
        }

        public IActionResult Index()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");

            int userId = int.Parse(userIdStr);
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            
            if (kullanici == null) return RedirectToAction("Login", "Account");

            string email = User.FindFirstValue(ClaimTypes.Email) ?? "test@univera.com.tr";
            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;

            DateTime trh = new DateTime(2025, 1, 1);
            var stats = _mskDb.SP_N4B_TICKET_DURUM_SAYILARI(Convert.ToInt16(firmaKod), email, trh).ToList();
            var slaData = _mskDb.SP_N4B_SLA_ORAN(Convert.ToInt16(firmaKod)).ToList();
            
            // Fetch open tickets to calculate exact "Kritik" (Escalated or Overdue) count
            var openTickets = _mskDb.SP_N4B_TICKETLARI(Convert.ToInt16(firmaKod), email, 3).ToList();

            ViewBag.OpenTicketsCount = stats.Where(i => i.Durum.Contains("Açık", StringComparison.OrdinalIgnoreCase)).Select(i => i.Sayi).Sum();
            
            
            // Calculate Critical: Status contains "Eskale" OR SLA is negative
            ViewBag.EscalatedCount = openTickets.Count(i => 
                (i.Bildirim_Durumu?.Contains("Eskale", StringComparison.OrdinalIgnoreCase) ?? false) || 
                (i.SLA_YD_Cozum_Kalan_Sure ?? 0) < 0
            );

            // Fetch Active Development Requests (Logic from TaleplerController)
            var liveTfsRequests = _mskDb.SP_TFS_GELISTIRME(Convert.ToInt16(firmaKod));
            var startDate = new DateTime(2025, 1, 1);
            var openDevRequestsCount = liveTfsRequests
                .Count(tfs => !string.Equals(tfs.MADDEDURUM, "CLOSED", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "CANCEL", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "CANCELED", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "RESOLVED", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "SEND BACK", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "SEND-BACK", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "REJECTED", StringComparison.OrdinalIgnoreCase) &&
                              tfs.ACILMATARIHI >= startDate);

            ViewBag.OpenDevRequestsCount = openDevRequestsCount;

            ViewBag.TotalSla = slaData.FirstOrDefault()?.ORAN ?? 100;
            ViewBag.SlaHistory = slaData;
            
            ViewBag.Kullanici = kullanici;
            return View();
        }

        public IActionResult N4Bgrafik()
        {
            // TEMPORARY MOCK CHART DATA
            List<BildirimChartVm> bld = new List<BildirimChartVm>
            {
                new BildirimChartVm { Durum = "Açık/Email", Sayi = 5 },
                new BildirimChartVm { Durum = "Kapatıldı/Telefon", Sayi = 8 },
                new BildirimChartVm { Durum = "İptal Edildi/Email", Sayi = 2 }
            };

            return PartialView("N4Bgrafik", bld.ToList());
        }
    }
}
