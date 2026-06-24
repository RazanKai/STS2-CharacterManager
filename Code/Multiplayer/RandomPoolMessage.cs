using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace CharacterManager.Multiplayer
{
    /// <summary>
    /// Broadcast by a player to advertise the character pool their <b>Random</b> pick may draw from.
    ///
    /// <para><b>Why this exists.</b> The random character is not networked as a result — every peer
    /// re-derives every player's pick inside <c>StartRunLobby.BeginRunLocally</c> from the shared seed
    /// (see <see cref="Patches.RandomPoolPatch"/>). For our pool filter to stay deterministic across
    /// peers, the set each player draws from must be identical on every machine. So each player
    /// broadcasts their own pool here (mirroring how the game broadcasts each player's character
    /// choice via <c>LobbyPlayerChangedCharacterMessage</c>), and every peer resolves that player's
    /// slot from the synced copy. The owner of a slot uses its local config; everyone else uses this
    /// advertised list — they are the same set in the same order, so <c>rng.NextItem</c> agrees.</para>
    ///
    /// <para><b>Reliable, NOT buffered.</b> We deliberately do <i>not</i> buffer (unlike
    /// <c>LobbyPlayerChangedCharacterMessage</c>). A buffered custom message survives the lobby and is
    /// replayed by the net layer after the run starts — but by then our <c>CleanUp</c> patch has
    /// unregistered the handler, so BaseLib's <c>CustomMessageWrapper.Deserialize</c> can no longer
    /// find this message type's key and throws <c>KeyNotFoundException</c> on the client's network
    /// thread, killing combat sync (black screen). Late joiners are covered instead by re-broadcasting
    /// on <c>StartRunLobby.PlayerConnected</c> (see <see cref="Patches.RandomPoolNet"/>), so buffering
    /// buys us nothing and only creates that orphaned-replay hazard. The payload is serialized by
    /// model id via <c>WriteModelList</c>/<c>ReadModelList</c>, correct for modded characters as long
    /// as both peers have the same character mods installed.</para>
    /// </summary>
    public struct RandomPoolMessage : INetMessage, IPacketSerializable
    {
        /// <summary>The owning player's pool, in draw order. Resolved by model id on each peer.</summary>
        public List<CharacterModel> Pool;

        public bool ShouldBroadcast => true;

        public NetTransferMode Mode => NetTransferMode.Reliable;

        public LogLevel LogLevel => LogLevel.VeryDebug;

        // Must stay false — see the class remarks: a buffered copy replayed into the run after the
        // handler is unregistered crashes the client in BaseLib's CustomMessageWrapper.
        public bool ShouldBuffer => false;

        public void Serialize(PacketWriter writer)
        {
            writer.WriteModelList(Pool ?? new List<CharacterModel>());
        }

        public void Deserialize(PacketReader reader)
        {
            Pool = reader.ReadModelList<CharacterModel>();
        }
    }
}
