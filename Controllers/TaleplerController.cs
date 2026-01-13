using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using UniCP.DbData;
using UniCP.Models.Talepler;
using UniCP.Models.MsK.SpModels;
using UniCP.Models.MsK;
using Microsoft.EntityFrameworkCore;

namespace UniCP.Controllers
{
    [Authorize]
    public class TaleplerController : Controller
    {
        private readonly MskDbContext _mskDb;

        public TaleplerController(MskDbContext mskDb)
        {
            _mskDb = mskDb;
        }

        public IActionResult Index()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");

            int userId = int.Parse(userIdStr);
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);

            if (kullanici == null)
            {
                return View(new List<Request>());
            }

            // 1. Fetch TFS Data (System of Record for "External" Requests)
            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;
            var liveTfsRequests = _mskDb.SP_TFS_GELISTIRME(Convert.ToInt16(firmaKod));

            // Filter TFS Data
            var startDate = new DateTime(2025, 1, 1);
            var filteredTfs = liveTfsRequests
                .Where(tfs => !string.Equals(tfs.MADDEDURUM, "CLOSED", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "CANCEL", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "CANCELED", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "RESOLVED", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "SEND BACK", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "SEND-BACK", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(tfs.MADDEDURUM, "REJECTED", StringComparison.OrdinalIgnoreCase) &&
                              tfs.ACILMATARIHI >= startDate)
                .ToList();

            // 2. Fetch Portal Data (TBL_TALEP) - Includes "Shadow" records for TFS items and "Pure" portal requests
            // We preload Notes and Workflow Logs to avoid N+1
            var portalRequests = _mskDb.TBL_TALEPs
                .Include(t => t.TBL_TALEP_NOTLARs)
                .Include(t => t.TBL_TALEP_FILEs)
                .ToList();

            // Helper to find portal record for a TFS ID
            var portalMap = portalRequests
                .Where(r => r.LNGTFSNO.HasValue)
                .ToDictionary(r => r.LNGTFSNO.Value, r => r);

            // 3. Merge Data
            var viewModels = new List<Request>();

            foreach (var tfs in filteredTfs)
            {
                var id = "TFS-" + tfs.TFSNO;
                var baseStatus = "Analiz"; // Default to Analiz as per user request
                var baseProgress = tfs.TAMAMLANMA_OARANI.HasValue ? (int)tfs.TAMAMLANMA_OARANI.Value : 0;

                // Portal Override Logic
                var portalRecord = portalMap.ContainsKey(tfs.TFSNO) ? portalMap[tfs.TFSNO] : null;
                var comments = new List<Comment>();
                
                if (portalRecord != null)
                {
                    // Check for latest status in Access Log
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

                    // Comments
                    if (portalRecord.TBL_TALEP_NOTLARs != null)
                    {
                        foreach (var note in portalRecord.TBL_TALEP_NOTLARs)
                        {
                             var noteUser = _mskDb.TBL_KULLANICIs.FirstOrDefault(u => u.LNGIDENTITYKOD == note.LNGKULLANICIKOD);
                             var userName = noteUser?.TXTADSOYAD ?? "Kullanıcı";

                            comments.Add(new Comment
                            {
                                Id = note.LNGKOD.ToString(),
                                Text = note.TXTNOT ?? "",
                                User = userName,
                                Date = "Not Tarihi Yok" 
                            });
                        }
                    }
                }

                // Fix progress for display
                if (baseProgress == 100 && (baseStatus == "Analiz" || baseStatus == "ACTIVE")) baseProgress = 15;
                if (baseProgress == 0 && baseStatus == "Analiz") baseProgress = 15;

                decimal? yazilimInfo = decimal.TryParse(tfs.YAZILIM_TOPLAMAG, out var y) ? y : null;

                viewModels.Add(new Request
                {
                    Id = id,
                    Title = tfs.MADDEBASLIK ?? "Başlıksız Talep",
                    Status = baseStatus,
                    DevOpsStatus = tfs.MADDEDURUM ?? "-",
                    Date = tfs.ACILMATARIHI?.ToString("dd.MM.yyyy") ?? DateTime.Now.ToString("dd.MM.yyyy"),
                    LastModifiedDate = tfs.DEGISTIRMETARIHI?.ToString("dd.MM.yyyy") ?? "-",
                    PlanlananPyuat = tfs.PLANLANAN_PYUAT?.ToString("dd.MM.yyyy") ?? "-",
                    GerceklesenPyuat = tfs.GERCEKLESEN_PYUAT?.ToString("dd.MM.yyyy") ?? "-",
                    PlanlananCanliTeslim = tfs.PLANLAN_CANLITESLIM?.ToString("dd.MM.yyyy") ?? "-",
                    GerceklesenCanliTeslim = tfs.GERCEKLESEN_CANLITESLIM?.ToString("dd.MM.yyyy") ?? "-",
                    Priority = "Orta",
                    Progress = baseProgress,
                    Budget = tfs.COST ?? "-",
                    AssignedTo = tfs.YARATICI ?? "Atanmamış",
                    Effort = yazilimInfo.HasValue ? yazilimInfo.Value.ToString("N1") + " K/G" : "-",
                    Cost = yazilimInfo.HasValue ? (yazilimInfo.Value * 22500).ToString("N0") + " TL" : "-", 
                    Type = "Geliştirme",
                    Subtasks = new List<Subtask>(),
                    Comments = comments,
                    History = new List<HistoryItem>() 
                });
            }

            return View(viewModels);
        }

        [HttpPost]
        public IActionResult UpdateStatus(string id, string status)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = string.IsNullOrEmpty(userIdStr) ? 0 : int.Parse(userIdStr);

            // Get or Create TBL_TALEP
            var talep = GetOrCreateTalep(id, userId);
            if (talep == null) return Json(new { success = false, message = "Talep bulunamadı" });

            // Find Status ID
            var statusRecord = _mskDb.TBL_TALEP_AKISDURUMLARIs.FirstOrDefault(s => s.TXTDURUMADI == status);
            if (statusRecord == null)
            {
                // Create Status if not exists (Auto-seed for convenience)
                statusRecord = new TBL_TALEP_AKISDURUMLARI { TXTDURUMADI = status };
                _mskDb.TBL_TALEP_AKISDURUMLARIs.Add(statusRecord);
                _mskDb.SaveChanges();
            }

            // Insert Log
            var log = new TBL_TALEP_AKIS_LOG
            {
                LNGTALEPKOD = talep.LNGKOD,
                LNGTFSNO = talep.LNGTFSNO,
                LNGDURUMKOD = statusRecord.LNGKOD,
                TRHDURUMBASLANGIC = DateTime.Now,
                LNGONAYKULLANICI = userId,
                // LNGSIRA: Calculate max + 1
                LNGSIRA = (_mskDb.TBL_TALEP_AKIS_LOGs.Where(l => l.LNGTALEPKOD == talep.LNGKOD).Max(l => (int?)l.LNGSIRA) ?? 0) + 1
            };

            _mskDb.TBL_TALEP_AKIS_LOGs.Add(log);
            _mskDb.SaveChanges();

            return Json(new { success = true });
        }

        [HttpPost]
        public IActionResult AddComment(string id, string text)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = string.IsNullOrEmpty(userIdStr) ? 0 : int.Parse(userIdStr);

            var talep = GetOrCreateTalep(id, userId);
            if (talep == null) return Json(new { success = false, message = "Talep bulunamadı" });

            var note = new TBL_TALEP_NOTLAR
            {
                LNGTALEPKOD = talep.LNGKOD,
                TXTNOT = text,
                LNGKULLANICIKOD = userId,
                BYTDURUM = 1 // Active
            };

            _mskDb.TBL_TALEP_NOTLARs.Add(note);
            _mskDb.SaveChanges();
            
             var userName = User.Identity?.Name ?? "Kullanıcı";
             var userRec = _mskDb.TBL_KULLANICIs.FirstOrDefault(u => u.LNGIDENTITYKOD == userId);
             if (userRec != null) userName = userRec.TXTADSOYAD;

            return Json(new { success = true, comment = new Comment { 
                User = userName, 
                Text = text, 
                Date = DateTime.Now.ToString("dd.MM.yyyy HH:mm") 
            }});
        }

        // Helper Methods

        private TBL_TALEP? GetOrCreateTalep(string idStr, int userId)
        {
            bool isTfs = idStr.StartsWith("TFS-");
            int tfsNo = 0;
            if (isTfs)
            {
                int.TryParse(idStr.Replace("TFS-", ""), out tfsNo);
            }

            TBL_TALEP? talep = null;

            if (isTfs && tfsNo > 0)
            {
                talep = _mskDb.TBL_TALEPs.FirstOrDefault(t => t.LNGTFSNO == tfsNo);
                if (talep == null)
                {
                    // Create Shadow Record
                    // Need to fetch TFS Title for better record, but optional
                    var tfsItem = _mskDb.SP_TFS_GELISTIRME(2).FirstOrDefault(t => t.TFSNO == tfsNo); // FirmaKod hardcoded 2 for lookup scope
                    
                    talep = new TBL_TALEP
                    {
                        LNGTFSNO = tfsNo,
                        TXTTALEPBASLIK = tfsItem?.MADDEBASLIK ?? "Otomatik Oluşturulan Talep",
                        BYTDURUM = "1", // Active
                        LNGPROJEKOD = 1 // Default Project Code
                    };
                    _mskDb.TBL_TALEPs.Add(talep);
                    _mskDb.SaveChanges();
                }
            }
            
            return talep;
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
