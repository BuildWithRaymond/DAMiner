using DAGoldManager.Game;
using Arbiter.Net.Types;
using Xunit;

namespace DAGoldManager.Tests;

public class ExchangePacketTests
{
    [Theory]
    [InlineData(ExchangeClientActionType.BeginExchange, "0000126D53")]
    [InlineData(ExchangeClientActionType.AddItem, "0100126D531E")]
    [InlineData(ExchangeClientActionType.AddStackableItem, "0200126D531E02")]
    [InlineData(ExchangeClientActionType.Accept, "0500126D53")]
    public void MatchesManuallyCapturedDAOrganizerPacketShape(ExchangeClientActionType action, string expected)
    {
        var packet = ExchangePackets.Create(action, 0x00126D53, 30, 2);
        Assert.Equal(Convert.FromHexString(expected), packet.Data);
    }
}
