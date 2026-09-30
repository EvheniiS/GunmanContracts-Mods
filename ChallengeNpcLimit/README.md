# Challenge NPC Limit

Separate MelonLoader mod for the enemy count selected on the contract terminal's takedown challenges. The game's `+` button normally stops at the selected challenge's `maxTakedownEnemies` value (30 on the observed challenge). This mod raises that selection limit to 60 by default. It does not change how many enemies can be alive at once in wave or last stand modes.

Build: `dotnet build ChallengeNpcLimit/ChallengeNpcLimit.csproj -c Release -p:GameDir="<game installation>"`. Install `ChallengeNpcLimit.dll` in the game's `Mods` folder and restart the game.

Settings in `UserData/MelonPreferences.cfg` under `[ChallengeNpcLimit]`:

- `Enabled = true`: enable the higher limit.
- `MaxEnemies = 60`: desired maximum selectable enemy count. Use an integer above 30. The mod preserves any challenge whose own maximum is already higher.

Status: built and installed for local testing; selecting and playing a challenge above 30 enemies has not yet been verified in game.
