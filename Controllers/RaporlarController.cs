using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using UniCP.DbData;
using UniCP.Models.Talepler;
using UniCP.Models.MsK.SpModels;
using UniCP.Models.MsK;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace UniCP.Controllers
{
    [Authorize]
    public class RaporlarController : Controller
    {
        private readonly MskDbContext _mskDb;
        private readonly UniCP.Models.IEmailService _emailService;

        public RaporlarController(MskDbContext mskDb, UniCP.Models.IEmailService emailService)
        {
            _mskDb = mskDb;
            _emailService = emailService;
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

            // 1. Fetch TFS Data
            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;
            var liveTfsRequests = _mskDb.SP_TFS_GELISTIRME(Convert.ToInt16(firmaKod));

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

            // 2. Fetch Portal Data
            var portalRequests = _mskDb.TBL_TALEPs
                .Include(t => t.TBL_TALEP_NOTLARs)
                .Include(t => t.TBL_TALEP_FILEs)
                .ToList();

            var portalMap = portalRequests
                .Where(r => r.LNGTFSNO.HasValue && r.LNGTFSNO.Value > 0)
                .GroupBy(r => r.LNGTFSNO.Value)
                .ToDictionary(g => g.Key, g => g.First());

            // 3. Merge Data
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
                    Company = firmaAdi,
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
                    Effort = yazilimInfo.HasValue && yazilimInfo.Value > 0 ? yazilimInfo.Value.ToString("N0") + " K/G" : "-",
                    Cost = yazilimInfo.HasValue && yazilimInfo.Value > 0 ? (yazilimInfo.Value * 22500).ToString("N0") + " TL" : "-", 
                    Type = "Geliştirme"
                });
            }

            // 4. Add Portal-Only Requests (Not in TFS yet)
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
                    Effort = req.DEC_EFOR.HasValue ? req.DEC_EFOR.Value.ToString("N0") + " K/G" : "-",
                    Cost = req.DEC_EFOR.HasValue ? (req.DEC_EFOR.Value * 22500).ToString("N0") + " TL" : "-",
                    Po = req.TXT_PO ?? "-", // Should display PO
                    Type = "Geliştirme"
                });
            }

            return (viewModels, startDate, endDate);
        }

        public IActionResult Destek(string period = "1m", DateTime? baslangic = null, DateTime? bitis = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");
            int userId = int.Parse(userIdStr);

            // Fetch Data
            var (tickets, startDate, endDate) = GetSupportTickets(userId, period, baslangic, bitis);

            // Prepare View Data
            ViewBag.CurrentPeriod = period;
            ViewBag.StartDate = startDate.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.ToString("yyyy-MM-dd");

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

            var (tickets, _, _) = GetSupportTickets(userId, period, baslangic, bitis);
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

            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            string firmaAdi = kullanici?.TXTFIRMAADI ?? "Firma";

            try
            {
                var (tickets, _, _) = GetSupportTickets(userId, period, baslangic, bitis);
                var content = GenerateSupportExcel(tickets);

                string subject = $"{firmaAdi} Destek Talebi Raporu";
                string message = $@"
                    <h3>Sayın İlgili,</h3>
                    <p>{firmaAdi} firmasına ait destek talebi raporu ektedir.</p>
                    <p>İyi çalışmalar dileriz.</p>
                ";

                await _emailService.SendEmailAsync(email, subject, message, content, $"Destek_Raporu_{DateTime.Now:yyyyMMdd}.xlsx");

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

        private (List<SSP_N4B_TICKETLARI> Tickets, DateTime StartDate, DateTime EndDate) GetSupportTickets(int userId, string period, DateTime? baslangic, DateTime? bitis)
        {
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            if (kullanici == null) return (new List<SSP_N4B_TICKETLARI>(), DateTime.Now, DateTime.Now);

            string email = User.FindFirstValue(ClaimTypes.Email) ?? "test@univera.com.tr";
            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;

            // Fetch Data using SP_N4B_TICKETLARI
            // ID = 0 to fetch all tickets mostly, logic from N4BController
            // Assuming SP returns all tickets and we filter locally for flexibility
            var allTickets = _mskDb.SP_N4B_TICKETLARI(Convert.ToInt16(firmaKod), email, 0);

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

            return (filteredTickets, startDate, endDate);
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
