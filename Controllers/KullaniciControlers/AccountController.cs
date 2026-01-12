using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using NuGet.Common;
using System.Security.Claims;
using System.Threading.Tasks;
using UniCP.DbData;
using UniCP.Models;
using UniCP.Models.Kullanici;
using UniCP.Models.Kullanici.Account;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace UniCP.Controllers;

public class AccountController : Controller
{
    private UserManager<AppUser> _userManager;
    private SignInManager<AppUser> _signInManager;
    private IEmailService _emailService;
    private readonly MskDbContext _mskDb;



    public AccountController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IEmailService emailService , MskDbContext mskDb)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailService = emailService;
        _mskDb = mskDb;
    }
    public ActionResult Create()
    {
        return View();
    }

    [HttpPost]
    public async Task<ActionResult> Create(AccountCreateModel model)
    {
        if (ModelState.IsValid)
        {
            var user = new AppUser { UserName = model.Email, Email = model.Email, AdSoyad = model.AdSoyad };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                AppUser? user2 = await _userManager.FindByEmailAsync(model.Email);
                if (user2 != null)
                {
                   
                    var confirmToken = await _userManager.GenerateEmailConfirmationTokenAsync(user2);
                   
                   
                    var url = Url.Action("EmailConfirmedToken", "Account", new { userId = user2.Id, confirmToken });

                    //var link = $"<a href='http://localhost:5162{url}'>Mail Adresini Doğrula</a>";
                    MailBody mb = new MailBody();

                    string sunucu = _mskDb.PARAMETRELERs.Where(i => i.ParametreAdi == "UYGULAMAROOTMAP").Select(i => i.Deger).FirstOrDefault();

                    var link = mb.dogrulamamail(user2.UserName!, sunucu + url);



                    await _emailService.SendEmailAsync(user2.Email!, "Email Doğrulama", link);

                    TempData["Mesaj"] = "Email Doğrulama Maili Gönredildi Mail Hesabınızı Kontrol Edin";

                }

                return RedirectToAction("Login"); 
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
        }
        return View(model);
    }

    public ActionResult EmailConfirmToken()
    {
        return View();
    }

    [HttpPost]
    public async Task<ActionResult> EmailConfirmToken(string email)
    {
       
        AppUser? user2 = await _userManager.FindByEmailAsync(email);
        if (user2 != null)
        {

            var confirmToken = await _userManager.GenerateEmailConfirmationTokenAsync(user2);


            var url = Url.Action("EmailConfirmedToken", "Account", new { userId = user2.Id, confirmToken });

            string sunucu = _mskDb.PARAMETRELERs.Where(i => i.ParametreAdi == "UYGULAMAROOTMAP").Select(i => i.Deger).FirstOrDefault();

            MailBody mb = new MailBody();

            var link = mb.dogrulamamail(user2.UserName!, sunucu + url); 
                //$"<a href='http://localhost:5162{url}'>Mail Adresini Doğrula</a>";

            await _emailService.SendEmailAsync(user2.Email!, "Email Doğrulama", link);

            TempData["Mesaj"] = "Email Doğrulama Maili Gönredildi Mail Hesabınızı Kontrol Edin";

        }
        else
        {
            TempData["Mesaj"] = "Email Adresine Bağlı Kullanıcı Bulunamadı";

            return RedirectToAction("Login");
        }

        return RedirectToAction("Login");
    }


    public async Task<ActionResult> EmailConfirmedToken(string userId, string confirmToken)
    {
        AppUser? user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
           
            IdentityResult result = await _userManager.ConfirmEmailAsync(user, confirmToken);
            if (result.Succeeded)
            {

                await _userManager.UpdateSecurityStampAsync(user);
                TempData["Mesaj"] = "Email Adresiniz Doğrulandı";
            }
                
            
        }
        return RedirectToAction("Login");
    }

    public ActionResult Login()
    {
        if (User?.Identity?.IsAuthenticated ?? false)
        {
            return RedirectToAction("Index", "Musteri");
        }
        return View();
    }

    [HttpPost]
    public async Task<ActionResult> Login(AccountLoginModel model, string? returnUrl)
    {
        if (ModelState.IsValid)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user != null)
            {
                // Verify password first without signing in
                var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, true);

                if (result.Succeeded)
                {
                    // Fetch Company Name from TBL_KULLANICI
                    var firmaAdi = _mskDb.TBL_KULLANICIs
                        .Where(x => x.LNGIDENTITYKOD == user.Id)
                        .Select(x => x.TXTFIRMAADI)
                        .FirstOrDefault();

                    var claims = new List<Claim>();
                    if (!string.IsNullOrEmpty(firmaAdi))
                    {
                        claims.Add(new Claim("FirmaAdi", firmaAdi));
                    }

                    // Sign in with additional claims
                    await _signInManager.SignInWithClaimsAsync(user, model.BeniHatirla, claims);

                    await _userManager.ResetAccessFailedCountAsync(user);
                    await _userManager.SetLockoutEndDateAsync(user, null);

                    if (!string.IsNullOrEmpty(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }
                    else
                    {
                        int k_tip =  Convert.ToInt16(_mskDb.TBL_KULLANICIs.Where(i=>i.LNGIDENTITYKOD== user.Id).Select(i=>i.LNGKULLANICITIPI).FirstOrDefault());
                            
                        if (k_tip == 2)
                        {
                            return RedirectToAction("Index", "Musteri");
                        } else if (k_tip == 1)
                        {
                            return RedirectToAction("Index", "Yonetim");
                        }

                        return RedirectToAction("Index", "Home");
                    }

                }
                else if (result.IsLockedOut)
                {
                    var lockoutDate = await _userManager.GetLockoutEndDateAsync(user);
                    var timeLeft = lockoutDate.Value - DateTime.UtcNow;
                    ModelState.AddModelError("", $"Hesabınız kitlendi. Lütfen {timeLeft.Minutes + 1} dakika sonra tekrar deneyiniz.");
                    TempData["Mesaj"] = $"Hesabınız kitlendi. Lütfen {timeLeft.Minutes + 1} dakika sonra tekrar deneyiniz.";
                }
                else if (result.IsNotAllowed)
                {
                    ModelState.AddModelError("", "Doğrulanmamış EMail");
                    TempData["Mesaj"] = "Doğrulanmamış EMail";

                } else
                {
                    ModelState.AddModelError("", "Hatalı parola");
                    TempData["Mesaj"] = "Hatalı parola";
                }
            }
            else
            {
                ModelState.AddModelError("", "Hatalı email");
                TempData["Mesaj"] = "Hatalı email";
            }
        }
        return View(model);
    }

    [Authorize]
    public async Task<ActionResult> LogOut()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login", "Account");
    }

    [Authorize]
    public ActionResult Settings()
    {
        return View();
    }

    [Authorize]
    public async Task<ActionResult> EditUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);

        if (user == null)
        {
            return RedirectToAction("Login", "Account");
        }

        return View(new AccountEditUserModel
        {
            AdSoyad = user.AdSoyad,
            Email = user.Email!
        });
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult> EditUser(AccountEditUserModel model)
    {
        if (ModelState.IsValid)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId!);

            if (user != null)
            {
                user.Email = model.Email;
                user.AdSoyad = model.AdSoyad;

                var result = await _userManager.UpdateAsync(user);

                if (result.Succeeded)
                {
                    TempData["Mesaj"] = "Bilgileriniz güncellendi";
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
        }
        return View(model);
    }

    [Authorize]
    public ActionResult ChangePassword()
    {
        return View();
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult> ChangePassword(AccountChangePasswordModel model)
    {
        if (ModelState.IsValid)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId!);

            if (user != null)
            {
                var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.Password);

                if (result.Succeeded)
                {
                    TempData["Mesaj"] = "Parolanız güncellendi";
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
        }
        return View(model);
    }

    public ActionResult AccessDenied()
    {
        return View();
    }
    public ActionResult ForgotPassword()
    {
        return View();
    }

    [HttpPost]
    public async Task<ActionResult> ForgotPassword(string email)
    {
        if (string.IsNullOrEmpty(email))
        {
            TempData["Mesaj"] = "Eposta adresinizi giriniz";
            return View();
        }

        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            TempData["Mesaj"] = "Bu eposta adresi kayıtlı değil";
            return View();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        var url = Url.Action("ResetPassword", "Account", new { userId = user.Id, token });

        //  var link = $"<a href='http://localhost:5162{url}'>Şifre Yenile</a>";
        string sunucu = _mskDb.PARAMETRELERs.Where(i => i.ParametreAdi == "UYGULAMAROOTMAP").Select(i => i.Deger).FirstOrDefault();

        MailBody mb = new MailBody();

        var link = mb.resetlememail(user.UserName!, sunucu + url);

        await _emailService.SendEmailAsync(user.Email!, "Parola Sıfırlama", link);

        TempData["Mesaj"] = "Eposta adresine gönderilen link ile şifreni sıfırlayabilirsin.";

        return RedirectToAction("Login");
    }

    public async Task<ActionResult> ResetPassword(string userId, string token)
    {
        if (userId == null || token == null)
        {
            return RedirectToAction("Login");
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return RedirectToAction("Login");
        }

        var model = new AccountResetPasswordModel
        {
            Token = token,
            Email = user.Email!
        };

        return View(model);
    }

    [HttpPost]
    public async Task<ActionResult> ResetPassword(AccountResetPasswordModel model)
    {
        if (ModelState.IsValid)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user == null)
            {
                return RedirectToAction("Login");
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);

            if (result.Succeeded)
            {
                TempData["Mesaj"] = "Şifreniz güncellendi";
                return RedirectToAction("Login");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
        }
        return View(model);
    }
}