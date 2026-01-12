using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniCP.Models.Lisans;

namespace UniCP.Controllers
{
    [Authorize]
    public class LisanslarController : Controller
    {
        public IActionResult Index()
        {
            var licenses = new List<LisansVm>
            {
                new LisansVm { StokKodu = "EY.01.001", StokAdi = "EnRoute Panorama - Mobil Satış & Dağıtım Çözüm Lisansı", Kategori = "Lisans", UrunGrubu = "Enterprise", Aciklama = "Panorama platformu Çoklu Kanal Satış Yönetimi uygulaması modül lisansıdır." },
                new LisansVm { StokKodu = "EY.01.002", StokAdi = "EnRoute Panorama Mobile Sales & Distribution Module (DDI)", Kategori = "Lisans", UrunGrubu = "Enterprise", Aciklama = "Panorama platformu Veri Çekme uygulaması modül lisansıdır." },
                new LisansVm { StokKodu = "EY.01.021", StokAdi = "EnRoute Panorama - Dağıtım Kanalı (Bayi/Distribütör/Şube) Lisansı", Kategori = "Lisans", UrunGrubu = "Enterprise", Aciklama = "MSD modülünü kullanacak ve sisteme dahil olan her dağıtım kanalı için lisanslanır." },
                new LisansVm { StokKodu = "EY.01.014", StokAdi = "EnRoute Panorama - Platform Back Office Kullanıcı Lisansı", Kategori = "Lisans", UrunGrubu = "Enterprise", Aciklama = "Panorama platformu kullanıcı lisansıdır." },
                new LisansVm { StokKodu = "EY.02.033", StokAdi = "EnRoute Panorama - Mobil Kullanıcı Lisansı", Kategori = "Lisans", UrunGrubu = "Enterprise", Aciklama = "Sisteme dahil olacak her Mobil Kullanıcı için proje başlanır." },
                new LisansVm { StokKodu = "EY.05.008", StokAdi = "EnRoute Panorama - Commerce Portal - B2B - Dağıtım Kanalı Lisansı", Kategori = "Modül Lisansı", UrunGrubu = "Enterprise", Aciklama = "Ticari Portal Modülü kullanacak müşterilerde Standart versiyona ilave olarak her da..." },
                new LisansVm { StokKodu = "EY.05.009", StokAdi = "EnRoute Panorama - Business Analytics - Qlik Sense Analyzer User Lisansı", Kategori = "Modül Lisansı", UrunGrubu = "Enterprise", Aciklama = "Mevcut Qlik Site lisansına sahip müşterilerde, eski sözleşmeleri üzerinden kullan..." },
                new LisansVm { StokKodu = "EY.05.011", StokAdi = "EnRoute Panorama - Business Analytics - İş Zekası (MS Power BI) Site Lisansı", Kategori = "Modül Lisansı", UrunGrubu = "Enterprise", Aciklama = "Her müşteriye 1 Adet Site lisansı verilmelidir. Kullanıcı sayısından bağımsızdır." }
            };

            return View(licenses);
        }
    }
}
