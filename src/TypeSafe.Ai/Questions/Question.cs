namespace TypeSafe.Ai;

/// <summary>A question sent to the system-one endpoint.</summary>
public abstract class Question
{
    /// <summary>Gets the wire discriminator.</summary>
    public abstract string Type { get; }
}

/// <summary>A yes/no question with optional descriptions for either outcome.</summary>
public sealed class NoulQuestion : Question
{
    private EntryValue? _instructions;
    private NoulCriteria? _criteria;

    /// <inheritdoc />
    public override string Type => "noul";

    /// <summary>Gets the question instructions, or null when explicitly sent as JSON null.</summary>
    public EntryValue? Instructions
    {
        get => _instructions;
        init
        {
            _instructions = value;
            InstructionsSpecified = true;
        }
    }

    /// <summary>Gets the optional yes/no descriptions.</summary>
    public NoulCriteria? Criteria
    {
        get => _criteria;
        init
        {
            _criteria = value;
            CriteriaSpecified = true;
        }
    }

    internal bool InstructionsSpecified { get; private set; }

    internal bool CriteriaSpecified { get; private set; }

    internal static NoulQuestion Create(EntryValue? instructions, bool criteriaSpecified, NoulCriteria? criteria)
    {
        var question = new NoulQuestion { Instructions = instructions };
        if (criteriaSpecified)
        {
            question.SetCriteria(criteria);
        }

        return question;
    }

    internal void SetInstructions(EntryValue? value)
    {
        _instructions = value;
        InstructionsSpecified = true;
    }

    internal void SetCriteria(NoulCriteria? value)
    {
        _criteria = value;
        CriteriaSpecified = true;
    }
}

/// <summary>Optional descriptions for the true and false outcomes of a noul question.</summary>
public sealed class NoulCriteria
{
    private EntryValue? _true;
    private EntryValue? _false;

    /// <summary>Gets the true-outcome description.</summary>
    public EntryValue? True
    {
        get => _true;
        init
        {
            _true = value;
            TrueSpecified = true;
        }
    }

    /// <summary>Gets the false-outcome description.</summary>
    public EntryValue? False
    {
        get => _false;
        init
        {
            _false = value;
            FalseSpecified = true;
        }
    }

    internal bool TrueSpecified { get; private set; }

    internal bool FalseSpecified { get; private set; }

    internal void SetTrue(EntryValue? value)
    {
        _true = value;
        TrueSpecified = true;
    }

    internal void SetFalse(EntryValue? value)
    {
        _false = value;
        FalseSpecified = true;
    }
}

/// <summary>A question that selects one named alternative.</summary>
public sealed class ChoiceQuestion : Question
{
    private EntryValue? _instructions;
    private IReadOnlyDictionary<string, EntryValue?> _criteria =
        new Dictionary<string, EntryValue?>(StringComparer.Ordinal);

    /// <inheritdoc />
    public override string Type => "choice";

    /// <summary>Gets the optional question instructions.</summary>
    public EntryValue? Instructions
    {
        get => _instructions;
        init
        {
            _instructions = value;
            InstructionsSpecified = true;
        }
    }

    /// <summary>Gets the label-to-description map.</summary>
    public IReadOnlyDictionary<string, EntryValue?> Criteria
    {
        get => _criteria;
        init => _criteria = value;
    }

    internal bool InstructionsSpecified { get; private set; }

    internal void SetInstructions(EntryValue? value)
    {
        _instructions = value;
        InstructionsSpecified = true;
    }

    internal void SetCriteria(IReadOnlyDictionary<string, EntryValue?> value) => _criteria = value;
}

/// <summary>A question that assigns an expected score from an ordered rubric.</summary>
public sealed class ScoreQuestion : Question
{
    private EntryValue? _instructions;
    private IReadOnlyList<EntryValue?> _criteria = Array.Empty<EntryValue?>();

    /// <inheritdoc />
    public override string Type => "score";

    /// <summary>Gets the optional question instructions.</summary>
    public EntryValue? Instructions
    {
        get => _instructions;
        init
        {
            _instructions = value;
            InstructionsSpecified = true;
        }
    }

    /// <summary>Gets the ordered score descriptions.</summary>
    public IReadOnlyList<EntryValue?> Criteria
    {
        get => _criteria;
        init => _criteria = value;
    }

    internal bool InstructionsSpecified { get; private set; }

    internal void SetInstructions(EntryValue? value)
    {
        _instructions = value;
        InstructionsSpecified = true;
    }

    internal void SetCriteria(IReadOnlyList<EntryValue?> value) => _criteria = value;
}
