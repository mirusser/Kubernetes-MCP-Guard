using System.Security.Cryptography;

namespace InfraGate.Approvals;

public static class ApprovalIds
{
    private const int DefaultRandomByteCount = 16;
    private const int ChallengeRandomByteCount = 32;

    public static string NewChallengeId() => NewHexId(ChallengeRandomByteCount);

    public static string NewChallengeOutcomeId() => NewHexId(DefaultRandomByteCount);

    public static string NewExecutionAttemptId() => NewHexId(DefaultRandomByteCount);

    public static string NewExecutionOutcomeId() => NewHexId(DefaultRandomByteCount);

    public static string NewGrantId() => NewHexId(DefaultRandomByteCount);

    public static string NewPlanId() => NewHexId(DefaultRandomByteCount);

    private static string NewHexId(int byteCount)
    {
        var bytes = new byte[byteCount];
        RandomNumberGenerator.Fill(bytes);

        return Convert.ToHexString(bytes).ToUpperInvariant();
    }
}
