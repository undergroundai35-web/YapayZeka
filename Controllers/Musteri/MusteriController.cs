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
    [Authorize(Roles = "Musteri,Admin")]
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
            
            // Multi-Company Logic for Type 3 (Univera)
            List<int> targetCompanies = new List<int>();

            if (kullanici.LNGKULLANICITIPI == 3)
            {
                 targetCompanies = _mskDb.TBL_KULLANICI_FIRMAs
                                     .Where(f => f.LNGKULLANICIKOD == kullanici.LNGKOD)
                                     .Select(f => f.LNGFIRMAKOD)
                                     .ToList();
            }
            
            // Fallback logic
            if (!targetCompanies.Any() && projectClaim != null && int.TryParse(projectClaim.Value, out int selectedProject))
            {
                firmaKod = selectedProject;
                targetCompanies.Add(firmaKod);
            }
            else if (!targetCompanies.Any())
            {
                 targetCompanies.Add(firmaKod);
            }

            DateTime trh = new DateTime(2025, 1, 1);
            var stats = new List<SSP_N4B_TICKET_DURUM_SAYILARI>();
            var slaData = new List<SSP_N4B_SLA_ORAN>();
            var openTickets = new List<SSP_N4B_TICKETLARI>();
            var liveTfsRequests = new List<SSP_TFS_GELISTIRME>();

            foreach (var currentFirmaKod in targetCompanies)
            {
                try 
                {
                    var s = _mskDb.SP_N4B_TICKET_DURUM_SAYILARI(Convert.ToInt16(currentFirmaKod), email, trh).ToList();
                    stats.AddRange(s);
                }
                catch (Exception ex) { Console.WriteLine($"[Dashboard] Stat Error: {ex.Message}"); }

                try 
                {
                    var sl = _mskDb.SP_N4B_SLA_ORAN(Convert.ToInt16(currentFirmaKod)).ToList();
                    slaData.AddRange(sl);
                }
                catch (Exception ex) { Console.WriteLine($"[Dashboard] SLA Error: {ex.Message}"); }

                try 
                {
                    var ot = _mskDb.SP_N4B_TICKETLARI(Convert.ToInt16(currentFirmaKod), email, 3).ToList();
                    openTickets.AddRange(ot);
                }
                catch (Exception ex) { Console.WriteLine($"[Dashboard] Tickets Error: {ex.Message}"); }

                try 
                {
                    var tfs = _mskDb.SP_TFS_GELISTIRME(Convert.ToInt16(currentFirmaKod));
                    liveTfsRequests.AddRange(tfs);
                }
                catch (Exception ex) { Console.WriteLine($"[Dashboard] TFS Error: {ex.Message}"); }
            }

            ViewBag.OpenTicketsCount = stats.Where(i => i.Durum.Contains("Açık", StringComparison.OrdinalIgnoreCase)).Select(i => i.Sayi).Sum();
            
            // Calculate Critical: Status contains "Eskale" OR SLA is negative
            ViewBag.EscalatedCount = openTickets.Count(i => 
                (i.Bildirim_Durumu?.Contains("Eskale", StringComparison.OrdinalIgnoreCase) ?? false) || 
                (i.SLA_YD_Cozum_Kalan_Sure ?? 0) < 0
            );
            
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

            // Simple handling for SLA Chart: Group by Month and Average? 
            // For now, if multiple companies, we might have multiple entries for same month.
            // Let's Group by DONEM and take Average ORAN for the chart.
            var aggregatedSla = slaData
                .GroupBy(x => x.DONEM)
                .Select(g => new SSP_N4B_SLA_ORAN 
                { 
                    DONEM = g.Key, 
                    ORAN = g.Average(x => x.ORAN),
                    YIL = g.First().YIL,
                    AY = g.First().AY
                })
                .OrderBy(x => x.YIL).ThenBy(x => x.AY)
                .ToList();

            // Fetch Portal Data to get actual statuses - Trusting TFS as source of truth for company
            var tfsIds = liveTfsRequests.Select(t => t.TFSNO).ToList();

            // Fetch Portal Data to get actual statuses
            // Include records linked to our filtered TFS items, even if missing local company code
            var portalRequests = _mskDb.TBL_TALEPs
                                    .Where(r => (r.LNGVARUNAKOD.HasValue && targetCompanies.Contains(r.LNGVARUNAKOD.Value)) 
                                             || (r.LNGTFSNO.HasValue && tfsIds.Contains(r.LNGTFSNO.Value)))
                                    .ToList();

            var requestIds = portalRequests.Select(r => (int?)r.LNGKOD).ToList();
            
            var allLogs = _mskDb.TBL_TALEP_AKIS_LOGs
                            .Where(l => requestIds.Contains(l.LNGTALEPKOD))
                            .Include(l => l.LNGDURUMKODNavigation)
                            .ToList();
            
            var logsMap = allLogs
                            .GroupBy(l => l.LNGTALEPKOD)
                            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.TRHDURUMBASLANGIC).ThenByDescending(l => l.LNGSIRA).FirstOrDefault());

            var tfsMap = liveTfsRequests.GroupBy(x => x.TFSNO).ToDictionary(g => g.Key, g => g.First());

            decimal pendingBudgetEffort = 0;

            foreach (var req in portalRequests)
            {
                // Determine Status
                var latestLog = logsMap.ContainsKey(req.LNGKOD) ? logsMap[req.LNGKOD] : null;

                var currentStatus = "Analiz";
                if (latestLog?.LNGDURUMKODNavigation != null)
                {
                    currentStatus = latestLog.LNGDURUMKODNavigation.TXTDURUMADI ?? "Analiz";
                }

                if (string.Equals(currentStatus, "Bütçe Onayı", StringComparison.OrdinalIgnoreCase))
                {
                    decimal effort = 0;
                    
                    // Priority 1: TFS Effort
                    if (req.LNGTFSNO.HasValue && req.LNGTFSNO > 0 && tfsMap.ContainsKey(req.LNGTFSNO.Value))
                    {
                        effort = tfsMap[req.LNGTFSNO.Value].YAZILIM_TOPLAMAG ?? 0;
                    }
                    else
                    {
                        // Priority 2: Portal Effort
                        effort = req.DEC_EFOR ?? 0;
                    }

                    pendingBudgetEffort += effort;
                }
            }

            ViewBag.PendingBudgetEffort = pendingBudgetEffort;

            ViewBag.TotalSla = aggregatedSla.LastOrDefault()?.ORAN ?? 100;
            ViewBag.SlaHistory = aggregatedSla;
            
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
