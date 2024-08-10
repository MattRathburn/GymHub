namespace GH.Identity.API.Models.AccountViewModels;

public record VerifyPhoneNumberViewModel
{
    [Required]
    public string Code { get; init; }

    [Required]
    [Phone]
    [Display(Name = "Phone number")]
    public string PhoneNumber { get; init; }
}
