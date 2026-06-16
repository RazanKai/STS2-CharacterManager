using MegaCrit.Sts2.Core.Models;

namespace CharacterManager.Config
{
    /// <summary>
    /// Holds the currently-active run-history character filter.
    /// Set before pushing NRunHistory; cleared in OnSubmenuClosed.
    /// </summary>
    public static class RunHistoryFilter
    {
        /// <summary>
        /// The character to filter by, or null for the global unfiltered view.
        /// </summary>
        public static ModelId? Character { get; set; }
    }
}
