namespace GH.Identity.API.Models.AccountViewModels;

public record AddPhoneNumberViewModel
{
    [Required]
    [Phone]
    [Display(Name = "Phone number")]
    public string PhoneNumber { get; init; }
}
