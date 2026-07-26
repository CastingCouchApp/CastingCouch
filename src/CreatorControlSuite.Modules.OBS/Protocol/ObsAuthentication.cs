using System.Security.Cryptography;
using System.Text;

namespace CreatorControlSuite.Modules.OBS.Protocol;

internal static class ObsAuthentication
{
    public static string CreateResponse(
        string password,
        string salt,
        string challenge)
    {
        var secretInput = Encoding.UTF8.GetBytes(password + salt);
        var secretHash = SHA256.HashData(secretInput);
        var secret = Convert.ToBase64String(secretHash);

        var authenticationInput = Encoding.UTF8.GetBytes(secret + challenge);
        var authenticationHash = SHA256.HashData(authenticationInput);

        return Convert.ToBase64String(authenticationHash);
    }
}
