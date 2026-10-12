using DAGoldManager.Game;
using Xunit;

namespace DAGoldManager.Tests;

public class DialogTextTests
{
    [Fact]
    public void RecordedGreetingCanBeReusedByAnotherCharacterWithoutChangingPriceText()
    {
        Assert.Equal(DialogText.ForCharacter("Hello Rowan. Price: 50500000 gold.", "Rowan"),
            DialogText.ForCharacter("Hello Aisling. Price: 50500000 gold.", "Aisling"));
        Assert.NotEqual(DialogText.ForCharacter("Price: 50500000 gold.", "Rowan"),
            DialogText.ForCharacter("Price: 60000000 gold.", "Aisling"));
    }
}
