namespace DAGoldManager.Core;

/// <summary>Pure regional bank choices shared by planning and simulated runs.</summary>
public static class RegionalBanks
{
    public const int NoamMap = 10061;
    public const long CourierTravelReserve = 100_000;

    public static long RequiredTravelGold(IReadOnlyList<CharacterState> producers, string courierName)
    {
        var courier = producers.Single(c => c.Name.Equals(courierName, StringComparison.OrdinalIgnoreCase));
        // The local banker is in Abel; external deliveries from Noam also reserve a fare.
        bool leavesRegion = courier.CraftBankMap == NoamMap ||
            producers.Any(c => c.CraftBankMap != courier.CraftBankMap);
        return leavesRegion ? CourierTravelReserve : 0;
    }
}
