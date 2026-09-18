using Floppy.Core;

namespace Floppy.Core.Tests;

public class WifiInfoTests
{
    private const string EnglishSample = """

        There is 1 interface on the system:

            Name                   : Wi-Fi
            Description            : Intel(R) Wi-Fi 6 AX201 160MHz
            GUID                   : e1f5c2a0-1234-5678-9abc-def012345678
            Physical address       : aa:bb:cc:dd:ee:ff
            State                  : connected
            SSID                   : Schulhof-Netz
            BSSID                  : 11:22:33:44:55:66
            Network type           : Infrastructure
            Radio type             : 802.11ac
            Authentication         : WPA2-Personal
            Cipher                 : CCMP
            Connectivity mode      : Profile
            Channel                : 6
            Profile                : Schulhof-Netz
        """;

    private const string GermanSample = """

        Auf dem System ist eine Schnittstelle vorhanden:

            Name                   : WLAN
            Beschreibung           : Intel(R) Wi-Fi 6 AX201 160MHz
            GUID                   : e1f5c2a0-1234-5678-9abc-def012345678
            Physische Adresse      : aa:bb:cc:dd:ee:ff
            Status                 : Verbunden
            SSID                   : Zuhause-24
            BSSID                  : 11:22:33:44:55:66
            Netzwerktyp            : Infrastruktur
            Profil                 : Zuhause-24
        """;

    [Fact]
    public void Reads_ssid_from_english_output() => Assert.Equal("Schulhof-Netz", WifiInfo.ParseSsid(EnglishSample));

    [Fact]
    public void Reads_ssid_from_german_output() => Assert.Equal("Zuhause-24", WifiInfo.ParseSsid(GermanSample));

    [Fact]
    public void Does_not_confuse_bssid_with_ssid()
    {
        const string onlyBssid = "    BSSID : 11:22:33:44:55:66\n    State : connected";
        Assert.Null(WifiInfo.ParseSsid(onlyBssid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("There is 0 interfaces on the system.")]
    [InlineData("    SSID                   : ")]
    public void Returns_null_when_nothing_usable_is_found(string text) => Assert.Null(WifiInfo.ParseSsid(text));
}
