using System;
using System.Collections.Generic;
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
        /// </summary>
        public static List<CharacterModel> GetAllCharacters()
        {
            var custom = new List<CharacterModel>();
            // Use reference equality to deduplicate — _contentById can store the same
            // CharacterModel instance under multiple ModelId keys.
            var seen = new HashSet<CharacterModel>(ReferenceEqualityComparer.Instance);
            if (ContentByIdField?.GetValue(null) is IDictionary<ModelId, AbstractModel> dict)
            {
                foreach (var model in dict.Values)
                {
                    if (model is CharacterModel c && c.IsPlayable && !IsBaseCharacter(c.Id) && seen.Add(c))
                        custom.Add(c);
                }
            }
            custom.Sort((a, b) => string.Compare(
                a.Title.GetFormattedText(), b.Title.GetFormattedText(),
                StringComparison.OrdinalIgnoreCase));

            var result = new List<CharacterModel>();
            result.AddRange(ModelDb.AllCharacters);
            result.AddRange(custom);
            return result;
        }

        /// <summary>All installed custom (non-base) playable characters, alphabetically.</summary>
        public static List<CharacterModel> GetCustomCharacters()
        {
            var result = new List<CharacterModel>();
            var seen = new HashSet<CharacterModel>(ReferenceEqualityComparer.Instance);
            if (ContentByIdField?.GetValue(null) is IDictionary<ModelId, AbstractModel> dict)
            {
                foreach (var model in dict.Values)
                {
                    if (model is CharacterModel c && c.IsPlayable && !IsBaseCharacter(c.Id) && seen.Add(c))
                        result.Add(c);
                }
            }
            result.Sort((a, b) => string.Compare(
                a.Title.GetFormattedText(), b.Title.GetFormattedText(),
                StringComparison.OrdinalIgnoreCase));
            return result;
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
