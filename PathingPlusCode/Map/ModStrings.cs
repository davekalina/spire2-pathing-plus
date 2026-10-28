using MegaCrit.Sts2.Core.Localization;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The mod's own entries in the game's localisation tables.
///
/// Several native widgets take their text as a <c>LocString</c> — a table name and a
/// key — and resolve it by reading the table. A hover tip is one; every row of
/// Settings → Input is another. There is no constructor that takes a literal, and
/// missing keys are not tolerated: <c>LocTable.GetRawText</c> throws, and it is called
/// outside the formatter's own catch, so the exception surfaces inside whatever native
/// <c>_Ready</c> was building the widget.
///
/// So the entries are put in the table instead. <c>LocTable.MergeWith</c> is public and
/// adds keys without disturbing any, which makes borrowing a native widget's text path
/// a two-line affair rather than a reason to hand-build a lookalike.
///
/// **Entries do not survive a language change.** <c>LocManager</c> replaces its whole
/// table dictionary when the language is set, taking the merged keys with it. So every
/// caller re-asserts its entries at the point of use rather than once at startup, which
/// is cheap — a dictionary write — and is the only version of this that stays correct.
/// </summary>
internal static class ModStrings
{
    /// <summary>
    /// Put <paramref name="text" /> at <paramref name="key" />, and say whether it took.
    ///
    /// Quietly false before the game has made its <c>LocManager</c>: the loader runs mod
    /// initializers first and builds the tables after, so there is nothing to merge into
    /// yet. That is not a failure worth a stack trace in every player's log — the
    /// callers re-assert at the point of use, which is always later.
    /// </summary>
    public static bool Ensure(string table, string key, string text) =>
        LocManager.Instance is { } loc &&
        Guard.Run($"Registering the {table} text", () =>
        {
            loc.GetTable(table).MergeWith(new Dictionary<string, string>
            {
                [key] = text,
            });
            return true;
        }, false);
}
