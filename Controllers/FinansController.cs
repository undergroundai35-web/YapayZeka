using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UniCP.DbData;
using UniCP.Models.Finans;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System.IO;

namespace UniCP.Controllers
{
    [Authorize]
    public class FinansController : Controller
    {
        private readonly MskDbContext _mskDb;
        private readonly UniCP.Models.IEmailService _emailService;

        public FinansController(MskDbContext mskDb, UniCP.Models.IEmailService emailService)
        {
            _mskDb = mskDb;
            _emailService = emailService;
        }

        public IActionResult Index(string filter = "month", DateTime? startDate = null, DateTime? endDate = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");

            int userId = int.Parse(userIdStr);
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            
            if (kullanici == null) return RedirectToAction("Login", "Account");

            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;

            var varunaSiparisler = GetFilteredOrders(firmaKod, filter, startDate, endDate);

            // Calculate Previous Period Stats
            CalculateComparisonStats(firmaKod, filter, startDate, endDate, varunaSiparisler.Sum(x => x.TotalAmountWithTax ?? 0));

            var filteredList = varunaSiparisler.ToList();
            ViewBag.TotalFilteredAmount = filteredList.Sum(x => x.TotalAmountWithTax ?? 0);
            
            ViewBag.ActiveFilter = filter;
            if (startDate.HasValue) ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            if (endDate.HasValue) ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            // Chart Data Logic
            var chartDataRaw = _mskDb.SP_VARUNA_CHART_DATA(firmaKod).ToList();
            
            // Filter Chart Data (In-memory)
            // Determine effective dates for Chart
            DateTime chartStartDate;
            DateTime chartEndDate = DateTime.Now;

            if (startDate.HasValue && endDate.HasValue)
            {
                chartStartDate = startDate.Value;
                chartEndDate = endDate.Value.Date.AddDays(1).AddTicks(-1);
            }
            else
            {
                switch (filter.ToLower())
                {
                    case "3months": chartStartDate = DateTime.Now.AddMonths(-3); break;
                    case "year": chartStartDate = DateTime.Now.AddYears(-1); break;
                    case "month": 
                    default: chartStartDate = DateTime.Now.AddMonths(-1); break;
                }
            }

            // Filter Chart Data (In-memory)
            var filteredChartData = chartDataRaw
                .Where(x => x.TARIH >= chartStartDate && x.TARIH <= chartEndDate)
                .ToList();

            // Group and Sum
            var chartGrouped = filteredChartData
                .GroupBy(x => x.GRUP ?? "Diğer")
                .Select(g => new {
                    Label = g.Key,
                    Value = g.Sum(x => x.TOPLAMTUTAR)
                })
                .OrderByDescending(x => x.Value)
                .ToList();

            ViewBag.ChartData = new {
                Labels = chartGrouped.Select(x => x.Label).ToList(),
                Values = chartGrouped.Select(x => x.Value).ToList(),
                Total = chartGrouped.Sum(x => x.Value)
            };

            // Calculate Pending Payment (Bekleyen Bakiye > 0)
            var pendingOrders = filteredList.Where(x => x.Bekleyen_Bakiye > 0).ToList();
            ViewBag.PendingPayment = pendingOrders.Sum(x => x.Bekleyen_Bakiye ?? 0);
            ViewBag.OverdueCount = pendingOrders.Count(x => x.Gecikme_Gun > 0);
            ViewBag.WaitingForMaturityCount = pendingOrders.Count(x => x.Gecikme_Gun <= 0);

            ViewBag.WaitingForMaturityCount = pendingOrders.Count(x => x.Gecikme_Gun <= 0);

            return View(filteredList);
        }

        public IActionResult DownloadStatement(string filter = "month", DateTime? startDate = null, DateTime? endDate = null)
        {
            // ... existing PDF logic ...
            // (Keeping the existing method as is, or I could remove it if the user wants purely replacement. 
            // The prompt said "PDF yerine Excel olsun" implies replacement, but keeping it as a legacy or alternative might be safer unless explicitly told to delete.
            // Actually, user said "buradaki indirme formatı da excel olsun", implies change. 
            // I will keep existing PDF method for now just in case, but the UI will point to Excel.)
            
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");

            int userId = int.Parse(userIdStr);
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            if (kullanici == null) return RedirectToAction("Login", "Account");

            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;

            var orders = GetFilteredOrders(firmaKod, filter, startDate, endDate).ToList();

            using (var stream = new MemoryStream())
            {
                var document = new PdfDocument();
                // ... (existing PDF generation code) ...
                // Re-creating functionality inside block to ensure it compiles if I were replacing, 
                // but since I am appending/modifying, I should just ADD the new method.
                // However, `replace_file_content` replaces a block.
                // I will Add ExportExcel AFTER DownloadStatement.
                 var page = document.AddPage();
                var gfx = XGraphics.FromPdfPage(page);
                var fontTitle = new XFont("Arial", 18, XFontStyle.Bold);
                var fontHeader = new XFont("Arial", 10, XFontStyle.Bold);
                var fontRow = new XFont("Arial", 9, XFontStyle.Regular);
                var fontBoldRow = new XFont("Arial", 9, XFontStyle.Bold);
                var fontSmall = new XFont("Arial", 8, XFontStyle.Italic);

                double yPoint = 40;
                double margin = 40;
                double pageWidth = page.Width;
                double contentWidth = pageWidth - (2 * margin);

                // Title
                gfx.DrawString("Hesap Ekstresi", fontTitle, XBrushes.Black, new XRect(0, yPoint, pageWidth, 30), XStringFormats.TopCenter);
                yPoint += 40;
                
                // Filter Info
                string filterName = filter switch 
                {
                    "month" => "Aylık",
                    "3months" => "3 Aylık",
                    "year" => "Yıllık",
                    _ => filter
                };

                string dateRange = startDate.HasValue && endDate.HasValue 
                    ? $"{startDate.Value:dd.MM.yyyy} - {endDate.Value:dd.MM.yyyy}"
                    : $"Filtre: {filterName}";
                gfx.DrawString($"Rapor Tarihi: {DateTime.Now:dd.MM.yyyy HH:mm}", fontSmall, XBrushes.DarkGray, margin, yPoint);
                gfx.DrawString($"Aralık: {dateRange}", fontSmall, XBrushes.DarkGray, pageWidth - margin - 100, yPoint);
                yPoint += 20;

                gfx.DrawLine(XPens.Gray, margin, yPoint, pageWidth - margin, yPoint);
                yPoint += 20;

                decimal grandTotal = 0;

                foreach (var order in orders)
                {
                    // Check page break for Order Header + at least one item
                    if (yPoint > page.Height - 80)
                    {
                        page = document.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        yPoint = 40;
                    }

                    // Order Header
                    var status = order.OrderStatus == "Closed" ? "Kapalı" : (order.OrderStatus ?? "-");
                    var orderDate = order.CreateOrderDate?.ToString("dd.MM.yyyy") ?? "-";
                    
                    // Removed gray background
                    // var rectHeader = new XRect(margin, yPoint, contentWidth, 20);
                    // gfx.DrawRectangle(XBrushes.WhiteSmoke, rectHeader);
                    
                    gfx.DrawString($"{order.SerialNumber} | Tar: {orderDate} | Durum: {status}", fontHeader, XBrushes.Black, margin + 5, yPoint + 5);
                    yPoint += 25;

                    // Fetch Details
                    var details = _mskDb.SP_VARUNA_SIPARIS_DETAY(order.OrderId).ToList();

                    // Table Header
                    // Layout: 
                    // Product: Left (margin + 10)
                    // Quantity: Right Aligned (Box: margin + 300, Width 50) -> Ends at 350
                    // Unit Price: Right Aligned (Box: margin + 360, Width 80) -> Ends at 440
                    // Total: Right Aligned (Box: margin + 450, Width 80) -> Ends at 530
                    
                    var brushHeader = XBrushes.DarkSlateGray; // Darker than details, lighter than black
                    var brushRow = XBrushes.SlateGray;      // Lighter detail color

                    gfx.DrawString("Ürün", fontBoldRow, brushHeader, margin + 10, yPoint);
                    gfx.DrawString("Miktar", fontBoldRow, brushHeader, new XRect(margin + 260, yPoint, 50, 15), XStringFormats.TopRight);
                    gfx.DrawString("Birim Fiyat", fontBoldRow, brushHeader, new XRect(margin + 320, yPoint, 80, 15), XStringFormats.TopRight); // Closer to quantity
                    gfx.DrawString("Satır Tutarı", fontBoldRow, brushHeader, new XRect(margin + 410, yPoint, 100, 15), XStringFormats.TopRight);
                    yPoint += 15;

                    foreach (var item in details)
                    {
                        if (yPoint > page.Height - 40)
                        {
                            page = document.AddPage();
                            gfx = XGraphics.FromPdfPage(page);
                            yPoint = 40;
                        }

                        var productName = item.ProductName?.Length > 40 ? item.ProductName.Substring(0, 37) + "..." : item.ProductName;
                        
                        gfx.DrawString(productName ?? "-", fontRow, brushRow, margin + 10, yPoint);
                        
                        // Right-aligned numeric values
                        gfx.DrawString((item.Quantity ?? 0).ToString("N0"), fontRow, brushRow, new XRect(margin + 260, yPoint, 50, 15), XStringFormats.TopRight);
                        gfx.DrawString((item.UnitPrice ?? 0).ToString("N2"), fontRow, brushRow, new XRect(margin + 320, yPoint, 80, 15), XStringFormats.TopRight);
                        gfx.DrawString((item.NetLineTotalWithTax ?? 0).ToString("N2"), fontRow, brushRow, new XRect(margin + 410, yPoint, 100, 15), XStringFormats.TopRight);
                        
                        yPoint += 15;
                    }

                    // Order Subtotal
                    var orderTotal = order.TotalAmountWithTax ?? 0;
                    grandTotal += orderTotal;

                    gfx.DrawLine(XPens.LightGray, margin + 300, yPoint, pageWidth - margin, yPoint);
                    yPoint += 5;
                    gfx.DrawString($"Sipariş Toplamı: {orderTotal:C2}", fontBoldRow, XBrushes.Black, new XRect(0, yPoint, pageWidth - margin, 15), XStringFormats.TopRight);
                    yPoint += 25; // Space between orders
                }

                // Grand Total
                yPoint += 10;
                if (yPoint > page.Height - 50) 
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    yPoint = 40;
                }
                
                gfx.DrawLine(XPens.Black, margin, yPoint, pageWidth - margin, yPoint);
                yPoint += 10;
                gfx.DrawString($"GENEL TOPLAM: {grandTotal:C2}", fontTitle, XBrushes.Black, new XRect(0, yPoint, pageWidth - margin, 30), XStringFormats.TopRight);

                document.Save(stream, false);
                return File(stream.ToArray(), "application/pdf", $"Ekstre_{DateTime.Now:yyyyMMdd}.pdf");
            }
        }

        public IActionResult ExportExcel(string filter = "month", DateTime? startDate = null, DateTime? endDate = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Account");

            int userId = int.Parse(userIdStr);
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            if (kullanici == null) return RedirectToAction("Login", "Account");

            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;
            string firmaAdi = kullanici.TXTFIRMAADI ?? "";

            var orders = GetFilteredOrders(firmaKod, filter, startDate, endDate).ToList();

            var excelBytes = GenerateExcelBytes(orders, firmaAdi);
            var fileName = $"Finans_Ekstre_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpPost]
        public async Task<IActionResult> SendReconciliation(string filter = "month", DateTime? startDate = null, DateTime? endDate = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return Json(new { success = false, message = "Oturum bulunamadı." });

            int userId = int.Parse(userIdStr);
            var kullanici = _mskDb.TBL_KULLANICIs.FirstOrDefault(i => i.LNGIDENTITYKOD == userId);
            if (kullanici == null) return Json(new { success = false, message = "Kullanıcı bulunamadı." });
            
            if (string.IsNullOrEmpty(kullanici.TXTEMAIL))
                return Json(new { success = false, message = "Sistemde kayıtlı e-posta adresiniz bulunmuyor. Lütfen profil ayarlarına gidiniz." });

            int firmaKod = kullanici.LNGORTAKFIRMAKOD ?? 2;
            string firmaAdi = kullanici.TXTFIRMAADI ?? "";

            try 
            {
                var orders = GetFilteredOrders(firmaKod, filter, startDate, endDate).ToList();
                var excelBytes = GenerateExcelBytes(orders, firmaAdi);
                
                string subject = $"{firmaAdi} Mutabakat Formu";
                string message = $@"
                    <h3>Sayın {kullanici.TXTADSOYAD},</h3>
                    <p>Talep ettiğiniz finansal mutabakat formu ektedir.</p>
                    <p>İyi çalışmalar dileriz.</p>
                ";

                await _emailService.SendEmailAsync(kullanici.TXTEMAIL, subject, message, excelBytes, $"Mutabakat_{DateTime.Now:yyyyMMdd}.xlsx");

                return Json(new { success = true, message = $"Mutabakat formu başarıyla {kullanici.TXTEMAIL} adresine gönderildi." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Gönderim sırasında hata oluştu: " + ex.Message });
            }
        }

        private byte[] GenerateExcelBytes(List<UniCP.Models.MsK.SpModels.SSP_VARUNA_SIPARIS> orders, string firmaAdi)
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Ekstre");

                // Headers
                var headers = new[] { "Firma", "Sipariş No", "Tarih", "Müşteri", "Durum", "Tutar", "Bekleyen Bakiye" };
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
                foreach (var item in orders)
                {
                    worksheet.Cell(row, 1).Value = firmaAdi;
                    worksheet.Cell(row, 2).Value = item.SerialNumber ?? "";
                    worksheet.Cell(row, 3).Value = item.CreateOrderDate?.ToString("dd.MM.yyyy") ?? "";
                    worksheet.Cell(row, 4).Value = item.AccountTitle ?? "";
                    worksheet.Cell(row, 5).Value = item.OrderStatus ?? "";
                    worksheet.Cell(row, 6).Value = item.TotalAmountWithTax ?? 0;
                    worksheet.Cell(row, 7).Value = item.Bekleyen_Bakiye ?? 0;

                    // Formatting
                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00 ₺";
                    worksheet.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00 ₺";
                    
                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        private IQueryable<UniCP.Models.MsK.SpModels.SSP_VARUNA_SIPARIS> GetFilteredOrders(int firmaKod, string filter, DateTime? startDate, DateTime? endDate)
        {
            var varunaSiparisler = _mskDb.SP_VARUNA_SIPARIS(firmaKod).AsQueryable();
            var now = DateTime.Now;

            if (startDate.HasValue && endDate.HasValue)
            {
                 var end = endDate.Value.Date.AddDays(1).AddTicks(-1);
                 varunaSiparisler = varunaSiparisler.Where(o => o.CreateOrderDate >= startDate.Value && o.CreateOrderDate <= end);
            }
            else
            {
                switch (filter.ToLower())
                {
                    case "3months":
                        var threeMonthsAgo = now.AddMonths(-3);
                        varunaSiparisler = varunaSiparisler.Where(o => o.CreateOrderDate >= threeMonthsAgo);
                        break;
                    case "year":
                        var oneYearAgo = now.AddYears(-1);
                        varunaSiparisler = varunaSiparisler.Where(o => o.CreateOrderDate >= oneYearAgo);
                        break;
                    case "month":
                    default:
                        var oneMonthAgo = now.AddMonths(-1);
                        varunaSiparisler = varunaSiparisler.Where(o => o.CreateOrderDate >= oneMonthAgo);
                        break;
                }
            }
            return varunaSiparisler;
        }

        private void CalculateComparisonStats(int firmaKod, string filter, DateTime? startDate, DateTime? endDate, decimal currentTotal)
        {
             var now = DateTime.Now;
             decimal previousTotal = 0;
             string comparisonLabel = "";

            if (startDate.HasValue && endDate.HasValue)
            {
                 var end = endDate.Value.Date.AddDays(1).AddTicks(-1);
                 var duration = end - startDate.Value;
                 var prevStart = startDate.Value.Subtract(duration);
                 var prevEnd = startDate.Value.AddTicks(-1);
                 
                 var prevPeriodOrders = _mskDb.SP_VARUNA_SIPARIS(firmaKod).AsQueryable()
                    .Where(o => o.CreateOrderDate >= prevStart && o.CreateOrderDate <= prevEnd)
                    .ToList();
                 
                 previousTotal = prevPeriodOrders.Sum(x => x.TotalAmountWithTax ?? 0);
                 comparisonLabel = "önceki döneme göre";
            }
            else
            {
                var allOrders = _mskDb.SP_VARUNA_SIPARIS(firmaKod).AsQueryable();

                switch (filter.ToLower())
                {
                    case "3months":
                        var threeMonthsAgo = now.AddMonths(-3);
                        var prev3MonthsStart = threeMonthsAgo.AddMonths(-3);
                        var prev3MonthsEnd = threeMonthsAgo.AddTicks(-1);
                        previousTotal = allOrders
                            .Where(o => o.CreateOrderDate >= prev3MonthsStart && o.CreateOrderDate <= prev3MonthsEnd)
                            .Sum(x => x.TotalAmountWithTax ?? 0);
                        comparisonLabel = "geçen 3 aya göre";
                        break;

                    case "year":
                        var oneYearAgo = now.AddYears(-1);
                        var prevYearStart = oneYearAgo.AddYears(-1);
                        var prevYearEnd = oneYearAgo.AddTicks(-1);
                        previousTotal = allOrders
                            .Where(o => o.CreateOrderDate >= prevYearStart && o.CreateOrderDate <= prevYearEnd)
                            .Sum(x => x.TotalAmountWithTax ?? 0);
                        comparisonLabel = "geçen yıla göre";
                        break;

                    case "month":
                    default:
                        var oneMonthAgo = now.AddMonths(-1);
                        var prevMonthStart = oneMonthAgo.AddMonths(-1);
                        var prevMonthEnd = oneMonthAgo.AddTicks(-1);
                        previousTotal = allOrders
                            .Where(o => o.CreateOrderDate >= prevMonthStart && o.CreateOrderDate <= prevMonthEnd)
                            .Sum(x => x.TotalAmountWithTax ?? 0);
                        comparisonLabel = "geçen aya göre";
                        break;
                }
            }

            ViewBag.PreviousTotal = previousTotal;
            ViewBag.ComparisonLabel = comparisonLabel;

            double percentageChange = 0;
            if (previousTotal != 0)
            {
                percentageChange = (double)((currentTotal - previousTotal) / previousTotal) * 100;
            }
            else if (currentTotal > 0)
            {
                percentageChange = 100;
            }
            
            ViewBag.PercentageChange = percentageChange;
        }




        [HttpGet]
        public IActionResult GetOrderDetails(string orderId)
        {
            if (string.IsNullOrEmpty(orderId))
                return Json(new { success = false, message = "Sipariş ID eksik" });

            try
            {
                var siparisDetay = _mskDb.SP_VARUNA_SIPARIS_DETAY(orderId).ToList();
                return Json(new { success = true, details = siparisDetay });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Veri hatası: " + ex.Message });
            }
        }
    }
}
