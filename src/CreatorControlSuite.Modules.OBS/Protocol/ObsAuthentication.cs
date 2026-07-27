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
        byte[] secretInput = Encoding.UTF8.GetBytes(password + salt);
        byte[] secretHash = SHA256.HashData(secretInput);
        string secret = Convert.ToBase64String(secretHash);

        byte[] authenticationInput = Encoding.UTF8.GetBytes(secret + challenge);
        byte[] authenticationHash = SHA256.HashData(authenticationInput);

        return Convert.ToBase64String(authenticationHash);
    }
}
