using System.Security.Cryptography;

namespace InfraGate.Approvals.AccessCodes;

public static class ApprovalAccessCodeGenerator
{
    public static string Generate()
    {
        Span<char> code = stackalloc char[ApprovalConventions.AccessCodes.CodeLength];
        for (int i = 0; i < code.Length; i++)
        {
            int index = RandomNumberGenerator.GetInt32(ApprovalConventions.AccessCodes.Alphabet.Length);
            code[i] = ApprovalConventions.AccessCodes.Alphabet[index];
        }

        return new string(code);
    }
}
