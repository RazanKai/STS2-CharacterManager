using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Modding;

namespace CharacterManager
{
    /// <summary>
    /// Helpers for enumerating playable characters, including modded ones.
    ///
    /// <c>ModelDb.AllCharacters</c> is a hardcoded array of the 5 base characters only.
    /// The full registry (base + modded) lives in the private <c>ModelDb._contentById</c>
    /// dictionary, which we read via reflection.
    /// </summary>
    public static class CharacterHelper
    {
        private static readonly FieldInfo? ContentByIdField =
            AccessTools.Field(typeof(ModelDb), "_contentById");

        private static HashSet<ModelId>? _baseIds;

        private static HashSet<ModelId> BaseIds => _baseIds ??= new HashSet<ModelId>
        {
            ModelDb.Character<Ironclad>().Id,
            ModelDb.Character<Silent>().Id,
            ModelDb.Character<Regent>().Id,
            ModelDb.Character<Necrobinder>().Id,
            ModelDb.Character<Defect>().Id,
        };

        public static bool IsBaseCharacter(ModelId id) => BaseIds.Contains(id);

        /// <summary>
        /// All playable characters (base + custom), sorted: base characters first in
        /// their canonical order, then custom characters alphabetically by title.
        ///
        /// <para>There are TWO runtime sources of characters and they overlap:</para>
        /// <list type="bullet">
        ///   <item><c>ModelDb.AllCharacters</c> — a hardcoded array of the 5 base
        ///   characters that character libraries (BaseLib, KitLib, RitsuLib) and the
        ///   character mods themselves Harmony-patch (<c>get_AllCharacters</c>) to append
        ///   their modded characters. So at runtime this often already contains the full
        ///   roster.</item>
        ///   <item><c>ModelDb._contentById</c> — the model registry, which also holds every
        ///   registered custom character.</item>
        /// </list>
        /// <para>Because a modded character is reachable through BOTH sources, naively
        /// concatenating them lists each custom character twice. The fix is to merge both
        /// and deduplicate across the WHOLE result by <see cref="ModelId"/> (the key every
        /// store in this mod uses). Earlier fixes deduped only within the registry slice and
        /// so never collapsed an <c>AllCharacters</c> entry against a registry entry.</para>
        /// </summary>
        public static List<CharacterModel> GetAllCharacters()
        {
            var seen = new HashSet<ModelId>();
            var bases = new List<CharacterModel>();
            var customs = new List<CharacterModel>();

            void Consider(CharacterModel? c)
            {
                if (c == null || !c.IsPlayable || !seen.Add(c.Id)) return;
                (IsBaseCharacter(c.Id) ? bases : customs).Add(c);
            }

            foreach (var c in ModelDb.AllCharacters) Consider(c);
            foreach (var c in EnumerateRegistryCharacters()) Consider(c);

            customs.Sort((a, b) => string.Compare(
                a.Title.GetFormattedText(), b.Title.GetFormattedText(),
                StringComparison.OrdinalIgnoreCase));

            var result = new List<CharacterModel>(bases.Count + customs.Count);
            result.AddRange(bases);   // canonical base order, preserved from AllCharacters
            result.AddRange(customs);
            return result;
        }

        /// <summary>
        /// All installed custom (non-base) playable characters, alphabetically by title.
        /// Derived from <see cref="GetAllCharacters"/> so the manager list and the
        /// Compendium stats injection always see the exact same deduplicated set.
        /// </summary>
        public static List<CharacterModel> GetCustomCharacters()
        {
            return GetAllCharacters().Where(c => !IsBaseCharacter(c.Id)).ToList();
        }

        /// <summary>Yields every <see cref="CharacterModel"/> in the model registry (base + custom).</summary>
        private static IEnumerable<CharacterModel> EnumerateRegistryCharacters()
        {
            if (ContentByIdField?.GetValue(null) is IDictionary<ModelId, AbstractModel> dict)
            {
                foreach (var model in dict.Values)
                {
                    if (model is CharacterModel c)
                        yield return c;
                }
            }
        }

        /// <summary>
        /// Finds which loaded mod provides a character by matching assembly identity.
        /// Returns null for base-game characters.
        /// </summary>
        public static Mod? GetSourceMod(CharacterModel character)
        {
            var charAssembly = character.GetType().Assembly;
            foreach (var mod in ModManager.GetLoadedMods())
            {
                if (mod.assembly == charAssembly)
                    return mod;
            }
            return null;
        }
    }
}
