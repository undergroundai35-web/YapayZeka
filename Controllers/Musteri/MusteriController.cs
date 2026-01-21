using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UniCP.DbData;
using UniCP.Models;
using UniCP.Models.Kullanici;
using UniCP.Models.MsK.SpModels;
using Microsoft.EntityFrameworkCore;

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
            
            // Default to user's company, but override if "ProjectCode" claim exists (from Login selection)
            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;
            var projectClaim = User.FindFirst("ProjectCode");
            if (projectClaim != null && int.TryParse(projectClaim.Value, out int selectedProjectCode))
            {
                firmaKod = selectedProjectCode;
            }

            DateTime trh = new DateTime(2025, 1, 1);
            var stats = new List<SSP_N4B_TICKET_DURUM_SAYILARI>();
            var slaData = new List<SSP_N4B_SLA_ORAN>();
            var openTickets = new List<SSP_N4B_TICKETLARI>();
            var liveTfsRequests = new List<SSP_TFS_GELISTIRME>();

            try 
            {
                stats = _mskDb.SP_N4B_TICKET_DURUM_SAYILARI(Convert.ToInt16(firmaKod), email, trh).ToList();
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[Dashboard] Stat Error (Firma: {firmaKod}): {ex.Message}"); 
            }

            try 
            {
                slaData = _mskDb.SP_N4B_SLA_ORAN(Convert.ToInt16(firmaKod)).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dashboard] SLA Error (Firma: {firmaKod}): {ex.Message}");
            }

            try 
            {
                // Fetch open tickets to calculate exact "Kritik" (Escalated or Overdue) count
                openTickets = _mskDb.SP_N4B_TICKETLARI(Convert.ToInt16(firmaKod), email, 3).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dashboard] Tickets Error (Firma: {firmaKod}): {ex.Message}");
            }

            ViewBag.OpenTicketsCount = stats.Where(i => i.Durum.Contains("Açık", StringComparison.OrdinalIgnoreCase)).Select(i => i.Sayi).Sum();
            
            
            // Calculate Critical: Status contains "Eskale" OR SLA is negative
            ViewBag.EscalatedCount = openTickets.Count(i => 
                (i.Bildirim_Durumu?.Contains("Eskale", StringComparison.OrdinalIgnoreCase) ?? false) || 
                (i.SLA_YD_Cozum_Kalan_Sure ?? 0) < 0
            );

            // Fetch Active Development Requests (Logic from TaleplerController)
            try 
            {
                liveTfsRequests = _mskDb.SP_TFS_GELISTIRME(Convert.ToInt16(firmaKod));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dashboard] TFS Error (Firma: {firmaKod}): {ex.Message}");
            }
            
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

            // Fetch Portal Data to get actual statuses - Trusting TFS as source of truth for company
            var portalRequests = _mskDb.TBL_TALEPs.Where(r => r.LNGTFSNO > 0).ToList();
            var portalMap = portalRequests.GroupBy(r => r.LNGTFSNO.Value)
                                          .ToDictionary(g => g.Key, g => g.First());
            decimal pendingBudgetEffort = 0;

            foreach (var tfs in liveTfsRequests)
            {
                var portalRecord = portalMap.ContainsKey(tfs.TFSNO) ? portalMap[tfs.TFSNO] : null;

                if (portalRecord != null)
                {
                    var latestLog = _mskDb.TBL_TALEP_AKIS_LOGs
                        .Where(l => l.LNGTALEPKOD == portalRecord.LNGKOD)
                        .OrderByDescending(l => l.TRHDURUMBASLANGIC)
                        .Include(l => l.LNGDURUMKODNavigation)
                        .FirstOrDefault();

                    if (latestLog?.LNGDURUMKODNavigation != null)
                    {
                        var currentStatus = latestLog.LNGDURUMKODNavigation.TXTDURUMADI ?? "";
                        if (currentStatus.Contains("Bütçe", StringComparison.OrdinalIgnoreCase))
                        {
                            pendingBudgetEffort += tfs.YAZILIM_TOPLAMAG ?? 0;
                        }
                    }
                }
            }

            ViewBag.PendingBudgetEffort = (int)Math.Round(pendingBudgetEffort);

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
