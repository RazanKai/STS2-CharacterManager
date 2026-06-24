using System;
using System.Linq;
using System.Reflection;
using CharacterManager.Config;
using CharacterManager.Multiplayer;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;

namespace CharacterManager.Patches
{
    /// <summary>
    /// Networking glue for the multiplayer random pool. While a lobby exists, each peer:
    /// <list type="bullet">
    /// <item>listens for <see cref="RandomPoolMessage"/> and stores every other player's advertised
    /// pool (keyed by net id) so it can resolve their Random slot the same way they would, and</item>
    /// <item>re-broadcasts its own pool whenever the local player edits it (via
    /// <see cref="RandomPoolStore.PoolChanged"/>).</item>
    /// </list>
    /// Singleplayer is unaffected — broadcasts are skipped and resolution falls back to the local pool.
    /// </summary>
    public static class RandomPoolNet
    {
        private static INetGameService? _net;
        private static StartRunLobby? _lobby;
        // The NetService our receive handler is registered on. The handler is registered ONCE per
        // connection and never unregistered mid-connection (see Register), so we track which service
        // it belongs to and only register again when a genuinely new one appears.
        private static INetGameService? _handlerNet;

        internal static void Register(StartRunLobby? lobby)
        {
            try
            {
                if (lobby?.NetService == null) return;
                var net = lobby.NetService;
                StopSending(); // detach any previous lobby's send-side wiring (keeps the handler)
                _lobby = lobby;
                _net = net;
                RandomPoolStore.ClearRemotePools();

                // Register the receive handler once per NetService and KEEP it for the whole
                // connection (lobby through run). The connection persists into the run, and BaseLib's
                // CustomMessageWrapper throws KeyNotFoundException if a packet for our message type
                // arrives after its key was removed — which is exactly what unregistering on CleanUp
                // used to do, crashing the client's combat sync (black screen). The handler dies with
                // its NetService, so there's nothing to leak.
                if (!ReferenceEquals(_handlerNet, net))
                {
                    net.RegisterMessageHandler<RandomPoolMessage>(OnReceive);
                    _handlerNet = net;
                }

                RandomPoolStore.PoolChanged += OnLocalPoolChanged;
                // Re-send our pool whenever a peer connects, so a player who set their pool before
                // someone joined doesn't leave that joiner without it (which would diverge the random
                // draw and trip the game's hard "Character changed" rejection).
                _lobby.PlayerConnected += OnPlayerConnected;
                // Advertise our current pool up front for whoever is already here.
                BroadcastLocalPool();
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool: failed to hook lobby networking: " + e.Message);
            }
        }

        /// <summary>
        /// Detach the send-side wiring (PoolChanged / PlayerConnected) and stop broadcasting. Called on
        /// lobby CleanUp and before re-registering. Deliberately leaves the receive handler registered
        /// — removing its key mid-connection is what crashed the client (see Register).
        /// </summary>
        internal static void StopSending()
        {
            try
            {
                RandomPoolStore.PoolChanged -= OnLocalPoolChanged;
                if (_lobby != null) _lobby.PlayerConnected -= OnPlayerConnected;
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool: failed to unhook lobby networking: " + e.Message);
            }
            finally
            {
                _net = null;
                _lobby = null;
                RandomPoolStore.ClearRemotePools();
            }
        }

        // A peer just connected — resend our pool so they have it well before run-start.
        private static void OnPlayerConnected(LobbyPlayer _) => BroadcastLocalPool();

        /// <summary>Send the local player's current pool to all peers. No-op in singleplayer.</summary>
        public static void BroadcastLocalPool()
        {
            try
            {
                if (_net == null || _net.Type == NetGameType.Singleplayer) return;
                var msg = new RandomPoolMessage { Pool = RandomPoolStore.BuildLocalPool() };
                _net.SendMessage(msg);
            }
            catch (Exception e)
            {
                Log.Warn("[CharacterManager] random pool: broadcast failed: " + e.Message);
            }
        }

        private static void OnReceive(RandomPoolMessage msg, ulong senderNetId)
        {
            // Storing our own echoed broadcast is harmless: PoolForPlayer uses the local config for
            // the local player and only consults the remote map for everyone else.
            RandomPoolStore.SetRemotePool(senderNetId, msg.Pool);
        }

        private static void OnLocalPoolChanged() => BroadcastLocalPool();
    }

    /// <summary>Registers our message handler when a lobby is created (host or client).</summary>
    [HarmonyPatch]
    public static class RandomPoolLobbyCtorPatch
    {
        private static MethodBase? TargetMethod()
        {
            // Patch the 4-arg ctor; the 5-arg overload chains to it, so this covers both paths.
            var ctor = AccessTools.GetDeclaredConstructors(typeof(StartRunLobby))
                .FirstOrDefault(c => c.GetParameters().Length == 4);
            if (ctor == null)
                Log.Warn("[CharacterManager] random pool: StartRunLobby 4-arg ctor not found; MP sync disabled.");
            return ctor;
        }

        private static void Postfix(StartRunLobby __instance)
        {
            RandomPoolNet.Register(__instance);
        }
    }

    /// <summary>
    /// Detaches the send side when the lobby closes or transitions to a run. The receive handler is
    /// intentionally left registered for the connection's lifetime (see <see cref="RandomPoolNet"/>).
    /// </summary>
    [HarmonyPatch(typeof(StartRunLobby), "CleanUp")]
    public static class RandomPoolLobbyCleanupPatch
    {
        private static void Postfix() => RandomPoolNet.StopSending();
    }
}
