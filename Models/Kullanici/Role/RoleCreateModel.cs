using System.ComponentModel.DataAnnotations;

namespace UniCP.Models.Kullanici.Role;

public class RoleCreateModel
{
    [Required]
    [StringLength(30)]
    [Display(Name = "Role Adı")]
    public string RoleAdi { get; set; } = null!;
}