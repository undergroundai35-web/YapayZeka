using Microsoft.AspNetCore.Identity;

namespace UniCP.Models.Kullanici;

public class AppUser : IdentityUser<int>
{
    public string AdSoyad { get; set; } = null!;
}