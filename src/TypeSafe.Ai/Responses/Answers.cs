namespace TypeSafe.Ai;

/// <summary>An answer returned for a named question.</summary>
public abstract class Answer
{
    /// <summary>Gets the wire discriminator.</summary>
    public abstract string Type { get; }
}

/// <summary>A probability for a noul question.</summary>
public sealed class NoulAnswer : Answer
{
    /// <inheritdoc />
    public override string Type => "noul";

    /// <summary>Gets the probability of a true answer.</summary>
    public double Noul { get; init; }
}

/// <summary>A selected label and its probabilities.</summary>
public sealed class ChoiceAnswer : Answer
{
    /// <inheritdoc />
    public override string Type => "choice";

    /// <summary>Gets the selected label.</summary>
    public string Choice { get; init; } = string.Empty;

    /// <summary>Gets the reported confidence.</summary>
    public double Confidence { get; init; }

    /// <summary>Gets probabilities keyed by label.</summary>
    public IReadOnlyDictionary<string, double> Probabilities { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
}

/// <summary>An expected score, rubric legend, and probabilities.</summary>
public sealed class ScoreAnswer : Answer
{
    /// <inheritdoc />
    public override string Type => "score";

    /// <summary>Gets the expected score.</summary>
    public double Score { get; init; }

    /// <summary>Gets the reported confidence.</summary>
    public double Confidence { get; init; }

    /// <summary>Gets rubric descriptions keyed by integer score.</summary>
    public IReadOnlyDictionary<int, EntryValue?> Legend { get; init; } =
        new Dictionary<int, EntryValue?>();

    /// <summary>Gets probabilities keyed by integer score.</summary>
    public IReadOnlyDictionary<int, double> Probabilities { get; init; } =
        new Dictionary<int, double>();
}
