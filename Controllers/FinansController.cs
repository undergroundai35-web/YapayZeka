using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniCP.Models.Finans;

namespace UniCP.Controllers
{
    [Authorize]
    public class FinansController : Controller
    {
        public IActionResult Index()
        {
            var services = new List<FinansItemVm>
            {
                new FinansItemVm { StokKodu = "EH.05.001", StokAdi = "EnRoute Panorama - Yazılım Geliştirme Hizmeti", Kategori = "Hizmet", Birim = "Adam/Gün", Aciklama = "Geliştirme hizmetleri için yapılacak analiz, kodlama ve test işlemlerinde gerç..." },
                new FinansItemVm { StokKodu = "EH.05.002", StokAdi = "EnRoute Panorama - Rapor Geliştirme Hizmeti", Kategori = "Hizmet", Birim = "Adam/Gün", Aciklama = "Geliştirme hizmetleri için yapılacak analiz, kodlama ve test işlemlerinde gerç..." },
                new FinansItemVm { StokKodu = "EH.01.003", StokAdi = "EnRoute Panorama - Kurulum ve Eğitim Hizmeti", Kategori = "Hizmet", Birim = "Adam/Gün", Aciklama = "FMCG projelerin dağıtım kanalı başına minimum 2 A/G dür." },
                new FinansItemVm { StokKodu = "EH.01.005", StokAdi = "EnRoute Panorama - Online Eğitim Hizmeti", Kategori = "Hizmet", Birim = "Kişi/Saat", Aciklama = "Kişi/Saat bazlı online eğitim hizmeti." },
                new FinansItemVm { StokKodu = "EH.02.001", StokAdi = "EnRoute Panorama - Yazılım Bakımı, Yaşatma ve Merkezi Destek Hizmeti", Kategori = "Bakım-Destek", Birim = "Yıllık", Aciklama = "Liste fiyatlarına göre lisans toplamlarının %22'si alınacaktır." },
                new FinansItemVm { StokKodu = "EH.03.001", StokAdi = "EnRoute Panorama - Çağrı Merkezi Hizmeti (MSD)", Kategori = "Bakım-Destek", Birim = "Aylık", Aciklama = "Dağıtım kanalı başına ücretlendirilecek Çağrı Merkezi hizmetini kapsar." }
            };

            return View(services);
        }
    }
}
