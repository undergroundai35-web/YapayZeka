using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UniCP.DbData;
using UniCP.Models.N4BModels;
using UniCP.Models.MsK.SpModels;

namespace UniCP.Controllers.N4B
{
    [Authorize]
    public class N4BController : Controller
    {
        private readonly MskDbContext _mskDb;

        public N4BController(MskDbContext mskDb)
        {
            _mskDb = mskDb;
        }
       
        public ActionResult Index(int id)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");

            int userId = int.Parse(userIdStr);
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            
            if (kullanici == null)
            {
                // Fallback for missing TBL_KULLANICI link - log or handle as needed
                return View(new List<SSP_N4B_TICKETLARI>());
            }

            string email = User.FindFirstValue(ClaimTypes.Email) ?? "test@univera.com.tr";
            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;

            // User specified: Pass 3 for 'BildirimTipi' to get Open tickets
            if (id == 0) id = 3; 

            DateTime trh = new DateTime(2025, 1, 1);
            var bildirim_durum_sayı = _mskDb.SP_N4B_TICKET_DURUM_SAYILARI(Convert.ToInt16(firmaKod), email, trh);
            
            // Call SP with id=3 (or requested id) and implicitly trust it returns the correct set
            var bildirimler = _mskDb.SP_N4B_TICKETLARI(Convert.ToInt16(firmaKod), email, id)
                .OrderByDescending(x => x.Bildirim_Tarihi)
                .ToList();

            ViewBag.toplambildirim = bildirim_durum_sayı.Select(i => i.Sayi).Sum();
            ViewBag.acikbildirimsayi = bildirim_durum_sayı.Where(i => i.Durum.Contains("Açık")).Select(i => i.Sayi).Sum();
            ViewBag.kapalibildirimsayi = bildirim_durum_sayı.Where(i => i.Durum.Contains("Kapatıldı") || i.Durum.Contains("İptal")).Select(i => i.Sayi).Sum();
            ViewBag.cagrimerkezisayi = bildirim_durum_sayı.Where(i => i.Durum.Contains("Telefon")).Select(i => i.Sayi).Sum();
            ViewBag.yazilimdesteksayi = bildirim_durum_sayı.Where(i => i.Durum.Contains("Email")).Select(i => i.Sayi).Sum();
            
            ViewBag.SLA = _mskDb.SP_N4B_SLA_ORAN(Convert.ToInt16(firmaKod)).ToList();

            return View(bildirimler);
        }

        // ... Other actions kept as placeholders ...
        public ActionResult Details(int id)
        {
            var ticket = _mskDb.VIEW_N4BISSUEs.FirstOrDefault(x => x.Bildirim_No == id);
            if (ticket == null) return NotFound();

            var history = _mskDb.VIEW_N4BISSUESLIFECYCLEs
                .Where(x => x.IssueLifeIssueId == id)
                .OrderBy(x => x.Tarihce_Sira)
                .ToList();

            ViewBag.History = history;
            return View(ticket);
        }
        public ActionResult Create() { return View(); }
        public ActionResult Edit(int id) { return View(); }
        public ActionResult Delete(int id) { return View(); }
    }
}
