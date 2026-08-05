namespace AtomPix.Core.Licensing;

public enum FeatureId
{
    SingleCompress,
    BatchCompress,
    SingleConvert,
    BatchConvert,
    WebpExport,
    MetadataControl,
    ResizeOnExport,
    AdvancedCompressionProfile
}

public enum SubscriptionStatus
{
    Free,
    Active,
    Expired
}

public enum BillingCycle
{
    Monthly,
    Quarterly,
    Yearly
}

public sealed record SubscriptionState
{
    public SubscriptionState(SubscriptionStatus status, BillingCycle? billingCycle, DateTimeOffset? expiresAt)
    {
        switch (status)
        {
            case SubscriptionStatus.Free:
                if (billingCycle is not null || expiresAt is not null)
                {
                    throw new ArgumentException("Free subscription cannot carry billing cycle or expiration.");
                }
                break;
            case SubscriptionStatus.Active:
                if (billingCycle is null || expiresAt is null)
                {
                    throw new ArgumentException("Active subscription requires billing cycle and expiration.");
                }
                break;
            case SubscriptionStatus.Expired:
                if (expiresAt is null)
                {
                    throw new ArgumentException("Expired subscription requires expiration.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported subscription status.");
        }

        Status = status;
        BillingCycle = billingCycle;
        ExpiresAt = expiresAt;
    }

    public SubscriptionStatus Status { get; }

    public BillingCycle? BillingCycle { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public static SubscriptionState Free { get; } = new(SubscriptionStatus.Free, null, null);
}

public interface IFeatureAccessPolicy
{
    FeatureAccessDecision CanUse(FeatureId feature, SubscriptionState subscription);
}

public sealed class DefaultFeatureAccessPolicy : IFeatureAccessPolicy
{
    private static readonly HashSet<FeatureId> FreeFeatures =
    [
        FeatureId.SingleCompress,
        FeatureId.SingleConvert
    ];

    public FeatureAccessDecision CanUse(FeatureId feature, SubscriptionState subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (subscription.Status == SubscriptionStatus.Active)
        {
            return FeatureAccessDecision.Allow();
        }

        if (FreeFeatures.Contains(feature))
        {
            return FeatureAccessDecision.Allow();
        }

        return FeatureAccessDecision.Deny(
            subscription.Status == SubscriptionStatus.Expired
                ? FeatureAccessBlockReason.SubscriptionExpired
                : FeatureAccessBlockReason.SubscriptionRequired);
    }
}

public sealed record FeatureAccessDecision
{
    private FeatureAccessDecision(bool allowed, FeatureAccessBlockReason? blockReason)
    {
        if (allowed && blockReason is not null)
        {
            throw new ArgumentException("Allowed feature decisions cannot carry a block reason.", nameof(blockReason));
        }

        if (!allowed && blockReason is null)
        {
            throw new ArgumentNullException(nameof(blockReason), "Denied feature decisions must carry a block reason.");
        }

        Allowed = allowed;
        BlockReason = blockReason;
    }

    public bool Allowed { get; }

    public FeatureAccessBlockReason? BlockReason { get; }

    public static FeatureAccessDecision Allow() => new(true, null);

    public static FeatureAccessDecision Deny(FeatureAccessBlockReason reason) => new(false, reason);
}

public enum FeatureAccessBlockReason
{
    SubscriptionRequired,
    SubscriptionExpired
}