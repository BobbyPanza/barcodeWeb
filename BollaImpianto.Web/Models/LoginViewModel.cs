using System.ComponentModel.DataAnnotations;

namespace BollaImpianto.Web.Models;

public sealed class LoginViewModel
{
    [Required]
    [Display(Name = "Operatore")]
    public string OpCod { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string? Password { get; set; }

    [Display(Name = "Ricordami")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
