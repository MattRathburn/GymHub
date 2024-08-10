using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace GH.Identity.API.Models;

public class ApplicationUser : IdentityUser
{
    [Required]
    public string Street { get; set; }

    [Required]
    public string City { get; set; }

    [Required]
    public string State { get; set; }

    [Required]
    public string Country { get; set; }

    [Required]
    public string ZipCode { get; set; }

    [Required]
    public string FirstName { get; set; }

    [Required]
    public string LastName { get; set; }
}