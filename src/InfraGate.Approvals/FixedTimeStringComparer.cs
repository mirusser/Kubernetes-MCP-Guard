using System.Security.Cryptography;
using System.Text;

namespace InfraGate.Approvals;

public static class FixedTimeStringComparer
{
    public static bool Equals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
    }
}
