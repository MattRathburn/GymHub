using GymHub.BuildingBlocks.Storage;

namespace GH.UnitTests;

public class UploadRulesTests
{
    private const string Prefix = "members/user-1/avatar/";

    [Fact]
    public void Issued_keys_are_accepted()
    {
        var key = UploadRules.NewKey(Prefix, "image/png");

        Assert.Matches("^members/user-1/avatar/[0-9a-f]{32}\\.png$", key);
        Assert.True(UploadRules.IsIssuedKey(key, Prefix));
    }

    [Theory]
    [InlineData("members/user-1/avatar/../../../programs/0123456789abcdef0123456789abcdef/x.mp4")]
    [InlineData("members/user-1/avatar/0123456789abcdef0123456789abcdef/../x.png")]
    [InlineData("members/user-1/avatar/0123456789abcdef0123456789abcdef.svg")]
    [InlineData("members/user-1/avatar/0123456789ABCDEF0123456789ABCDEF.png")]
    [InlineData("members/user-1/avatar/0123456789abcdef0123456789abcde.png")]
    [InlineData("members/user-1/avatar/0123456789abcdef0123456789abcdef.png.html")]
    [InlineData("members/user-2/avatar/0123456789abcdef0123456789abcdef.png")]
    [InlineData("members/user-1/avatar/")]
    public void Anything_else_under_or_outside_the_prefix_is_refused(string key) =>
        Assert.False(UploadRules.IsIssuedKey(key, Prefix));
}
