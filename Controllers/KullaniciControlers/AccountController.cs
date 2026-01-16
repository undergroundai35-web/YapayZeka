using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
                    // Fetch Company Name & User Type from TBL_KULLANICI
                    var dbUser = _mskDb.TBL_KULLANICIs
                        .Where(x => x.LNGIDENTITYKOD == user.Id)
                        .Select(x => new { x.TXTFIRMAADI, x.LNGKULLANICITIPI })
                        .FirstOrDefault();

                    var claims = new List<Claim>();
                    if (dbUser != null)
                    {
                        if (!string.IsNullOrEmpty(dbUser.TXTFIRMAADI))
                        {
                            claims.Add(new Claim("FirmaAdi", dbUser.TXTFIRMAADI));
                        }
                        // Add UserType Claim (1=Admin, 2=Customer)
                        claims.Add(new Claim("UserType", dbUser.LNGKULLANICITIPI?.ToString() ?? "0"));
                    }

                    // Sign in with additional claims
                    await _signInManager.SignInWithClaimsAsync(user, model.BeniHatirla, claims);

                    await _userManager.ResetAccessFailedCountAsync(user);
                    await _userManager.SetLockoutEndDateAsync(user, null);

                    // Force Password Change Check
                    // Check directly against the user object we just signed in
                    var userClaims = await _userManager.GetClaimsAsync(user);
                    if (userClaims.Any(c => c.Type == "ForcePasswordChange" && c.Value == "true"))
                    {
                        return RedirectToAction("ChangePassword");
                    }

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
                            return RedirectToAction("Index", "Musteri");
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
                    // Remove ForcePasswordChange claim if exists
                    var claims = await _userManager.GetClaimsAsync(user);
                    var forceClaim = claims.FirstOrDefault(c => c.Type == "ForcePasswordChange");
                    if (forceClaim != null)
                    {
                        await _userManager.RemoveClaimAsync(user, forceClaim);
                    }

                    TempData["Mesaj"] = "Parolanız güncellendi. Lütfen yeni şifrenizle giriş yapınız.";

                    // Sign out to force re-login with new password
                    await _signInManager.SignOutAsync();

                    return RedirectToAction("Login", "Account");

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

        // 1. Try to find in AspNetUsers by Email
        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            // 2. Try to find in AspNetUsers by UserName
            user = await _userManager.FindByNameAsync(email);
        }

        if (user == null)
        {
            // 3. Try to find in TBL_KULLANICI (Business Table)
            var dbUser = await _mskDb.TBL_KULLANICIs.FirstOrDefaultAsync(x => x.TXTEMAIL == email);
            if (dbUser != null && dbUser.LNGIDENTITYKOD.HasValue)
            {
                user = await _userManager.FindByIdAsync(dbUser.LNGIDENTITYKOD.Value.ToString());
            }
        }

        if (user == null)
        {
            TempData["Mesaj"] = "Girdiğiniz mail adresi sistemde kayıtlı değil. Lütfen geçerli bir mail adresi giriniz."; 
            return View(); // Stay on the same page to allow retry
        }

        // Generate Random Password
        string randomPassword = GenerateRandomPassword(8);
        
        // Reset Password to Random Password
        // Bypass token logic for forced administrative reset
        // string randomPassword = GenerateRandomPassword(8); // ALREADY DEFINED ABOVE

        // Remove password if exists
        if (await _userManager.HasPasswordAsync(user))
        {
             await _userManager.RemovePasswordAsync(user);
        }
        
        // Add new random password
        var result = await _userManager.AddPasswordAsync(user, randomPassword);

        if (result.Succeeded)
        {
            // Remove ForcePasswordChange claim if exists
            var claims = await _userManager.GetClaimsAsync(user);
            if (!claims.Any(c => c.Type == "ForcePasswordChange"))
            {
                await _userManager.AddClaimAsync(user, new Claim("ForcePasswordChange", "true"));
            }

            try 
            {
                // Send Email
                // Use the input 'email' because we verified it belongs to this user (either via Identity or TBL_KULLANICI)
                // and user.Email might be different or outdated in Identity.
                MailBody mb = new MailBody();
                var body = mb.TemporaryPasswordEmail(user.UserName!, randomPassword);
                
                // Use input 'email' explicitly
                await _emailService.SendEmailAsync(email, "Geçici Şifreniz", body);
                
                TempData["Mesaj"] = $"Geçici şifreniz '{email}' adresine gönderildi. (Lütfen Spam/Gereksiz kutusunu da kontrol ediniz)";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                 // Create a log or feedback mechanism here
                 TempData["Mesaj"] = "Şifre oluşturuldu ancak mail gönderilemedi: " + ex.Message;
                 return RedirectToAction("Login");
            }
        }
        else 
        {
             string errors = string.Join("; ", result.Errors.Select(e => e.Description));
             TempData["Mesaj"] = "Şifre sıfırlama hatası: " + errors;
             return View();
        }
    }

    private string GenerateRandomPassword(int length)
    {
        const string valid = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*";
        System.Text.StringBuilder res = new System.Text.StringBuilder();
        Random rnd = new Random();
        for (int i = 0; i < length; i++)
        {
            res.Append(valid[rnd.Next(valid.Length)]);
        }
        // Ensure complexity requirements usually needed by Identity
        res.Append("A1!"); 
        return res.ToString();
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