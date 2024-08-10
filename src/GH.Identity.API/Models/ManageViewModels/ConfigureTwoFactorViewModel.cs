namespace GH.Identity.API.Models.AccountViewModels;

public record ConfigureTwoFactorViewModel
{
    public string SelectedProvider { get; init; }

    public ICollection<SelectListItem> Providers { get; init; }
}
