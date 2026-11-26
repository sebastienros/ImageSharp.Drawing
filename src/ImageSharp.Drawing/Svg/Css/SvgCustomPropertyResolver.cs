// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Computes the custom properties that one element declares: substitutes the <c>var()</c> references in their values,
/// in dependency order, and detects reference cycles, as CSS Custom Properties Level 1, section 2.3 defines.
/// </summary>
/// <remarks>
/// <para>
/// A reference to a property that the element itself declares resolves to the computed value of that declaration, which
/// is computed first (depth first). A reference to any other name resolves to the computed value of the parent, which is
/// already substituted. Every property on a reference cycle gets the guaranteed-invalid value; a property that only
/// refers to a cycle member gets that member's invalid value, so its own fallback applies.
/// </para>
/// <para>
/// The fallback of a <c>var()</c> is followed only when it is used, so a fallback reference closes a cycle only then.
/// </para>
/// </remarks>
internal sealed class SvgCustomPropertyResolver : SvgVariableSubstitution.ISource
{
    private readonly Dictionary<string, string?> own;
    private readonly Dictionary<string, string?>.AlternateLookup<ReadOnlySpan<char>> ownLookup;
    private readonly SvgCustomProperties? parent;
    private readonly SvgSubstitutionBudget budget;

    // The state of each declaration that has var(): absent while not visited, then InProgress or Done.
    private readonly Dictionary<string, State> states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, State>.AlternateLookup<ReadOnlySpan<char>> stateLookup;

    // The declarations being computed, innermost last, for marking the members of a cycle.
    private readonly List<string> stack = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCustomPropertyResolver"/> class.
    /// </summary>
    /// <param name="own">The declared values of the element, which the resolver replaces with computed values.</param>
    /// <param name="parent">The computed custom properties of the parent, or <see langword="null"/>.</param>
    /// <param name="budget">The substitution budget of the document.</param>
    private SvgCustomPropertyResolver(Dictionary<string, string?> own, SvgCustomProperties? parent, SvgSubstitutionBudget budget)
    {
        this.own = own;
        this.ownLookup = own.GetAlternateLookup<ReadOnlySpan<char>>();
        this.parent = parent;
        this.budget = budget;
        this.stateLookup = this.states.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>
    /// The progress of one declaration.
    /// </summary>
    private enum State
    {
        /// <summary>
        /// The value is being computed: a reference to it now is a cycle.
        /// </summary>
        InProgress,

        /// <summary>
        /// The value is on a cycle; it becomes the guaranteed-invalid value when its computation ends.
        /// </summary>
        Cyclic,

        /// <summary>
        /// The computed value is in the map.
        /// </summary>
        Done
    }

    /// <summary>
    /// Replaces each declared value that has <c>var()</c> with its computed value, or with <see langword="null"/>
    /// (the guaranteed-invalid value) when its substitution fails.
    /// </summary>
    /// <param name="own">The declared values of the element; <see langword="null"/> values are guaranteed-invalid already.</param>
    /// <param name="parent">The computed custom properties of the parent, or <see langword="null"/>.</param>
    /// <param name="budget">The substitution budget of the document.</param>
    public static void SubstituteDeclarations(Dictionary<string, string?> own, SvgCustomProperties? parent, SvgSubstitutionBudget budget)
    {
        SvgCustomPropertyResolver? resolver = null;
        string[]? names = null;
        foreach (KeyValuePair<string, string?> entry in own)
        {
            if (entry.Value is not null && SvgPropertyParser.ContainsVar(entry.Value))
            {
                // Values change below, so the names are copied before the first change.
                names ??= [.. own.Keys];
                resolver ??= new SvgCustomPropertyResolver(own, parent, budget);
            }
        }

        if (resolver is null)
        {
            return;
        }

        foreach (string name in names!)
        {
            _ = resolver.TryGetValue(name, 0, out _);
        }
    }

    /// <inheritdoc/>
    public bool TryGetValue(ReadOnlySpan<char> name, int depth, out string? value)
    {
        if (!this.ownLookup.TryGetValue(name, out value))
        {
            if (this.parent is not null)
            {
                return this.parent.TryGetValue(name, out value);
            }

            value = null;
            return false;
        }

        if (value is null || !SvgPropertyParser.ContainsVar(value))
        {
            return true;
        }

        if (this.stateLookup.TryGetValue(name, out State state))
        {
            if (state == State.Done)
            {
                value = this.ownLookup[name];
                return true;
            }

            // A reference back to a declaration that is being computed closes a cycle: every declaration on the stack from
            // that one on is a member.
            string key = this.ownLookup.TryGetValue(name, out string? actual, out _) ? actual : name.ToString();
            for (int i = this.stack.LastIndexOf(key); i >= 0 && i < this.stack.Count; i++)
            {
                this.states[this.stack[i]] = State.Cyclic;
            }

            value = null;
            return true;
        }

        string declared = value;
        string nameKey = this.ownLookup.TryGetValue(name, out string? existing, out _) ? existing : name.ToString();
        if (depth > SvgVariableSubstitution.MaxDepth)
        {
            this.own[nameKey] = null;
            this.states[nameKey] = State.Done;
            value = null;
            return true;
        }

        this.states[nameKey] = State.InProgress;
        this.stack.Add(nameKey);
        SvgVariableSubstitution.Outcome outcome = SvgVariableSubstitution.Substitute(declared, this, this.budget, depth, out string? result);
        this.stack.RemoveAt(this.stack.Count - 1);

        // A member of a cycle, a failed substitution, and a malformed var() all give the guaranteed-invalid value.
        if (this.states[nameKey] == State.Cyclic || outcome != SvgVariableSubstitution.Outcome.Substituted)
        {
            result = null;
        }

        this.own[nameKey] = result;
        this.states[nameKey] = State.Done;
        value = result;
        return true;
    }
}
