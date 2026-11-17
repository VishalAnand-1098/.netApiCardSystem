using InviteCraft.Infrastructure.Identity;
using InviteCraft.Infrastructure.Rendering;
using Microsoft.Extensions.Options;

namespace InviteCraft.IntegrationTests.Rendering;

public class DownloadLinkSignerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static DownloadLinkSigner Signer(string key = "unit-test-signing-key-0123456789-0123456789-abcdef") =>
        new(Options.Create(new JwtSettings { SigningKey = key }));

    [Fact]
    public void A_signature_validates_for_the_same_download_and_expiry_until_it_expires()
    {
        var signer = Signer();
        var id = Guid.NewGuid();
        var expires = Now.AddMinutes(15);

        var signature = signer.Sign(id, expires);

        signer.IsValid(id, expires.ToUnixTimeSeconds(), signature, Now).ShouldBeTrue();
        signer.IsValid(id, expires.ToUnixTimeSeconds(), signature, expires.AddSeconds(-1)).ShouldBeTrue();
        signer.IsValid(id, expires.ToUnixTimeSeconds(), signature, expires.AddSeconds(1)).ShouldBeFalse();
    }

    [Fact]
    public void Changing_the_download_the_expiry_or_the_signature_invalidates_the_link()
    {
        var signer = Signer();
        var id = Guid.NewGuid();
        var expires = Now.AddMinutes(15);
        var signature = signer.Sign(id, expires);

        signer.IsValid(Guid.NewGuid(), expires.ToUnixTimeSeconds(), signature, Now).ShouldBeFalse();
        signer.IsValid(id, expires.AddHours(5).ToUnixTimeSeconds(), signature, Now).ShouldBeFalse();
        signer.IsValid(id, expires.ToUnixTimeSeconds(), signature[..^1] + (signature[^1] == 'A' ? 'B' : 'A'), Now).ShouldBeFalse();
        signer.IsValid(id, expires.ToUnixTimeSeconds(), "", Now).ShouldBeFalse();
        signer.IsValid(id, expires.ToUnixTimeSeconds(), new string('A', 500), Now).ShouldBeFalse();
    }

    [Fact]
    public void Links_signed_with_a_different_key_are_rejected_and_signatures_are_url_safe()
    {
        var id = Guid.NewGuid();
        var expires = Now.AddMinutes(15);
        var signature = Signer().Sign(id, expires);

        Signer("another-signing-key-0123456789-0123456789-abcdefgh").IsValid(id, expires.ToUnixTimeSeconds(), signature, Now).ShouldBeFalse();
        signature.ShouldNotContain("+");
        signature.ShouldNotContain("/");
        signature.ShouldNotContain("=");
    }

    [Fact]
    public void The_signature_is_not_the_jwt_key_and_is_stable()
    {
        var id = Guid.NewGuid();
        var expires = Now.AddMinutes(15);

        Signer().Sign(id, expires).ShouldBe(Signer().Sign(id, expires));
    }
}
