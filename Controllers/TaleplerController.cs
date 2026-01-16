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
    public class TaleplerController : Controller
    {
        private readonly MskDbContext _mskDb;

        public TaleplerController(MskDbContext mskDb)
        {
            _mskDb = mskDb;
        }

        public IActionResult Index()
        {
            // Auto-Migration for PO Column (Temporary for Dev)
            try
            {
               _mskDb.Database.ExecuteSqlRaw("IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE Name = N'TXT_PO' AND Object_ID = Object_ID(N'TBL_TALEP')) BEGIN ALTER TABLE TBL_TALEP ADD TXT_PO VARCHAR(50) NULL; END");
               _mskDb.Database.ExecuteSqlRaw("IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE Name = N'TRHKAYIT' AND Object_ID = Object_ID(N'TBL_TALEP')) BEGIN ALTER TABLE TBL_TALEP ADD TRHKAYIT DATETIME NULL DEFAULT GETDATE(); END");
            } catch { /* Ignore permissions/errors */ }

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
                .Where(r => r.LNGTFSNO.HasValue && r.LNGTFSNO > 0)
                .ToDictionary(r => r.LNGTFSNO.Value, r => r);

            // 3. Merge Data
            var viewModels = new List<Request>();

            // A. Add TFS Requests
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

                decimal? yazilimInfo = tfs.YAZILIM_TOPLAMAG;

                viewModels.Add(new Request
                {
                    Id = id,
                    Title = tfs.MADDEBASLIK ?? "Başlıksız Talep",
                    Description = portalRecord?.TXTTALEPACIKLAMA ?? "",
                    Status = baseStatus,
                    DevOpsStatus = tfs.MADDEDURUM ?? "-",
                    Date = tfs.ACILMATARIHI?.ToString("dd.MM.yyyy HH:mm") ?? DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
                    LastModifiedDate = tfs.DEGISTIRMETARIHI?.ToString("dd.MM.yyyy") ?? "-",
                    PlanlananPyuat = tfs.PLANLANAN_PYUAT?.ToString("dd.MM.yyyy") ?? "-",
                    GerceklesenPyuat = tfs.GERCEKLESEN_PYUAT?.ToString("dd.MM.yyyy") ?? "-",
                    PlanlananCanliTeslim = tfs.PLANLAN_CANLITESLIM?.ToString("dd.MM.yyyy") ?? "-",
                    GerceklesenCanliTeslim = tfs.GERCEKLESEN_CANLITESLIM?.ToString("dd.MM.yyyy") ?? "-",
                    Priority = "Orta",
                    Progress = baseProgress,
                    Budget = tfs.COST ?? "-",
                    AssignedTo = "Atanmamış",
                    Effort = yazilimInfo.HasValue && yazilimInfo.Value > 0 ? yazilimInfo.Value.ToString("N0") + " K/G" : "-",
                    Cost = yazilimInfo.HasValue && yazilimInfo.Value > 0 ? (yazilimInfo.Value * 22500).ToString("N0") + " TL" : "-", 
                    Type = "Geliştirme",
                    Subtasks = new List<Subtask>(),
                    Comments = comments,
                    History = new List<HistoryItem>() 
                });
            }

            // B. Add Portal-Only Requests (Not synced to TFS yet)
            var portalOnlyRequests = portalRequests
                .Where(r => (!r.LNGTFSNO.HasValue || r.LNGTFSNO == 0) && r.LNGVARUNAKOD == firmaKod) // Only this company
                .ToList();

            foreach (var req in portalOnlyRequests)
            {
                var comments = new List<Comment>();
                 if (req.TBL_TALEP_NOTLARs != null)
                {
                    foreach (var note in req.TBL_TALEP_NOTLARs)
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
                
                // Get Status
                 var latestLog = _mskDb.TBL_TALEP_AKIS_LOGs
                        .Where(l => l.LNGTALEPKOD == req.LNGKOD)
                        .OrderByDescending(l => l.TRHDURUMBASLANGIC)
                        .Include(l => l.LNGDURUMKODNavigation)
                        .FirstOrDefault();
                
                var status = "Analiz";
                if(latestLog?.LNGDURUMKODNavigation != null) status = latestLog.LNGDURUMKODNavigation.TXTDURUMADI;

                decimal effort = req.DEC_EFOR ?? 0;
                string effortStr = effort > 0 ? effort.ToString("N0") + " K/G" : "-";
                string costStr = effort > 0 ? (effort * 22500).ToString("N0") + " TL" : "-";

                // Parse Assignees (Stored as IDs "1,2,5") and resolve names
                string assigneeNames = "Atanmamış";
                if (!string.IsNullOrEmpty(req.TXT_SORUMLULAR))
                {
                    var assigneeIds = req.TXT_SORUMLULAR.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => int.Parse(s)).ToList();
                    var assignees = _mskDb.TBL_KULLANICIs.Where(u => u.LNGIDENTITYKOD.HasValue && assigneeIds.Contains(u.LNGIDENTITYKOD.Value)).Select(u => u.TXTADSOYAD).ToList();
                    if(assignees.Any()) assigneeNames = string.Join(", ", assignees);
                }

                viewModels.Add(new Request
                {
                    Id = "PORTAL-" + req.LNGKOD,
                    Title = req.TXTTALEPBASLIK ?? "Başlıksız",
                    Description = req.TXTTALEPACIKLAMA ?? "",
                    Status = status,
                    DevOpsStatus =("-"),
                    Date = DateTime.Now.ToString("dd.MM.yyyy HH:mm"), // Fallback as TRHKAYIT is missing
                    LastModifiedDate = DateTime.Now.ToString("dd.MM.yyyy"),
                    PlanlananPyuat = "-",
                    GerceklesenPyuat = "-",
                    PlanlananCanliTeslim = "-",
                    GerceklesenCanliTeslim = "-",
                    Priority = "Orta",
                    Progress = GetProgressForStatus(status ?? "Analiz"),
                    Budget = "-",
                    AssignedTo = assigneeNames,
                    Effort = effortStr,
                    Cost = costStr,
                    Po = req.TXT_PO ?? "-",
                    Type = "Geliştirme",
                    Subtasks = new List<Subtask>(),
                    Comments = comments,
                    History = new List<HistoryItem>()
                });
            }

            // Sort: Portal Request (Newest First) then TFS (Newest ACILMA TARIH)
            // But viewModels currently mixes them.
            // Let's sort by Date desc if possible. Since Date format is string, it's tricky.
            // But we inserted them in order. Let's put Portal Requests at the TOP.
            viewModels = viewModels.OrderByDescending(x => x.Id.StartsWith("PORTAL")).ThenByDescending(x => x.Date).ToList();

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
        public IActionResult Create(string title, string description, decimal? effort, string assignees, string po)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = string.IsNullOrEmpty(userIdStr) ? 0 : int.Parse(userIdStr);
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            int firmaKod = kullanici?.LNGORTAKFIRMAKOD ?? 0;

            if (string.IsNullOrEmpty(title)) return Json(new { success = false, message = "Başlık zorunludur" });

            // 1. Create TBL_TALEP
            var talep = new TBL_TALEP
            {
                TXTTALEPBASLIK = title,
                TXTTALEPACIKLAMA = description ?? "",
                BYTDURUM = "1", // Active
                LNGPROJEKOD = 1, // Default
                LNGTFSNO = 0, // Not linked to TFS yet
                LNGVARUNAKOD = firmaKod, // Link to User's Company
                DEC_EFOR = effort,
                TXT_SORUMLULAR = assignees,
                TXT_PO = po,
                TRHKAYIT = DateTime.Now
            };

            _mskDb.TBL_TALEPs.Add(talep);
            _mskDb.SaveChanges(); // to get LNGKOD

            // 2. Create Initial Log (Analiz Status)
            var statusStr = "Analiz";
            var statusRecord = _mskDb.TBL_TALEP_AKISDURUMLARIs.FirstOrDefault(s => s.TXTDURUMADI == statusStr);
            if (statusRecord == null)
            {
                statusRecord = new TBL_TALEP_AKISDURUMLARI { TXTDURUMADI = statusStr };
                _mskDb.TBL_TALEP_AKISDURUMLARIs.Add(statusRecord);
                _mskDb.SaveChanges();
            }

            var log = new TBL_TALEP_AKIS_LOG
            {
                LNGTALEPKOD = talep.LNGKOD,
                LNGTFSNO = 0,
                LNGDURUMKOD = statusRecord.LNGKOD,
                TRHDURUMBASLANGIC = DateTime.Now,
                LNGONAYKULLANICI = userId,
                LNGSIRA = 1
            };

            _mskDb.TBL_TALEP_AKIS_LOGs.Add(log);
            _mskDb.SaveChanges();

            // Return formatted request object for UI to append
            var newRequest = new Request
            {
                Id = "PORTAL-" + talep.LNGKOD, // Temporary ID format for portal-only requests
                Title = talep.TXTTALEPBASLIK,
                Description = talep.TXTTALEPACIKLAMA,
                Status = "Analiz",
                DevOpsStatus = "",
                Date = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
                LastModifiedDate = DateTime.Now.ToString("dd.MM.yyyy"),
                Priority = "Orta",
                Progress = 15,
                AssignedTo = "Atanmamış",
                Type = "Geliştirme",
                Comments = new List<Comment>(),
                Subtasks = new List<Subtask>(),
                History = new List<HistoryItem>()
                {
                    new HistoryItem { Id = "1", User = User.Identity?.Name ?? "Ben", Action = "Talep oluşturuldu.", Date = DateTime.Now.ToString("dd.MM.yyyy HH:mm"), Type="status_change" }
                }
            };

            return Json(new { success = true, request = newRequest });
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

        [HttpPost]
        public async Task<IActionResult> UploadFile(string id, IFormFile file)
        {
            if (file == null || file.Length == 0) return Json(new { success = false, message = "Dosya seçilmedi" });

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = string.IsNullOrEmpty(userIdStr) ? 0 : int.Parse(userIdStr);

            var talep = GetOrCreateTalep(id, userId);
            if (talep == null) return Json(new { success = false, message = "Talep bulunamadı" });

            try
            {
                // Ensure directory exists
                string uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "talepler", talep.LNGKOD.ToString());
                if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

                string originalFileName = Path.GetFileName(file.FileName);
                string uniqueFileName = $"{DateTime.Now.Ticks}_{originalFileName}";
                string filePath = Path.Combine(uploadPath, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Create DB Record
                var talepFile = new TBL_TALEP_FILE
                {
                    LNGTALEPKOD = talep.LNGKOD,
                    FileName = originalFileName,
                    FileBase64 = $"/uploads/talepler/{talep.LNGKOD}/{uniqueFileName}",
                    FileContentType = file.ContentType
                };

                _mskDb.TBL_TALEP_FILEs.Add(talepFile);
                await _mskDb.SaveChangesAsync();

                return Json(new { success = true, fileName = originalFileName, filePath = talepFile.FileBase64 });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Hata: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetFiles(string id, string? talepNo)
        {
            var talepId = id ?? talepNo;
            if (string.IsNullOrEmpty(talepId)) return Json(new { success = false, message = "ID eksik" });

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = string.IsNullOrEmpty(userIdStr) ? 0 : int.Parse(userIdStr);

            var talep = GetOrCreateTalep(talepId, userId);
            if (talep == null) return Json(new { success = false, message = "Talep bulunamadı" });

            var files = _mskDb.TBL_TALEP_FILEs
                .Where(f => f.LNGTALEPKOD == talep.LNGKOD)
                .Select(f => new
                {
                    lngkod = f.LNGKOD,
                    fileName = f.FileName,
                    fileBase64 = f.FileBase64,
                    date = "-"
                })
                .ToList();

            return Json(new { success = true, files = files });
        }

        [HttpGet]
        public IActionResult DownloadFile(int id)
        {
            var fileRec = _mskDb.TBL_TALEP_FILEs.FirstOrDefault(f => f.LNGKOD == id);
            if (fileRec == null) return NotFound("Dosya kaydı bulunamadı.");

            string relativePath = fileRec.FileBase64?.TrimStart('/') ?? "";
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);
            
            if (!System.IO.File.Exists(filePath)) return NotFound("Dosya fiziksel olarak bulunamadı.");

            var contentType = fileRec.FileContentType ?? "application/octet-stream";
            var downloadName = fileRec.FileName ?? "download";

            return File(System.IO.File.OpenRead(filePath), contentType, downloadName);
        }

        [HttpGet]
        public IActionResult GetUsers(string q)
        {
             var query = _mskDb.TBL_KULLANICIs.AsQueryable();
             if (!string.IsNullOrEmpty(q))
             {
                 query = query.Where(u => u.TXTADSOYAD.Contains(q));
             }

             var users = query.Select(u => new { 
                 id = u.LNGIDENTITYKOD, 
                 name = u.TXTADSOYAD, 
                 email = u.TXTEMAIL 
             }).Take(20).ToList();

             return Json(users);
        }

        // Helper Methods


        [HttpPost]
        public IActionResult UpdatePo(string id, string po)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = string.IsNullOrEmpty(userIdStr) ? 0 : int.Parse(userIdStr);

            var talep = GetOrCreateTalep(id, userId);
            if (talep == null) return Json(new { success = false, message = "Talep bulunamadı" });

            talep.TXT_PO = po;
            _mskDb.SaveChanges();

            return Json(new { success = true });
        }

        private TBL_TALEP? GetOrCreateTalep(string idStr, int userId)
        {
            // Handle Portal IDs
            if (idStr.StartsWith("PORTAL-"))
            {
                if (int.TryParse(idStr.Replace("PORTAL-", ""), out int talepId))
                {
                    return _mskDb.TBL_TALEPs.FirstOrDefault(t => t.LNGKOD == talepId);
                }
                return null;
            }
            
            // Handle raw Integers (Legacy/Direct ID)
            if (int.TryParse(idStr, out int rawId))
            {
                 // Check if it matches a TBL_TALEP LNGKOD directly first
                 var direct = _mskDb.TBL_TALEPs.FirstOrDefault(t => t.LNGKOD == rawId);
                 if(direct != null) return direct;
            }

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
