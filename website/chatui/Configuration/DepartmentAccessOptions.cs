namespace chatui.Configuration;

public sealed class DepartmentAccessOptions
{
    public const string SectionName = "DepartmentAccess";

    public bool Enabled { get; init; }
    public Dictionary<string, DepartmentSubscription> Departments { get; init; } = [];

    public bool IsValid() => !Enabled ||
        (Departments.Count > 0
         && Departments.All(entry =>
             !string.IsNullOrWhiteSpace(entry.Key)
             && Guid.TryParse(entry.Value.GroupObjectId, out _)
             && !string.IsNullOrWhiteSpace(entry.Value.SubscriptionId)
             && !string.IsNullOrWhiteSpace(entry.Value.KeySecretName))
         && Departments.Values.Select(value => value.GroupObjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Departments.Count
         && Departments.Values.Select(value => value.SubscriptionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Departments.Count);

    public KeyValuePair<string, DepartmentSubscription> Resolve(IEnumerable<string> groupIds)
    {
        if (!Enabled || !IsValid())
            throw new InvalidOperationException("Department access is disabled or invalid.");

        var groups = groupIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = Departments.Where(entry => groups.Contains(entry.Value.GroupObjectId)).ToArray();
        if (matches.Length != 1)
            throw new DepartmentAccessDeniedException();

        return matches[0];
    }
}

public sealed class DepartmentSubscription
{
    public string GroupObjectId { get; init; } = "";
    public string SubscriptionId { get; init; } = "";
    public string KeySecretName { get; init; } = "";
}

public sealed class DepartmentAccessDeniedException : Exception;