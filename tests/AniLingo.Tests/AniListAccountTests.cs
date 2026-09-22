using System.Text;
using AniLingo.Web.Features.Tracking;

namespace AniLingo.Tests;

[TestClass]
public sealed class AniListAccountTests
{
    [TestMethod]
    public void BuildsOfficialImplicitAuthorizationUrl()
    {
        var url = AniListAccountService.BuildAuthorizationUrl(12345);

        Assert.AreEqual(
            "https://anilist.co/api/v2/oauth/authorize?client_id=12345&response_type=token",
            url);
    }

    [TestMethod]
    public void RejectsInvalidClientId()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AniListAccountService.BuildAuthorizationUrl(0));
    }

    [TestMethod]
    public void ParsesViewerResponse()
    {
        const string json = """
        {
          "data": {
            "Viewer": {
              "id": 42,
              "name": "juloc",
              "avatar": {
                "medium": "https://example.invalid/avatar.png"
              }
            }
          }
        }
        """;

        var viewer = AniListAccountService.ParseViewerResponse(json);

        Assert.AreEqual(42, viewer.Id);
        Assert.AreEqual("juloc", viewer.Name);
        Assert.AreEqual("https://example.invalid/avatar.png", viewer.AvatarUrl);
    }

    [TestMethod]
    public void SurfacesGraphQlAuthenticationError()
    {
        const string json = """
        {
          "errors": [
            { "message": "Invalid token" }
          ],
          "data": null
        }
        """;

        var exception = Assert.ThrowsExactly<AniListAccountException>(
            () => AniListAccountService.ParseViewerResponse(json));

        StringAssert.Contains(exception.Message, "Invalid token");
    }

    [TestMethod]
    public void ReadsExpiryFromJwtPayload()
    {
        var expected = new DateTimeOffset(2027, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var header = Base64Url("""{"alg":"none"}""");
        var payload = Base64Url($"{{"exp":{expected.ToUnixTimeSeconds()}}}");
        var token = $"{header}.{payload}.signature";

        var expiry = AniListAccountService.TryReadTokenExpiry(token);

        Assert.AreEqual(expected, expiry);
    }

    [TestMethod]
    public void UnknownTokenFormatHasNoExpiry()
    {
        Assert.IsNull(AniListAccountService.TryReadTokenExpiry("opaque-token"));
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
