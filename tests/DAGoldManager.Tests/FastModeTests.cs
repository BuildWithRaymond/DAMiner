using System.Collections.Immutable;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using DAGoldManager.Core;
using DAGoldManager.Storage;
using DAGoldManager.UI;
using DAQuester.Core;
using DAQuester.DarkAges;
using DAQuester.DarkAges.Navigation;
using DAQuester.DarkAges.Transport;
using Xunit;

namespace DAGoldManager.Tests;

public class FastModeTests
{
    [Fact]
    public void ExistingRosterEntriesDefaultToFastMode()
    {
        var character = JsonSerializer.Deserialize<SavedCharacter>("{\"Name\":\"Fictional\"}")!;
        Assert.Contains("\"FastMode\":true", JsonSerializer.Serialize(character));
    }

    [Fact]
    public void DisabledFastModeSurvivesProfileReload()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DAMinerFastTests", Guid.NewGuid().ToString("N"));
        var character = JsonSerializer.Deserialize<SavedCharacter>("{\"Name\":\"Fictional\",\"FastMode\":false}")!;
        var store = new LocalStore(directory);
        store.Save(new([character], new(), new()));
        Assert.Contains("\"FastMode\":false", JsonSerializer.Serialize(Assert.Single(store.Load().Characters)));
    }

    [Fact]
    public void ConnectedOwnedCharacterDoesNotSlowFastWalking()
    {
        using var client = new TcpClient();
        using var remote = new TcpClient();
        var connection = new ProxyConnection(client, remote, 1, _ => Task.FromResult(0));
        var maps = new MapRepository(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json"));
        var session = new GameSession(connection, maps) { FastMode = true };
        session.IsOwnCharacter = name => name == "Owned";
        var state = new CharacterSnapshot { Connected = true, Id = 1, Name = "Fictional", Map = 502, Position = new(10, 10),
            Entities = ImmutableDictionary<uint, Entity>.Empty.Add(2, new(2, "Owned", new(11, 10), true) { IsPlayer = true }) };
        var cadence = typeof(GameSession).GetMethod("MovementCadence", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(160, cadence.Invoke(session, [state, null]));
    }

    [Theory]
    [InlineData(502, 11, false, 430)]
    [InlineData(502, 31, false, 160)]
    [InlineData(502, 11, true, 160)]
    [InlineData(10056, 11, true, 430)]
    [InlineData(10180, 31, true, 430)]
    public void CadenceMatchesDAQuesterAreaAndPlayerRules(int map, int playerX, bool connectedOwned, int expected)
    {
        using var client = new TcpClient();
        using var remote = new TcpClient();
        var session = new GameSession(new(client, remote, 1, _ => Task.FromResult(0)),
            new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json")))
            { FastMode = true, IsOwnCharacter = _ => connectedOwned };
        var state = new CharacterSnapshot { Connected = true, Id = 1, Name = "Fictional", Map = map, Position = new(10, 10),
            Entities = ImmutableDictionary<uint, Entity>.Empty.Add(2, new(2, "Other", new(playerX, 10), true) { IsPlayer = true }) };
        Assert.Equal(expected, Pace(session, state));
        if (playerX == 31 && map == 502 && !connectedOwned)
            Assert.Equal(430, Pace(session, state, new(11, 10)));
        session.FastMode = false;
        Assert.Equal(400, Pace(session, state));
    }

    [Fact]
    public void UnknownPositionAndDisconnectedStateUseNormalPace()
    {
        using var client = new TcpClient();
        using var remote = new TcpClient();
        var session = new GameSession(new(client, remote, 1, _ => Task.FromResult(0)),
            new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "content", "routes.json"))) { FastMode = true };
        Assert.Equal(430, Pace(session, new() { Connected = true, Id = 1, Map = 502 }));
        Assert.Equal(430, Pace(session, new() { Connected = false, Id = 1, Map = 502, Position = new(10, 10) }));
    }

    static int Pace(GameSession session, CharacterSnapshot state, Tile? projected = null) =>
        (int)typeof(GameSession).GetMethod("MovementCadence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, [state, projected])!;

    [Fact]
    public void DashboardSwitchUpdatesOnlyTheSelectedCharactersPreference()
    {
        var model = new DashboardModel(true, Path.Combine(Path.GetTempPath(), "DAMinerFastTests", Guid.NewGuid().ToString("N")), "");
        string name = model.Characters[0].Name;
        model.SetFastMode(name, false);
        Assert.False(model.Characters[0].FastMode);
        Assert.All(model.Characters.Skip(1), c => Assert.True(c.FastMode));
        Assert.Equal("normal walking", model.FastModeDescription(name));
        Assert.Throws<InvalidOperationException>(() => model.SetFastMode("NotSaved", true));
    }
}
