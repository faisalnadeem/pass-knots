namespace VaultApp.Configuration;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "VaultApp";
    public string Audience { get; set; } = "VaultApp";
    public int ExpiresMinutes { get; set; } = 60;
}
