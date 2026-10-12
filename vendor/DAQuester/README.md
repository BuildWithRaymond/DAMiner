# DAQuester integration sources

Core, game protocol, launcher, walker, map graph, and functional collision data were copied from the local DAQuester source on 2026-10-11. Original MIT and third-party notices are retained here. No original application's files were edited.

The source repository's base HEAD was `bdd0bb09afaa41cd554ce748862c8b88f956c4f7`. This is a copy of its local working-tree files, not a runtime reference to that checkout.

Recording hooks consist of `StateTracker` publishing otherwise-unhandled packets to observers, and `ProxyConnection` publishing an allowlist of native gameplay actions and interrupting automation on manual exchanges. These hooks support setup recording and trade confirmation. They exclude login and chat traffic.

Navigation additionally supports an optional `GameCommand.TargetRow`. `NpcApproach.Resolve` keeps substitute destinations on that row and excludes blocked/occupied/trigger tiles; `GameSession` reselects when another player takes the chosen floor tile. Gold Manager uses this for Abel's public bank floor. Existing commands without a row retain ordinary navigation. World-map selection and the acknowledged movement protocol remain the upstream implementation.

`DAGoldManager.Game.ExchangePackets` implements the corrected exchange shape evidenced by DAOrganizer's synthetic capture tests: AddItem has a slot byte and no quantity byte. Stack quantity follows only AddStackableItem. Gold Manager's exchange reader additionally accepts the documented optional completion subtype without inventing inventory state.
