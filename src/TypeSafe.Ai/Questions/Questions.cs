namespace TypeSafe.Ai;

/// <summary>Builds and validates TypeSafe questions.</summary>
public static class Questions
{
    /// <summary>Creates a noul question with explicit JSON-null instructions and omitted criteria.</summary>
    public static NoulQuestion Noul() => Noul(null, criteriaSpecified: false, criteria: null);

    /// <summary>Creates a noul question with criteria omitted.</summary>
    public static NoulQuestion Noul(EntryValue? instructions) =>
        Noul(instructions, criteriaSpecified: false, criteria: null);

    /// <summary>Creates a noul question with explicitly supplied criteria, including null.</summary>
    public static NoulQuestion Noul(EntryValue? instructions, NoulCriteria? criteria) =>
        Noul(instructions, criteriaSpecified: true, criteria);

    /// <summary>Creates a choice question and preserves its criteria map unchanged.</summary>
    public static ChoiceQuestion Choice(
        EntryValue? instructions,
        IReadOnlyDictionary<string, EntryValue?> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return new ChoiceQuestion { Instructions = instructions, Criteria = criteria };
    }

    /// <summary>Creates a score question with at least two ordered descriptions.</summary>
    public static ScoreQuestion Score(
        EntryValue? instructions,
        IReadOnlyList<EntryValue?> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count < 2)
        {
            throw new TypeSafeException(
                "Score criteria must contain at least two descriptions indexed by score from zero.");
        }

        return new ScoreQuestion { Instructions = instructions, Criteria = criteria };
    }

    /// <summary>Rejects empty question sets and malformed score questions before transport.</summary>
    public static void Validate(IReadOnlyDictionary<string, Question> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
        {
            throw new TypeSafeException("At least one question is required.");
        }

        foreach ((string name, Question? question) in questions)
        {
            if (question is null)
            {
                throw new TypeSafeException($"Question \"{name}\" cannot be null.");
            }

            if (question is ScoreQuestion score && score.Criteria.Count < 2)
            {
                throw new TypeSafeException(
                    $"Score question \"{name}\" has {score.Criteria.Count} criteria; " +
                    "at least two scores are required.");
            }
        }
    }

    private static NoulQuestion Noul(
        EntryValue? instructions,
        bool criteriaSpecified,
        NoulCriteria? criteria) =>
        NoulQuestion.Create(instructions, criteriaSpecified, criteria);
}
