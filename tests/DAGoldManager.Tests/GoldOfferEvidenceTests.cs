using DAGoldManager.Game;
using Xunit;

namespace DAGoldManager.Tests;

public class GoldOfferEvidenceTests
{
    static TradeView Donor => new(true, 2, "Receiver", 0, 0, 0, 0, false, false, false, false, false, 0);
    static TradeView Recipient => new(true, 1, "Donor", 0, 0, 0, 12_000_000, false, false, false, false, false, 0);

    [Fact]
    public void ReceiverConfirmsExactGoldOfferWithoutDonorEcho()
    {
        Assert.True(GoldOfferEvidence.IsConfirmed(Donor, Recipient, 12_000_000));
    }

    [Fact]
    public void ConflictingDonorEchoIsNeverIgnored()
    {
        Assert.False(GoldOfferEvidence.IsConfirmed(Donor with { OwnGold = 1 }, Recipient, 12_000_000));
    }

    [Fact]
    public void ExtraItemsOrReverseGoldInvalidateOffer()
    {
        Assert.False(GoldOfferEvidence.IsConfirmed(Donor with { OwnGold = 12_000_000, OwnBars = 1 }, Recipient, 12_000_000));
        Assert.False(GoldOfferEvidence.IsConfirmed(Donor with { OwnGold = 12_000_000 }, Recipient with { OwnGold = 1 }, 12_000_000));
    }

    [Fact]
    public void CancelledOrIncompleteReceiverOfferCannotAuthorizeAcceptance()
    {
        Assert.False(GoldOfferEvidence.IsConfirmed(Donor, Recipient with { Cancelled = true }, 12_000_000));
        Assert.False(GoldOfferEvidence.IsConfirmed(Donor, Recipient with { OtherGold = 11_999_999 }, 12_000_000));
        Assert.False(GoldOfferEvidence.IsConfirmed(Donor, Recipient with { Started = false }, 12_000_000));
    }
}
