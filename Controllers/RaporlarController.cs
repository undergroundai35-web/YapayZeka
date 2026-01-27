using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using UniCP.DbData;
using UniCP.Models.Talepler;
using UniCP.Models.MsK.SpModels;
using UniCP.Models.MsK;
using Microsoft.EntityFrameworkCore;
using System.IO;
using Microsoft.Extensions.Caching.Memory;

namespace UniCP.Controllers
{
    [Authorize(Roles = "Raporlar,Admin")]
    public class RaporlarController : Controller
    {
        private readonly MskDbContext _mskDb;
        private readonly UniCP.Models.IEmailService _emailService;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

        public RaporlarController(MskDbContext mskDb, UniCP.Models.IEmailService emailService, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
        {
            _mskDb = mskDb;
            _emailService = emailService;
            _cache = cache;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Gelistirme(string period = "1m", DateTime? baslangic = null, DateTime? bitis = null, string status = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
           
            // Auto-Migration for TRHKAYIT Column (Emergency fix for Report View)
            try
            {
               _mskDb.Database.ExecuteSqlRaw("IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE Name = N'TRHKAYIT' AND Object_ID = Object_ID(N'TBL_TALEP')) BEGIN ALTER TABLE TBL_TALEP ADD TRHKAYIT DATETIME NULL DEFAULT GETDATE(); END");
            } catch { /* Ignore permissions/errors */ }

            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");
            int userId = int.Parse(userIdStr);

            // Fetch Data
            var (viewModels, startDate, endDate) = GetDevelopmentRequests(userId, period, baslangic, bitis);

            // Extract All Statuses for Dropdown (Before Filtering)
            var allStatuses = viewModels.Select(r => r.Status).Distinct().OrderBy(s => s).ToList();

            // Apply Status Filter
            if (!string.IsNullOrEmpty(status))
            {
                viewModels = viewModels.Where(r => r.Status == status).ToList();
            }

            // Prepare View Data
            ViewBag.CurrentPeriod = period;
            ViewBag.StartDate = startDate.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.ToString("yyyy-MM-dd");
            ViewBag.Status = status;
            ViewBag.AllStatuses = allStatuses;

            // Prepare Chart Data
            var statusCounts = viewModels
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            ViewBag.ChartLabels = statusCounts.Select(s => s.Status).ToList();
            ViewBag.ChartData = statusCounts.Select(s => s.Count).ToList();

            return View(viewModels);
        }

        public IActionResult ExportExcel(string period = "1m", DateTime? baslangic = null, DateTime? bitis = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");
            int userId = int.Parse(userIdStr);

            var (viewModels, _, _) = GetDevelopmentRequests(userId, period, baslangic, bitis);
            var content = GenerateDevelopmentExcel(viewModels);
            var fileName = $"Gelistirme_Raporu_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpPost]
        public async Task<IActionResult> SendDevelopmentReport(string email, string period = "1m", DateTime? baslangic = null, DateTime? bitis = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return Json(new { success = false, message = "Oturum bulunamadı." });
            int userId = int.Parse(userIdStr);

            // Rate Limiting Check
            string cacheKey = $"MailLimit_{userId}_Dev_{period}";
            if (_cache.TryGetValue(cacheKey, out object _))
            {
                 return Json(new { success = false, message = "Bu rapor kısa süre önce gönderildi. Lütfen bir süre bekleyin." });
            }

            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            string firmaAdi = kullanici?.TXTFIRMAADI ?? "Firma";

            try
            {
                var (viewModels, _, _) = GetDevelopmentRequests(userId, period, baslangic, bitis);
                var content = GenerateDevelopmentExcel(viewModels);
                
                string subject = $"{firmaAdi} Geliştirme Talebi Raporu";
                string message = $@"
                    <h3>Sayın İlgili,</h3>
                    <p>{firmaAdi} firmasına ait geliştirme talebi raporu ektedir.</p>
                    <p>İyi çalışmalar dileriz.</p>
                ";

                await _emailService.SendEmailAsync(email, subject, message, content, $"Gelistirme_Raporu_{DateTime.Now:yyyyMMdd}.xlsx");

                // Set Cache Expiration (e.g., 5 minutes to prevent spam/abuse, or period duration)
                using (var entry = _cache.CreateEntry(cacheKey))
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                    entry.Value = DateTime.Now;
                }

                return Json(new { success = true, message = $"Rapor başarıyla {email} adresine gönderildi." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Mail gönderilirken hata oluştu: " + ex.Message });
            }
        }

        private byte[] GenerateDevelopmentExcel(List<Request> viewModels)
        {
             using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("BasvuruListesi");
                
                // Headers
                var headers = new[] { "Firma", "Talep No", "Başlık", "Tarih", "Durum", "İlerleme", "Planlanan PY/UAT", "Planlanan Canlı", "Efor", "Maliyet" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                // Styling Headers
                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // Data Rows
                int row = 2;
                foreach (var item in viewModels)
                {
                    worksheet.Cell(row, 1).Value = item.Company ?? "";
                    worksheet.Cell(row, 2).Value = item.Id ?? "";
                    worksheet.Cell(row, 3).Value = item.Title ?? "";
                    worksheet.Cell(row, 4).Value = item.Date ?? "";
                    worksheet.Cell(row, 5).Value = item.Status ?? "";
                    worksheet.Cell(row, 6).Value = item.Progress; 
                    worksheet.Cell(row, 7).Value = item.PlanlananPyuat ?? "";
                    worksheet.Cell(row, 8).Value = item.PlanlananCanliTeslim ?? "";
                    worksheet.Cell(row, 9).Value = item.Effort ?? "";
                    worksheet.Cell(row, 10).Value = item.Cost ?? "";

                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "0\\%";
                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new System.IO.MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        private (List<Request> Requests, DateTime StartDate, DateTime EndDate) GetDevelopmentRequests(int userId, string period, DateTime? baslangic, DateTime? bitis)
        {
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);

            if (kullanici == null)
            {
                return (new List<Request>(), DateTime.Now, DateTime.Now);
            }
            
            string firmaAdi = kullanici.TXTFIRMAADI ?? "";

            // 1. Determine Target Companies
            var targetCompanies = new List<int>();
            int defaultFirmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;

            if (kullanici.LNGKULLANICITIPI == 3)
            {
                 targetCompanies = _mskDb.TBL_KULLANICI_FIRMAs
                                     .Where(f => f.LNGKULLANICIKOD == kullanici.LNGKOD)
                                     .Select(f => f.LNGFIRMAKOD)
                                     .ToList();
            }

            // Fallback Logic
            var projectClaim = User.FindFirst("ProjectCode");
            if (!targetCompanies.Any() && projectClaim != null && int.TryParse(projectClaim.Value, out int selectedProject))
            {
                targetCompanies.Add(selectedProject);
            }
            else if (!targetCompanies.Any())
            {
                 targetCompanies.Add(defaultFirmaKod);
            }


            // 2. Fetch TFS Data (Aggregated)
            var liveTfsRequests = new List<SSP_TFS_GELISTIRME>();
            foreach (var code in targetCompanies)
            {
                try 
                {
                    var tfs = _mskDb.SP_TFS_GELISTIRME(Convert.ToInt16(code)).ToList();
                    liveTfsRequests.AddRange(tfs);
                }
                catch (Exception ex) 
                {
                     // Log error?
                     Console.WriteLine($"Error fetching TFS for company {code}: {ex.Message}");
                }
            }


            // Filtering Logic
            DateTime startDate;
            DateTime endDate = DateTime.Now;

            if (period == "custom" && baslangic.HasValue)
            {
                startDate = baslangic.Value;
                if (bitis.HasValue)
                {
                     endDate = bitis.Value.Date.AddDays(1).AddTicks(-1);
                }
            }
            else
            {
                switch (period)
                {
                    case "1m":
                        startDate = DateTime.Now.AddMonths(-1);
                        break;
                    case "3m":
                        startDate = DateTime.Now.AddMonths(-3);
                        break;
                    case "1y":
                    default:
                        startDate = DateTime.Now.AddYears(-1);
                        break;
                }
            }

            var filteredTfs = liveTfsRequests
                .Where(tfs => !string.Equals(tfs.MADDEDURUM, "CLOSED", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "CANCEL", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "CANCELED", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "SEND BACK", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "SEND-BACK", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "REJECTED", StringComparison.OrdinalIgnoreCase))
                  .Where(tfs => (tfs.ACILMATARIHI >= startDate && tfs.ACILMATARIHI <= endDate) || 
                               ((string.Equals(tfs.MADDEDURUM, "RESOLVED", StringComparison.OrdinalIgnoreCase) || string.Equals(tfs.MADDEDURUM, "RESOLVE", StringComparison.OrdinalIgnoreCase)) && tfs.DEGISTIRMETARIHI >= startDate && tfs.DEGISTIRMETARIHI <= endDate))
                .OrderByDescending(tfs => tfs.ACILMATARIHI)
                .ToList();

            // 3. Fetch Portal Data
            var tfsIds = liveTfsRequests.Select(t => t.TFSNO).Distinct().ToList();

            var portalRequests = _mskDb.TBL_TALEPs
                .Include(t => t.TBL_TALEP_NOTLARs)
                .Include(t => t.TBL_TALEP_FILEs)
                .Where(r => (r.LNGVARUNAKOD.HasValue && targetCompanies.Contains(r.LNGVARUNAKOD.Value)) 
                            || (r.LNGTFSNO.HasValue && tfsIds.Contains(r.LNGTFSNO.Value)))
                .ToList();

            var portalMap = portalRequests
                .Where(r => r.LNGTFSNO.HasValue && r.LNGTFSNO.Value > 0)
                .GroupBy(r => r.LNGTFSNO.Value)
                .ToDictionary(g => g.Key, g => g.First());
            
            // Helper to get company name if needed, but for now we use scope 'firmaAdi' or user's main company name? 
            // Actually 'firmaAdi' is just the User's company name. 
            // Better to show the specific company name for each request if user is Type 3?
            // For now, let's stick to the current design or maybe fetch company names? 
            // The VIEW/Model doesn't seem to have CompanyName per request easily without Join.
            // Let's use the 'firmaAdi' (User's company) as default but ideally we should show the request's company.

            // 4. Merge Data
            var viewModels = new List<Request>();

            foreach (var tfs in filteredTfs)
            {
                var id = "TFS-" + tfs.TFSNO;
                var baseStatus = "Analiz"; 
                var baseProgress = tfs.TAMAMLANMA_OARANI.HasValue ? (int)tfs.TAMAMLANMA_OARANI.Value : 0;

                var portalRecord = portalMap.ContainsKey(tfs.TFSNO) ? portalMap[tfs.TFSNO] : null;
                
                if (portalRecord != null)
                {
                    var latestLog = _mskDb.TBL_TALEP_AKIS_LOGs
                        .Where(l => l.LNGTALEPKOD == portalRecord.LNGKOD)
                        .OrderByDescending(l => l.TRHDURUMBASLANGIC)
                        .Include(l => l.LNGDURUMKODNavigation)
                        .FirstOrDefault();

                    if (latestLog != null && latestLog.LNGDURUMKODNavigation != null)
                    {
                        baseStatus = latestLog.LNGDURUMKODNavigation.TXTDURUMADI;
                        baseProgress = GetProgressForStatus(baseStatus);
                    }
                }

                if (string.Equals(tfs.MADDEDURUM, "RESOLVED", StringComparison.OrdinalIgnoreCase) || 
                    string.Equals(tfs.MADDEDURUM, "RESOLVE", StringComparison.OrdinalIgnoreCase))
                {
                    baseStatus = "Tamamlandı";
                    baseProgress = 100;
                }

                decimal? yazilimInfo = tfs.YAZILIM_TOPLAMAG;

                viewModels.Add(new Request
                {
                    Id = id,
                    Title = tfs.MADDEBASLIK ?? "Başlıksız Talep",
                    Description = portalRecord?.TXTTALEPACIKLAMA ?? "",
                    Company = firmaAdi, // Maybe update this later to be dynamic if needed
                    Status = baseStatus,
                    DevOpsStatus = tfs.MADDEDURUM ?? "-",
                    Date = tfs.ACILMATARIHI?.ToString("dd.MM.yyyy") ?? DateTime.Now.ToString("dd.MM.yyyy"),
                    LastModifiedDate = tfs.DEGISTIRMETARIHI?.ToString("dd.MM.yyyy") ?? "-",
                    PlanlananPyuat = tfs.PLANLANAN_PYUAT?.ToString("dd.MM.yyyy") ?? "-", 
                    PlanlananCanliTeslim = tfs.PLANLAN_CANLITESLIM?.ToString("dd.MM.yyyy") ?? "-",
                    Priority = "Orta",
                    Progress = baseProgress,
                    Budget = tfs.COST ?? "-",
                    AssignedTo = tfs.YARATICI ?? "Atanmamış",
                    Effort = yazilimInfo.HasValue && yazilimInfo.Value > 0 ? yazilimInfo.Value.ToString("N2") + " K/G" : "-",
                    Cost = yazilimInfo.HasValue && yazilimInfo.Value > 0 ? (yazilimInfo.Value * 22500).ToString("N2") + " TL" : "-", 
                    Type = "Geliştirme"
                });
            }

            // 5. Add Portal-Only Requests (Not in TFS yet)
            var portalOnlyRequests = portalRequests
                .Where(r => (!r.LNGTFSNO.HasValue || r.LNGTFSNO == 0) && r.TRHKAYIT >= startDate && r.TRHKAYIT <= endDate && (r.BYTDURUM == null || r.BYTDURUM.Trim() == "1"))
                .ToList();

            foreach (var req in portalOnlyRequests)
            {
                viewModels.Add(new Request
                {
                    Id = "PORTAL-" + req.LNGKOD,
                    Title = req.TXTTALEPBASLIK ?? "Başlıksız",
                    Description = req.TXTTALEPACIKLAMA ?? "",
                    Company = firmaAdi, // Inherited from scope
                    Status = "Analiz", // Default
                    DevOpsStatus = "PORTAL",
                    Date = req.TRHKAYIT?.ToString("dd.MM.yyyy") ?? DateTime.Now.ToString("dd.MM.yyyy"),
                    LastModifiedDate = req.TRHKAYIT?.ToString("dd.MM.yyyy") ?? "-",
                    PlanlananPyuat = "-",
                    PlanlananCanliTeslim = "-",
                    Priority = "Orta",
                    Progress = 0,
                    Budget = "-",
                    AssignedTo = !string.IsNullOrEmpty(req.TXT_SORUMLULAR) ? req.TXT_SORUMLULAR : "Atanmamış",
                    Effort = req.DEC_EFOR.HasValue ? req.DEC_EFOR.Value.ToString("N2") + " K/G" : "-",
                    Cost = req.DEC_EFOR.HasValue ? (req.DEC_EFOR.Value * 22500).ToString("N2") + " TL" : "-",
                    Po = req.TXT_PO ?? "-", // Should display PO
                    Type = "Geliştirme"
                });
            }

            return (viewModels, startDate, endDate);
        }

        public IActionResult Destek(string period = "1m", DateTime? baslangic = null, DateTime? bitis = null, string status = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");
            int userId = int.Parse(userIdStr);

            // Fetch Data
            var (tickets, startDate, endDate, allStatuses) = GetSupportTickets(userId, period, baslangic, bitis);

            // Apply Status Filter
            if (!string.IsNullOrEmpty(status))
            {
                tickets = tickets.Where(r => r.Bildirim_Durumu == status).ToList();
            }

            // Prepare View Data
            ViewBag.CurrentPeriod = period;
            ViewBag.StartDate = startDate.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.ToString("yyyy-MM-dd");
            ViewBag.Status = status;
            ViewBag.AllStatuses = allStatuses;

            // Prepare Chart Data
            var statusCounts = tickets
                .GroupBy(r => r.Bildirim_Durumu)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            ViewBag.ChartLabels = statusCounts.Select(s => s.Status ?? "Belirsiz").ToList();
            ViewBag.ChartData = statusCounts.Select(s => s.Count).ToList();

            return View(tickets);
        }

        public IActionResult ExportDestekExcel(string period = "1m", DateTime? baslangic = null, DateTime? bitis = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");
            int userId = int.Parse(userIdStr);

            var (tickets, _, _, _) = GetSupportTickets(userId, period, baslangic, bitis);
            var content = GenerateSupportExcel(tickets);
            var fileName = $"Destek_Raporu_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpPost]
        public async Task<IActionResult> SendSupportReport(string email, string period = "1m", DateTime? baslangic = null, DateTime? bitis = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return Json(new { success = false, message = "Oturum bulunamadı." });
            int userId = int.Parse(userIdStr);

             // Rate Limiting Check
            string cacheKey = $"MailLimit_{userId}_Supp_{period}";
            if (_cache.TryGetValue(cacheKey, out object _))
            {
                 return Json(new { success = false, message = "Bu rapor kısa süre önce gönderildi. Lütfen bir süre bekleyin." });
            }

            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            string firmaAdi = kullanici?.TXTFIRMAADI ?? "Firma";

            try
            {
                var (tickets, _, _, _) = GetSupportTickets(userId, period, baslangic, bitis);
                var content = GenerateSupportExcel(tickets);

                string subject = $"{firmaAdi} Destek Talebi Raporu";
                string message = $@"
                    <h3>Sayın İlgili,</h3>
                    <p>{firmaAdi} firmasına ait destek talebi raporu ektedir.</p>
                    <p>İyi çalışmalar dileriz.</p>
                ";

                await _emailService.SendEmailAsync(email, subject, message, content, $"Destek_Raporu_{DateTime.Now:yyyyMMdd}.xlsx");

                // Set Cache Expiration (5 Minute Cooldown)
                using (var entry = _cache.CreateEntry(cacheKey))
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                    entry.Value = DateTime.Now;
                }

                return Json(new { success = true, message = $"Rapor başarıyla {email} adresine gönderildi." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Mail gönderilirken hata oluştu: " + ex.Message });
            }
        }

        private byte[] GenerateSupportExcel(List<SSP_N4B_TICKETLARI> tickets)
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("DestekTalepleri");

                // Headers
                var headers = new[] { "Firma", "Bildirim No", "Konu", "Tarih", "Durum", "Kanal", "İlgili Kişi", "SLA (Saat)" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                // Styling Headers
                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // Data Rows
                int row = 2;
                foreach (var item in tickets)
                {
                    worksheet.Cell(row, 1).Value = item.Firma ?? "";
                    worksheet.Cell(row, 2).Value = item.Bildirim_No;
                    worksheet.Cell(row, 3).Value = item.Bildirim_Aciklamasi ?? "";
                    worksheet.Cell(row, 4).Value = item.Bildirim_Tarihi.ToString("dd.MM.yyyy HH:mm");
                    worksheet.Cell(row, 5).Value = item.Bildirim_Durumu ?? "";
                    worksheet.Cell(row, 6).Value = item.Gelis_Kanali ?? "";
                    worksheet.Cell(row, 7).Value = item.Musteri_Tipi1 ?? ""; 
                    worksheet.Cell(row, 8).Value = item.SLA_YD_Cozum_Sure.HasValue ? item.SLA_YD_Cozum_Sure.Value.ToString("N2") : "-";

                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new System.IO.MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        private (List<SSP_N4B_TICKETLARI> Tickets, DateTime StartDate, DateTime EndDate, List<string> AllStatuses) GetSupportTickets(int userId, string period, DateTime? baslangic, DateTime? bitis)
        {
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            if (kullanici == null) return (new List<SSP_N4B_TICKETLARI>(), DateTime.Now, DateTime.Now, new List<string>());

            string email = User.FindFirstValue(ClaimTypes.Email) ?? "test@univera.com.tr";
            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;
            var projectClaim = User.FindFirst("ProjectCode");
            if (projectClaim != null && int.TryParse(projectClaim.Value, out int selectedProject))
            {
                firmaKod = selectedProject;
            }

            // Fetch Data using SP_N4B_TICKETLARI
            var allTickets = _mskDb.SP_N4B_TICKETLARI(Convert.ToInt16(firmaKod), email, 0);

            // Extract All Unique Statuses (Unfiltered)
            var allStatuses = allTickets
                .Select(t => t.Bildirim_Durumu)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            // Filtering Logic
            DateTime startDate;
            DateTime endDate = DateTime.Now;

            if (period == "custom" && baslangic.HasValue)
            {
                startDate = baslangic.Value;
                if (bitis.HasValue)
                {
                    endDate = bitis.Value.Date.AddDays(1).AddTicks(-1);
                }
            }
            else
            {
                switch (period)
                {
                    case "1m":
                        startDate = DateTime.Now.AddMonths(-1);
                        break;
                    case "3m":
                        startDate = DateTime.Now.AddMonths(-3);
                        break;
                    case "1y":
                    default:
                        startDate = DateTime.Now.AddYears(-1);
                        break;
                }
            }

            var filteredTickets = allTickets
                .Where(t => t.Bildirim_Tarihi >= startDate && t.Bildirim_Tarihi <= endDate)
                .OrderByDescending(t => t.Bildirim_Tarihi)
                .ToList();

            return (filteredTickets, startDate, endDate, allStatuses);
        }

        private int GetProgressForStatus(string status)
        {
            return status switch
            {
                "Analiz" => 15,
                "Bütçe Onayı" => 30,
                "Geliştirme" => 50,
                "Proje Testi" => 70,
                "Müşteri UAT" => 85,
                "Canlıya Geçiş" => 100,
                _ => 15
            };
        }
    }
}
