using System.Linq;
using Content.Shared.GameTicking;
using Content.Server.Station.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using System.Text;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;
using Content.Shared.NPC.Components;
namespace Content.Server.GameTicking
{
    public sealed partial class GameTicker
    {
        [ViewVariables]
        private readonly Dictionary<NetUserId, PlayerGameStatus> _playerGameStatuses = new();

        [ViewVariables]
        private TimeSpan _roundStartTime;

        /// <summary>
        /// How long before RoundStartTime do we load maps.
        /// </summary>
        [ViewVariables]
        public TimeSpan RoundPreloadTime { get; } = TimeSpan.FromSeconds(15);

        [ViewVariables]
        private TimeSpan _pauseTime;

        [ViewVariables]
        public new bool Paused { get; set; }

        [ViewVariables]
        private bool _roundStartCountdownHasNotStartedYetDueToNoPlayers;

        /// <summary>
        /// The game status of a players user Id. May contain disconnected players
        /// </summary>
        public IReadOnlyDictionary<NetUserId, PlayerGameStatus> PlayerGameStatuses => _playerGameStatuses;

        public void UpdateInfoText()
        {
            RaiseNetworkEvent(GetInfoMsg(), Filter.Empty().AddPlayers(_playerManager.NetworkedSessions));
            RaiseNetworkEvent(GetPlayerCountsMsg(), Filter.Empty().AddPlayers(_playerManager.NetworkedSessions)); // Send faction counts too
        }

        private string GetInfoText()
        {
            var preset = CurrentPreset ?? Preset;
            if (preset == null)
            {
                return string.Empty;
            }

            var playerCount = $"{_playerManager.PlayerCount}";
            var readyCount = _playerGameStatuses.Values.Count(x => x == PlayerGameStatus.ReadyToPlay);

            var stationNames = new StringBuilder();
            var query =
                EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent, MetaDataComponent>();

            var foundOne = false;

            while (query.MoveNext(out _, out _, out var meta))
            {
                foundOne = true;
                if (stationNames.Length > 0)
                    stationNames.Append('\n');

                stationNames.Append(meta.EntityName);
            }

            if (!foundOne)
            {
                stationNames.Append(_gameMapManager.GetSelectedMap()?.MapName ??
                                    Loc.GetString("game-ticker-no-map-selected"));
            }

            var gmTitle = Loc.GetString(preset.ModeTitle);
            var desc = Loc.GetString(preset.Description);
            return Loc.GetString(
                RunLevel == GameRunLevel.PreRoundLobby
                    ? "game-ticker-get-info-preround-text"
                    : "game-ticker-get-info-text",
                ("roundId", RoundId),
                ("playerCount", playerCount),
                ("readyCount", readyCount),
                ("mapName", stationNames.ToString()),
                ("gmTitle", gmTitle),
                ("desc", desc));
        }

        private TickerConnectionStatusEvent GetConnectionStatusMsg()
        {
            return new TickerConnectionStatusEvent(RoundStartTimeSpan);
        }

        private TickerLobbyStatusEvent GetStatusMsg(ICommonSession session)
        {
            _playerGameStatuses.TryGetValue(session.UserId, out var status);
            return new TickerLobbyStatusEvent(RunLevel != GameRunLevel.PreRoundLobby, LobbyBackground, status == PlayerGameStatus.ReadyToPlay, _roundStartTime, RoundPreloadTime, RoundStartTimeSpan, Paused);
        }

        private void SendStatusToAll()
        {
            foreach (var player in _playerManager.Sessions)
            {
                RaiseNetworkEvent(GetStatusMsg(player), player.Channel);
            }
        }

        private TickerLobbyInfoEvent GetInfoMsg()
        {
            return new(GetInfoText());
        }

        private GetPlayerFactionCounts GetPlayerCountsMsg()
        {
            return new(GetPlayerFactionCounts());
        }
        private void UpdateLateJoinStatus()
        {
            RaiseNetworkEvent(new TickerLateJoinStatusEvent(DisallowLateJoin));
        }

        public bool PauseStart(bool pause = true)
        {
            if (Paused == pause)
            {
                return false;
            }

            Paused = pause;

            if (pause)
            {
                _pauseTime = _gameTiming.CurTime;
            }
            else if (_pauseTime != default)
            {
                _roundStartTime += _gameTiming.CurTime - _pauseTime;
            }

            RaiseNetworkEvent(new TickerLobbyCountdownEvent(_roundStartTime, Paused));

            _chatManager.DispatchServerAnnouncement(Loc.GetString(Paused
                ? "game-ticker-pause-start"
                : "game-ticker-pause-start-resumed"));

            return true;
        }

        public bool TogglePause()
        {
            PauseStart(!Paused);
            return Paused;
        }

        public void ToggleReadyAll(bool ready)
        {
            var status = ready ? PlayerGameStatus.ReadyToPlay : PlayerGameStatus.NotReadyToPlay;
            foreach (var playerUserId in _playerGameStatuses.Keys)
            {
                _playerGameStatuses[playerUserId] = status;
                if (!_playerManager.TryGetSessionById(playerUserId, out var playerSession))
                    continue;
                RaiseNetworkEvent(GetStatusMsg(playerSession), playerSession.Channel);
            }
        }

        public void ToggleReady(ICommonSession player, bool ready)
        {
            if (!_playerGameStatuses.ContainsKey(player.UserId))
                return;

            if (!_userDb.IsLoadComplete(player))
                return;

            if (RunLevel != GameRunLevel.PreRoundLobby)
            {
                return;
            }

            var status = ready ? PlayerGameStatus.ReadyToPlay : PlayerGameStatus.NotReadyToPlay;
            _playerGameStatuses[player.UserId] = ready ? PlayerGameStatus.ReadyToPlay : PlayerGameStatus.NotReadyToPlay;
            RaiseNetworkEvent(GetStatusMsg(player), player.Channel);
            // update server info to reflect new ready count
            UpdateInfoText();
        }
        public Dictionary<ProtoId<NpcFactionPrototype>, int> GetPlayerFactionCounts()
        {
            var factionCounts = new Dictionary<ProtoId<NpcFactionPrototype>, int>();

            // Iterate through all currently connected player sessions on the server
            foreach (var session in _playerManager.Sessions)
            {
                // Get the entity currently controlled by the player
                if (session.AttachedEntity is not { Valid: true } playerEntity)
                    continue;

                // Try to get the NpcFactionMemberComponent from the player's controlled entity
                if (TryComp<NpcFactionMemberComponent>(playerEntity, out var factionMemberComponent))
                {
                    // A player can be part of multiple factions
                    foreach (var factionId in factionMemberComponent.Factions)
                    {
                        factionCounts.TryGetValue(factionId, out var currentCount);
                        factionCounts[factionId] = currentCount + 1;
                    }
                }
            }
            return factionCounts;
        }
        public bool UserHasJoinedGame(ICommonSession session)
            => UserHasJoinedGame(session.UserId);

        public bool UserHasJoinedGame(NetUserId userId)
            => PlayerGameStatuses.TryGetValue(userId, out var status) && status == PlayerGameStatus.JoinedGame;
    }
}
