using Content.Shared.Civ14.CivFactions;
using JetBrains.Annotations;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Client.Commands
{
    [UsedImplicitly]
    public sealed class AcceptFactionInviteCommand : IConsoleCommand
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;

        public string Command => "acceptfactioninvite";
        public string Description => "Accepts an invitation to join a faction.";
        public string Help => $"Usage: {Command} \"<faction_name>\" \"<inviter_user_id>\"";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length != 2)
            {
                shell.WriteError("Invalid number of arguments.");
                shell.WriteLine(Help);
                return;
            }

            var factionName = args[0];
            var inviterUserIdStr = args[1];

            if (string.IsNullOrWhiteSpace(factionName))
            {
                shell.WriteError("Faction name cannot be empty.");
                return;
            }

            if (!Guid.TryParse(inviterUserIdStr, out var inviterGuid))
            {
                shell.WriteError($"Invalid inviter user ID format: {inviterUserIdStr}");
                return;
            }

            var inviterNetId = new NetUserId(inviterGuid);

            // Create and raise the network event to the server
            // AcceptFactionInviteEvent is defined in Content.Shared.Civ14.CivFactions
            // The server (CivFactionsSystem) handles this event.
            var acceptEvent = new AcceptFactionInviteEvent(factionName, inviterNetId);
            _entityManager.RaisePredictiveEvent(acceptEvent);

            shell.WriteLine($"Sent request to join faction: '{factionName}' (invited by: {inviterNetId}).");
        }
    }
}
