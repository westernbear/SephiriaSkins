using System.Security.Cryptography;
using System.Text;
namespace SephiriaSkins.Core;
public static class Keys
{
    public static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    public static string Animation(string role, string set, string state, int fps, bool repeat,
        IEnumerable<int> indices, IEnumerable<string> sprites)
    {
        var signature = set + "\n" + state + "\n" + fps + "\n" + (repeat ? "1" : "0") + "\n" +
            string.Join(",", indices) + "\n" + string.Join("\n", sprites);
        return role + "/" + set + "/" + state + "/" + Hash(Encoding.UTF8.GetBytes(signature)).Substring(0, 12);
    }
}
