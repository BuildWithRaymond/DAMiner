namespace DAGoldManager.Game;

/// <summary>Requires an exact one-way gold offer before accepting a managed-character exchange.</summary>
public static class GoldOfferEvidence
{
    public static bool IsConfirmed(TradeView donor, TradeView recipient, uint gold) =>
        gold > 0 && donor.Started && recipient.Started &&
        !donor.Cancelled && !recipient.Cancelled && !donor.UnexpectedItem && !recipient.UnexpectedItem &&
        donor.OwnBars == 0 && donor.OtherBars == 0 && recipient.OwnBars == 0 && recipient.OtherBars == 0 &&
        donor.OtherGold == 0 && recipient.OwnGold == 0 && recipient.OtherGold == gold &&
        (donor.OwnGold == 0 || donor.OwnGold == gold);
}
